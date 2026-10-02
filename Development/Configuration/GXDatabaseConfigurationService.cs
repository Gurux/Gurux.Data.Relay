//
// --------------------------------------------------------------------------
//  Gurux Ltd
// 
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License 
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of 
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2. 
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using Gurux.Data.Relay.Realtime;
using System.Collections.Concurrent;
using System.Data.Common;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Log;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Enums;

namespace Gurux.Data.Relay.Configuration;

public sealed partial class GXDatabaseConfigurationService : IGXConfigurationService, IGXServerConfigurationChanges, IGXClientConfigurationChanges, IGXDataVaultConfigurationChanges, IGXDataVaultRuntimeStateStore
{
    private readonly IGXRelayChangePublisher? _changes;
    private readonly ILogger<GXDatabaseConfigurationService> _logger;

    public event Func<Task>? ClientConfigurationSaved;
    public event Func<Task>? ServerConfigurationSaved;
    public event Func<Task>? DataVaultConfigurationSaved;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ConfigurationGates = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _contentRootPath;
    private readonly GXConfigurationStoreSettings _storeSettings;
    private (DatabaseType Type, string ConnectionString)? _initializedStore;
    private readonly GXDatabaseConnectionFactory _connectionFactory = new();

    public GXDatabaseConfigurationService(IHostEnvironment hostEnvironment)
        : this(hostEnvironment, GXConfigurationStoreSettings.CreateDefault(hostEnvironment.ContentRootPath))
    {
    }

    public GXDatabaseConfigurationService(
        IHostEnvironment hostEnvironment,
        GXConfigurationStoreSettings storeSettings, IGXRelayChangePublisher? changes = null,
        ILogger<GXDatabaseConfigurationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(hostEnvironment);
        ArgumentNullException.ThrowIfNull(storeSettings);

        _contentRootPath = hostEnvironment.ContentRootPath;
        _storeSettings = storeSettings; _changes = changes;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GXDatabaseConfigurationService>.Instance;
    }

    public string GetConfigurationPath(ApplicationMode mode)
    {
        return $"{_storeSettings.Type}#{mode}";
    }

    public string GetStatePath(ApplicationMode mode)
        => $"{_storeSettings.Type}#RelayRuntimeState/{mode}";

    public Task<GXClientConfiguration?> LoadClientAsync(CancellationToken cancellationToken)
    {
        return LoadConfigurationAsync<GXClientConfiguration>(ApplicationMode.Client, cancellationToken);
    }

    public Task<GXClientState?> LoadClientStateAsync(CancellationToken cancellationToken)
    {
        return LoadStateAsync<GXClientState>(ApplicationMode.Client, cancellationToken);
    }

    public Task<GXServerConfiguration?> LoadServerAsync(CancellationToken cancellationToken)
    {
        return LoadConfigurationAsync<GXServerConfiguration>(ApplicationMode.Server, cancellationToken);
    }

    public Task<GXServerState?> LoadServerStateAsync(CancellationToken cancellationToken)
    {
        return LoadStateAsync<GXServerState>(ApplicationMode.Server, cancellationToken);
    }

    public Task<GXDataVaultConfiguration?> LoadDataVaultAsync(CancellationToken cancellationToken)
    {
        return LoadConfigurationAsync<GXDataVaultConfiguration>(ApplicationMode.DataVault, cancellationToken);
    }

    public Task SaveClientAsync(GXClientConfiguration configuration, CancellationToken cancellationToken)
        => SaveClientCoreAsync(configuration, cancellationToken, restoringArchive: false);

    public Task ImportClientAsync(GXClientConfiguration configuration, CancellationToken cancellationToken)
        => SaveClientCoreAsync(configuration, cancellationToken, restoringArchive: true);

    private async Task SaveClientCoreAsync(GXClientConfiguration configuration, CancellationToken cancellationToken, bool restoringArchive)
    {
        foreach (var table in configuration.Databases.SelectMany(database => database.Tables ?? []))
        {
            if (table.ChangeTracking.Type != ChangeTrackingType.ContentHash) continue;
            if (table.DeleteSourceRowsAfterTransfer || !string.IsNullOrWhiteSpace(table.IncrementalColumn))
                throw new InvalidOperationException("ContentHash cannot be combined with an incremental column or deleting source rows after transfer.");
            _ = new Gurux.Data.Relay.Client.GXContentHashTracker(table.Columns, table.Keys, table.ChangeTracking.HashColumns, null, 1);
        }
        await SaveConfigurationAsync(ApplicationMode.Client, configuration, cancellationToken, restoringArchive);
        if (ClientConfigurationSaved is { } handlers)
            foreach (Func<Task> handler in handlers.GetInvocationList()) await handler();
    }

    public Task SaveClientStateAsync(GXClientState state, CancellationToken cancellationToken)
    {
        return SaveStateAsync(ApplicationMode.Client, state, cancellationToken);
    }

    public Task SaveServerAsync(GXServerConfiguration configuration, CancellationToken cancellationToken)
        => SaveServerCoreAsync(configuration, cancellationToken, restoringArchive: false);

    public Task ImportServerAsync(GXServerConfiguration configuration, CancellationToken cancellationToken)
        => SaveServerCoreAsync(configuration, cancellationToken, restoringArchive: true);

    private async Task SaveServerCoreAsync(GXServerConfiguration configuration, CancellationToken cancellationToken, bool restoringArchive)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (var transport in configuration.Transports)
            GXServerTransportValidator.Validate(transport);
        await SaveConfigurationAsync(ApplicationMode.Server, configuration, cancellationToken, restoringArchive);
        if (ServerConfigurationSaved is { } handlers)
            foreach (Func<Task> handler in handlers.GetInvocationList()) await handler();
    }

    public Task SaveServerStateAsync(GXServerState state, CancellationToken cancellationToken)
    {
        return SaveStateAsync(ApplicationMode.Server, state, cancellationToken);
    }

    public Task SaveDataVaultAsync(GXDataVaultConfiguration configuration, CancellationToken cancellationToken)
        => SaveDataVaultCoreAsync(configuration, cancellationToken, restoringArchive: false);

    public Task ImportDataVaultAsync(GXDataVaultConfiguration configuration, CancellationToken cancellationToken)
        => SaveDataVaultCoreAsync(configuration, cancellationToken, restoringArchive: true);

    private async Task SaveDataVaultCoreAsync(GXDataVaultConfiguration configuration, CancellationToken cancellationToken, bool restoringArchive)
    {
        GXDataVaultMappingValidator.Validate(configuration.GetMappings());
        await SaveConfigurationAsync(ApplicationMode.DataVault, configuration, cancellationToken, restoringArchive);
        if (DataVaultConfigurationSaved is { } handlers)
            foreach (Func<Task> handler in handlers.GetInvocationList()) await handler();
    }

    private SemaphoreSlim ConfigurationGate => GetConfigurationGate(Path.Combine(_contentRootPath,
        GXTransportRouting.StableId($"{_storeSettings.Type}:{_storeSettings.ConnectionString}").ToString("N")));

    private async Task<T?> LoadConfigurationAsync<T>(ApplicationMode mode, CancellationToken cancellationToken)
        where T : GXModeConfiguration
    {

        await ConfigurationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConfigurationStoreAsync(cancellationToken);
            return await ReadConfigurationAsync<T>(mode, cancellationToken);
        }
        finally { ConfigurationGate.Release(); }
    }
    private async Task<T?> ReadConfigurationAsync<T>(ApplicationMode mode, CancellationToken cancellationToken)
        where T : GXModeConfiguration
    {
        await using DbConnection native = CreateConfigurationConnection();
        await native.OpenAsync(cancellationToken);
        using GXDbConnection connection = new(native);
        using var transaction = connection.BeginTransaction();
        RelayConfiguration? record = await connection.SingleOrDefaultAsync<RelayConfiguration>(transaction,
            Gurux.Data.Relay.Database.GXMetadataQueries.Select<RelayConfiguration>(connection, ("Mode", mode)), cancellationToken);
        if (record != null)
        {
            record.Payload = await new GXRelationalConfigurationStore(connection, transaction, cancellationToken).LoadAsync(record.PayloadId);
            T configuration = GXConfigurationMapper.FromEntities<T>(record.Payload);
            transaction.Commit();
            return configuration;
        }
        transaction.Commit();
        return default;
    }
    private async Task SaveConfigurationAsync<T>(ApplicationMode mode, T configuration, CancellationToken cancellationToken, bool restoringArchive = false)
        where T : GXModeConfiguration
    {
        ArgumentNullException.ThrowIfNull(configuration);
        System.ComponentModel.DataAnnotations.Validator.ValidateObject(configuration,
            new System.ComponentModel.DataAnnotations.ValidationContext(configuration), validateAllProperties: true);
        if (configuration.Mode != mode) throw new InvalidOperationException("Configuration mode does not match its store entry.");
        await ConfigurationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConfigurationStoreAsync(cancellationToken);
            await using DbConnection native = CreateConfigurationConnection();
            await native.OpenAsync(cancellationToken);
            using GXDbConnection connection = new(native);
            using var transaction = connection.BeginTransaction();
            try
            {
                RelayConfiguration? record = await connection.SingleOrDefaultAsync<RelayConfiguration>(transaction,
                    Gurux.Data.Relay.Database.GXMetadataQueries.Select<RelayConfiguration>(connection, ("Mode", mode)), cancellationToken);
                bool creating = record is null;
                record ??= new RelayConfiguration { Id = Guid.NewGuid(), Mode = mode, PayloadId = await GetSettingsIdAsync(connection, transaction, mode, cancellationToken) };
                record.Updated = DateTimeOffset.Now;
                var settings = GXConfigurationMapper.ToEntities(configuration, record.PayloadId);
                if (creating && configuration.ConcurrencyStamp is null)
                {
                    // Logging can create the mode's settings parent before its first configuration.
                    var logParent = await connection.SingleOrDefaultAsync<GXSettings>(transaction,
                        Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXSettings>(connection, ("Id", record.PayloadId)), cancellationToken);
                    if (logParent is not null) GXEntityPersistence.CopyMetadata(logParent, settings);
                }
                await new GXRelationalConfigurationStore(connection, transaction, cancellationToken, restoringArchive,
                    importedAt: restoringArchive ? DateTimeOffset.UtcNow : null).SaveAsync(record, settings);
                if (mode == ApplicationMode.DataVault)
                {
                    var retained = settings.Databases.SelectMany(d => d.Mappings ?? []).Concat(settings.Mappings ?? [])
                        .Select(m => m.Id).ToHashSet();
                    foreach (var state in await connection.SelectAllAsync<Shared.GXDataVaultMappingState>(transaction, cancellationToken))
                        if (!retained.Contains(state.Id))
                            await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<Shared.GXDataVaultMappingState>(s => s.Id == state.Id), cancellationToken);
                }
                transaction.Commit();
                GXConfigurationMapper.CopySavedMetadata(GXConfigurationMapper.FromEntities<T>(settings), configuration);
                if (_configurationFingerprints.Count != 0)
                    _configurationFingerprints[mode] = ConfigurationFingerprint(await ReadConfigurationAsync<T>(mode, cancellationToken));
                _changes?.Publish(new(mode.ToString().ToLowerInvariant(), GXRelayChangeKind.Configuration));
            }
            catch
            {
                if (transaction.Connection != null) transaction.Rollback();
                throw;
            }
        }
        finally { ConfigurationGate.Release(); }
    }

    private async Task EnsureConfigurationStoreAsync(CancellationToken cancellationToken)
    {
        // Callers hold ConfigurationGate. Cache successful initialization only, not data;
        // failures and changes of configuration store must run initialization again.
        cancellationToken.ThrowIfCancellationRequested();
        var store = (_storeSettings.Type, _storeSettings.ConnectionString);
        if (_initializedStore == store) return;
        if (_storeSettings.Type == DatabaseType.SqLite)
        {
            SqliteConnectionStringBuilder builder = new(_storeSettings.ConnectionString);
            string? directory = Path.GetDirectoryName(builder.DataSource);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        }
        await using DbConnection connection = CreateConfigurationConnection();
        await connection.OpenAsync(cancellationToken);
        GXSchemaManager schema = new(connection);
        if (schema.TableExist(typeof(Shared.GXDatabase)) &&
            schema.GetColumns(typeof(Shared.GXDatabase)).Contains("Settings", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The configuration store uses mode-owned databases. This version requires a new configuration store with a shared database catalog; legacy conversion is not supported.");
        foreach (Type type in GXRelationalConfigurationStore.EntityTypes.Concat(new[]
        {
            typeof(RelayRuntimeState), typeof(GXClientTableState), typeof(GXTableMapping),
            typeof(GXProcessedMessageState), typeof(GXEventLog), typeof(GXTransportMessageLog), typeof(Shared.GXDataVaultMappingState)
        }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!schema.TableExist(type)) schema.CreateTable(type, relations: false, overwrite: false);
            else if (type == typeof(GXClientTableState) && !schema.GetColumns(type).Contains(nameof(GXClientTableState.NextTransferTime), StringComparer.OrdinalIgnoreCase))
                schema.UpdateTable(type);
            else if (type == typeof(Shared.GXSettings))
            {
                var columns = schema.GetColumns(type);
                string[] required = [nameof(Shared.GXSettings.RestEnabled), nameof(Shared.GXSettings.SwaggerEnabled),
                    nameof(Shared.GXSettings.CorsEnabled), nameof(Shared.GXSettings.CorsAllowCredentials), nameof(Shared.GXSettings.CorsAllowedOriginsJson), nameof(Shared.GXSettings.ColumnAliasesJson)];
                if (required.Any(column => !columns.Contains(column, StringComparer.OrdinalIgnoreCase))) schema.UpdateTable(type);
            }
        }
        _initializedStore = store;
    }

    private static async Task<Guid> GetSettingsIdAsync(GXDbConnection connection, System.Data.IDbTransaction transaction,
        ApplicationMode mode, CancellationToken token)
    {
        var settings = await connection.SingleOrDefaultAsync<GXSettings>(transaction,
            Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXSettings>(connection, ("Mode", mode)), token);
        return settings?.Id ?? Guid.NewGuid();
    }

    private async Task<T?> LoadStateAsync<T>(ApplicationMode mode, CancellationToken cancellationToken)
    {
        await ConfigurationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConfigurationStoreAsync(cancellationToken);
            await using var native = CreateConfigurationConnection();
            await native.OpenAsync(cancellationToken);
            using GXDbConnection connection = new(native);
            using var transaction = connection.BeginTransaction();
            var record = await connection.SingleOrDefaultAsync<RelayRuntimeState>(transaction,
                Gurux.Data.Relay.Database.GXMetadataQueries.Select<RelayRuntimeState>(connection, ("Id", mode)), cancellationToken);
            if (record is not null)
            {
                var state = (T)await new GXRelationalRuntimeStateStore(connection, transaction, cancellationToken).LoadAsync(mode);
                GXEntityPersistence.CopyMetadata(record, (IGXEntityMetadata)(object)state!);
                transaction.Commit();
                return state;
            }
            transaction.Commit();
            return default;
        }
        finally { ConfigurationGate.Release(); }
    }

    public Task UpdateClientStateAsync(Action<GXClientState> update, CancellationToken cancellationToken)
        => SaveStateAsync(ApplicationMode.Client, new GXClientState(), cancellationToken, update);

    private async Task SaveStateAsync<T>(ApplicationMode mode, T state, CancellationToken cancellationToken, Action<T>? update = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        await ConfigurationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConfigurationStoreAsync(cancellationToken);
            await using var native = CreateConfigurationConnection();
            await native.OpenAsync(cancellationToken);
            using GXDbConnection connection = new(native);
            using var transaction = connection.BeginTransaction();
            var runtimeStore = new GXRelationalRuntimeStateStore(connection, transaction, cancellationToken);
            var existingRecord = await connection.SingleOrDefaultAsync<RelayRuntimeState>(transaction,
                Gurux.Data.Relay.Database.GXMetadataQueries.Select<RelayRuntimeState>(connection, ("Id", mode)), cancellationToken);
            var before = existingRecord is null ? null : StateContent(await runtimeStore.LoadAsync(mode));
            if (update is not null)
            {
                if (existingRecord is null) return;
                state = (T)await runtimeStore.LoadAsync(mode);
                GXEntityPersistence.CopyMetadata(existingRecord, (Shared.IGXEntityMetadata)(object)state!);
                update(state);
            }
            // Persist a detached copy so a rolled-back save leaves the caller's versions intact.
            T detached = System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(state))!;
            GXConfigurationMapper.CopySavedMetadata(state, detached!);
            await new GXRelationalRuntimeStateStore(connection, transaction, cancellationToken).SaveAsync(mode, detached);
            transaction.Commit();
            GXConfigurationMapper.CopySavedMetadata(detached!, state);
            if (before != StateContent(detached!))
                _changes?.Publish(new(mode.ToString().ToLowerInvariant(), GXRelayChangeKind.State));
        }
        finally { ConfigurationGate.Release(); }
    }
    private static string StateContent(object state)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(state)!;
        void RemoveMetadata(System.Text.Json.Nodes.JsonNode? value)
        {
            if (value is System.Text.Json.Nodes.JsonObject obj)
            {
                obj.Remove("CreationTime"); obj.Remove("Updated"); obj.Remove("ConcurrencyStamp");
                foreach (var child in obj.ToArray()) RemoveMetadata(child.Value);
            }
            else if (value is System.Text.Json.Nodes.JsonArray array)
                foreach (var child in array) RemoveMetadata(child);
        }
        RemoveMetadata(node);
        return node.ToJsonString();
    }
    public async Task<List<Shared.GXDataVaultMappingState>> LoadDataVaultStateAsync(CancellationToken cancellationToken)
    {
        await ConfigurationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConfigurationStoreAsync(cancellationToken);
            await using var native = CreateConfigurationConnection();
            await native.OpenAsync(cancellationToken);
            using var connection = new GXDbConnection(native);
            return await connection.SelectAllAsync<Shared.GXDataVaultMappingState>(cancellationToken: cancellationToken);
        }
        finally { ConfigurationGate.Release(); }
    }

    public async Task RecordDataVaultRunAsync(Guid mappingId, Guid runId, string status, long? rowCount,
        long? durationMs, string? error, CancellationToken cancellationToken)
    {
        await ConfigurationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConfigurationStoreAsync(cancellationToken);
            await using var native = CreateConfigurationConnection();
            await native.OpenAsync(cancellationToken);
            using var connection = new GXDbConnection(native);
            using var transaction = connection.BeginTransaction();
            var mapping = await connection.SingleOrDefaultAsync<GXDataVaultTableMapping>(transaction,
                Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXDataVaultTableMapping>(connection, ("Id", mappingId)), cancellationToken);
            // A mapping can be removed while its run is completing.
            if (mapping == null)
            {
                transaction.Commit();
                return;
            }
            var databaseLinks = await connection.SelectAllAsync<GXDatabaseMappingReference>(transaction, cancellationToken);
            var settingsLinks = await connection.SelectAllAsync<GXSettingsMappingReference>(transaction, cancellationToken);
            var ownerIds = databaseLinks.Where(r => r.TargetId == mappingId).Select(r => r.ConfigurationId)
                .Concat(settingsLinks.Where(r => r.TargetId == mappingId).Select(r => r.ConfigurationId))
                .Distinct().ToArray();
            // Both reference collections can point to the same mapping, but it must have one owner.
            if (ownerIds.Length != 1)
            {
                transaction.Commit();
                return;
            }
            Guid ownerId = ownerIds[0];
            var owner = await connection.SingleOrDefaultAsync<GXSettings>(transaction,
                Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXSettings>(connection, ("Id", ownerId)), cancellationToken);
            if (owner?.Mode != ApplicationMode.DataVault)
            {
                transaction.Commit();
                return;
            }
            var previous = await connection.SingleOrDefaultAsync<Shared.GXDataVaultMappingState>(transaction,
                Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXDataVaultMappingState>(connection, ("Id", mappingId)), cancellationToken);
            if (status != "Running" && previous?.RunId != runId) { transaction.Commit(); return; }
            var state = previous == null ? new GXDataVaultMappingState { Id = mappingId }
                : System.Text.Json.JsonSerializer.Deserialize<GXDataVaultMappingState>(System.Text.Json.JsonSerializer.Serialize(previous))!;
            state.RunId = runId;
            state.Status = status;
            state.RowCount = rowCount;
            state.DurationMs = durationMs;
            state.Error = error;
            if (status == "Running") { state.LastStarted = DateTimeOffset.UtcNow; state.LastCompleted = null; }
            else state.LastCompleted = DateTimeOffset.Now;
            if (status == "Succeeded") state.LastSuccessfulRun = state.LastCompleted;
            await GXEntityPersistence.SaveAsync(connection, transaction, state, previous, cancellationToken);
            transaction.Commit();
            _changes?.Publish(new("datavault", GXRelayChangeKind.State));
        }
        finally { ConfigurationGate.Release(); }
    }

    private DbConnection CreateConfigurationConnection()
    {
        return _connectionFactory.CreateConnection(new GXDatabaseConfiguration
        {
            Type = _storeSettings.Type,
            ConnectionString = _storeSettings.ConnectionString,
        });
    }

    private static SemaphoreSlim GetConfigurationGate(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return ConfigurationGates.GetOrAdd(fullPath, static _ => new SemaphoreSlim(1, 1));
    }
}




