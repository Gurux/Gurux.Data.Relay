using Gurux.Service.Orm.Common.Model;
using Gurux.Data.Relay.Realtime;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gurux.Data.Relay.Client;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Log;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Gurux.Data.Relay.Enums;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Sources;
using Microsoft.Extensions.Configuration;

namespace Gurux.Data.Relay.Web.Server.Services;

public sealed class GXRelayAdministrationService : IGXRelayAdministrationService
{
    private readonly IGXRelayChangePublisher? _changes;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IGXConfigurationService _configurationService;
    private readonly IGXDatabaseConnectionFactory _connectionFactory;
    private readonly GXConfigurationStoreSettings _storeSettings;
    private readonly IGXDatabaseConnectionTestService _connectionTestService;
    private readonly IGXDatabaseMetadataService _metadataService;
    private readonly IClientSchemaSender _schemaSender;
    private readonly IConfiguration? _sourceConfiguration;

    public GXRelayAdministrationService(
        IGXConfigurationService configurationService,
        IGXDatabaseConnectionFactory connectionFactory,
        GXConfigurationStoreSettings storeSettings,
        IGXDatabaseConnectionTestService connectionTestService,
        IGXDatabaseMetadataService metadataService,
        IClientSchemaSender schemaSender, IGXRelayChangePublisher? changes = null, IConfiguration? sourceConfiguration = null)
    {
        _configurationService = configurationService;
        _connectionFactory = connectionFactory;
        _storeSettings = storeSettings; _changes = changes;
        _connectionTestService = connectionTestService;
        _metadataService = metadataService;
        _schemaSender = schemaSender;
        _sourceConfiguration = sourceConfiguration;
    }

    public async Task<IReadOnlyList<GXDatabaseConfiguration>> GetDatabasesAsync(ApplicationMode mode, CancellationToken cancellationToken)
    {
        GXModeConfiguration configuration = await LoadOrCreateModeConfigurationAsync(mode, cancellationToken);
        if (configuration is GXClientConfiguration clientConfiguration) clientConfiguration.EnsureRoutingIds();
        if (configuration is GXServerConfiguration serverConfiguration) serverConfiguration.EnsureRoutingIds();
        IReadOnlyList<GXDatabaseConfiguration> databases = GetDatabasesFromModeConfiguration(configuration);
        List<GXDatabaseConfiguration> result = Clone(databases.ToList());
        if (mode == ApplicationMode.Client)
        {
            GXClientState state = await _configurationService.LoadClientStateAsync(cancellationToken) ?? new GXClientState();
            ApplyClientTableTransferState(result, state);
        }

        return result;
    }

    public async Task<string?> SaveDatabasesAsync(ApplicationMode mode, IReadOnlyList<GXDatabaseConfiguration> databases, CancellationToken cancellationToken, string? concurrencyStamp = null)
    {
        GXModeConfiguration configuration = await LoadOrCreateModeConfigurationAsync(mode, cancellationToken);
        if (!string.Equals(configuration.ConcurrencyStamp, concurrencyStamp, StringComparison.Ordinal))
            throw new DBConcurrencyException("The database list has changed. Reload the settings before saving.");

        switch (configuration)
        {
            case GXClientConfiguration client:
                client.EnsureRoutingIds();
                List<GXDatabaseConfiguration> previousDatabases = Clone(client.Databases);
                foreach (GXTableConfiguration table in databases.SelectMany(database => database.Tables ?? []))
                {
                    if (table.Schedule.Type == ScheduleType.Cron &&
                        GXCronSchedule.Validate(table.Schedule.Expression) is { } error)
                        throw new InvalidOperationException($"Table '{table.Name}': {error}");
                }
                PreserveTransportPasswords(databases, client.Databases);
                client.Databases = Clone(databases.ToList());
                NormalizeClientConfiguration(client);
                await _configurationService.SaveClientAsync(client, cancellationToken);
                await UpdateClientStateDatabasesAsync(previousDatabases, client.Databases, cancellationToken);
                break;
            case GXServerConfiguration server:
                server.EnsureRoutingIds();
                PreserveTransportPasswords(databases, server.Databases);
                server.Databases = Clone(databases.ToList());
                NormalizeServerConfiguration(server);
                await _configurationService.SaveServerAsync(server, cancellationToken);
                break;
            case GXDataVaultConfiguration dataVault:
                PreserveTransportPasswords(databases, dataVault.Databases);
                dataVault.Databases = Clone(databases.ToList());
                await _configurationService.SaveDataVaultAsync(dataVault, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Mode '{mode}' is not supported.");
        }
        var savedDatabases = GetDatabasesFromModeConfiguration(configuration);
        for (int index = 0; index < databases.Count; ++index)
            GXEntityMetadata.ApplySaved(savedDatabases[index], databases[index]);
        return configuration.ConcurrencyStamp;
    }

    public async Task<GXClientConfiguration> GetClientSettingsAsync(CancellationToken cancellationToken, GXDatabase? filter = null)
    {
        GXClientConfiguration configuration = await _configurationService.LoadClientAsync(cancellationToken)
            ?? new GXClientConfiguration();
        GXClientConfiguration result = SanitizeSecrets(Clone(configuration));
        GXClientState state = await _configurationService.LoadClientStateAsync(cancellationToken) ?? new GXClientState();
        ApplyClientTableTransferState(result.Databases, state);
        FilterDatabases(result.Databases, filter);
        return result;
    }

    public async Task UpdateClientSettingsAsync(GXClientConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.Mode != ApplicationMode.Client)
        {
            throw new InvalidOperationException("Client settings payload mode must be Client.");
        }

        GXClientConfiguration? existing = await _configurationService.LoadClientAsync(cancellationToken);
        NormalizeClientConfiguration(configuration);
        existing?.EnsureRoutingIds();
        PreservePasswords(configuration.Transports, existing?.Transports ?? []);
        await _configurationService.SaveClientAsync(configuration, cancellationToken);
        await UpdateClientStateDatabasesAsync(existing?.Databases ?? [], configuration.Databases, cancellationToken);
    }

    private async Task UpdateClientStateDatabasesAsync(IReadOnlyList<GXDatabaseConfiguration> previous,
        IReadOnlyList<GXDatabaseConfiguration> current, CancellationToken cancellationToken)
    {
        if (previous.Select(db => db.Id).SequenceEqual(current.Select(db => db.Id))) return;
        await _configurationService.UpdateClientStateAsync(state =>
        {
            List<GXClientTableState> retained = [];
            for (int newIndex = 0; newIndex < current.Count; ++newIndex)
            {
                int oldIndex = -1;
                for (int index = 0; index < previous.Count; ++index)
                    if (previous[index].Id == current[newIndex].Id) { oldIndex = index; break; }
                if (oldIndex < 0) continue;

                foreach (GXTableConfiguration table in current[newIndex].Tables ?? [])
                {
                    string oldName = previous.Count == 1 ? table.Name : $"source-{oldIndex + 1}:{table.Name}";
                    // Entries with no index predate database-scoped state. Their name must identify the database.
                    GXClientTableState? entry = state.Tables.Where(item =>
                        (item.DatabaseIndex == null || item.DatabaseIndex == oldIndex) &&
                        string.Equals(item.Name, oldName, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(item => item.LastAttemptedTransfer ?? item.LastSuccessfulTransfer)
                        .FirstOrDefault();
                    if (entry is null) continue;
                    GXClientTableState updated = Clone(entry);
                    updated.Id = entry.Id;
                    updated.DatabaseIndex = newIndex;
                    updated.Name = current.Count == 1 ? table.Name : $"source-{newIndex + 1}:{table.Name}";
                    retained.Add(updated);
                }
            }
            state.Tables = retained;
        }, cancellationToken);
    }

    public async Task<GXClientState> GetClientStateAsync(CancellationToken cancellationToken)
    {
        GXClientState state = await _configurationService.LoadClientStateAsync(cancellationToken) ?? new GXClientState();
        GXClientConfiguration? configuration = await _configurationService.LoadClientAsync(cancellationToken);
        if (configuration != null)
        {
            // Resolve older state entries using the same names as the client scheduler.
            foreach (GXClientTableState table in state.Tables.Where(item => item.DatabaseIndex == null))
            {
                var matches = configuration.Databases.Select((database, index) => new { database, index })
                    .Where(item => item.database.Tables != null && item.database.Tables.Any(source =>
                        string.Equals(table.Name, configuration.Databases.Count == 1
                            ? source.Name : $"source-{item.index + 1}:{source.Name}", StringComparison.OrdinalIgnoreCase)))
                    .Select(item => item.index).ToList();
                if (matches.Count == 1)
                {
                    table.DatabaseIndex = matches[0];
                }
            }
        }
        return state;
    }

    public async Task ResetClientStateAsync(CancellationToken cancellationToken)
    {
        GXClientState state = await _configurationService.LoadClientStateAsync(cancellationToken) ?? new();
        state.Tables.Clear();
        await _configurationService.SaveClientStateAsync(state, cancellationToken);
    }

    public async Task ResetClientTableCheckpointAsync(Guid databaseId, string stateTableName,
        string? concurrencyStamp, CancellationToken cancellationToken)
    {
        var configuration = await _configurationService.LoadClientAsync(cancellationToken)
            ?? throw new InvalidOperationException("Client configuration was not found.");
        int index = configuration.Databases.FindIndex(database => database.Id == databaseId);
        if (index < 0 || !(configuration.Databases[index].Tables ?? []).Any(table =>
            string.Equals(configuration.Databases.Count == 1 ? table.Name : $"source-{index + 1}:{table.Name}", stateTableName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The selected table has changed or was removed. Reload the state page.");
        var state = await _configurationService.LoadClientStateAsync(cancellationToken)
            ?? throw new InvalidOperationException("Client state was not found.");
        var selected = state.Tables.SingleOrDefault(table =>
            (table.DatabaseIndex == null || table.DatabaseIndex == index) &&
            string.Equals(table.Name, stateTableName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected table state was not found.");
        if (!string.Equals(selected.ConcurrencyStamp, concurrencyStamp, StringComparison.Ordinal))
            throw new System.Data.DBConcurrencyException("The table state has changed. Reload it before resetting the transfer checkpoint.");
        selected.LastCheckpointValue = null;
        await _configurationService.SaveClientStateAsync(state, cancellationToken);
    }

    public async Task<int> SendClientSchemaAsync(CancellationToken cancellationToken)
    {
        GXClientConfiguration configuration = await _configurationService.LoadClientAsync(cancellationToken)
            ?? throw new InvalidOperationException("Client configuration was not found.");
        NormalizeClientConfiguration(configuration);
        return await _schemaSender.SendAsync(configuration, cancellationToken);
    }

    public async Task<GXServerConfiguration> GetServerSettingsAsync(CancellationToken cancellationToken, GXDatabase? filter = null)
    {
        GXServerConfiguration configuration = await _configurationService.LoadServerAsync(cancellationToken)
            ?? new GXServerConfiguration();
        GXServerConfiguration result = SanitizeSecrets(Clone(configuration));
        FilterDatabases(result.Databases, filter);
        return result;
    }

    public async Task UpdateServerSettingsAsync(GXServerConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.Mode != ApplicationMode.Server)
        {
            throw new InvalidOperationException("Server settings payload mode must be Server.");
        }

        GXServerConfiguration? existing = await _configurationService.LoadServerAsync(cancellationToken);
        NormalizeServerConfiguration(configuration);
        existing?.EnsureRoutingIds();
        PreservePasswords(configuration.Transports, existing?.Transports ?? []);
        await _configurationService.SaveServerAsync(configuration, cancellationToken);
    }

    public async Task<GXServerState> GetServerStateAsync(CancellationToken cancellationToken)
    {
        return await _configurationService.LoadServerStateAsync(cancellationToken) ?? new GXServerState();
    }

    public async Task<GXDataVaultConfiguration> GetDataVaultSettingsAsync(CancellationToken cancellationToken, GXDatabase? filter = null)
    {
        GXDataVaultConfiguration configuration = await _configurationService.LoadDataVaultAsync(cancellationToken)
            ?? new GXDataVaultConfiguration();
        GXDataVaultConfiguration result = SanitizeSecrets(Clone(configuration));
        FilterDatabases(result.Databases, filter);
        return result;
    }

    private static void FilterDatabases(List<GXDatabaseConfiguration> databases, GXDatabase? filter)
    {
        if (filter == null) return;
        databases.RemoveAll(database =>
            (filter.Type.HasValue && database.Type != (DatabaseType)filter.Type) ||
            (!string.IsNullOrWhiteSpace(filter.Description) &&
             database.Description?.Contains(filter.Description, StringComparison.OrdinalIgnoreCase) != true));
    }

    public async Task UpdateDataVaultSettingsAsync(GXDataVaultConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.Mode != ApplicationMode.DataVault)
        {
            throw new InvalidOperationException("Data Vault settings payload mode must be DataVault.");
        }

        GXDataVaultConfiguration? existing = await _configurationService.LoadDataVaultAsync(cancellationToken);
        // Web access settings can be saved before a Data Vault database is configured.
        PreserveTransportPasswords(configuration.Databases, existing?.Databases);
        await _configurationService.SaveDataVaultAsync(configuration, cancellationToken);
    }

    public async Task<string> ExportSettingsJsonAsync(ApplicationMode mode, CancellationToken cancellationToken)
    {
        object configuration = mode switch
        {
            ApplicationMode.Client => NormalizeAndReturn(await _configurationService.LoadClientAsync(cancellationToken)
                ?? throw new InvalidOperationException("Client configuration was not found.")),
            ApplicationMode.Server => NormalizeAndReturn(await _configurationService.LoadServerAsync(cancellationToken)
                ?? throw new InvalidOperationException("Server configuration was not found.")),
            ApplicationMode.DataVault => NormalizeAndReturn(await _configurationService.LoadDataVaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Data Vault configuration was not found.")),
            _ => throw new NotSupportedException($"Mode '{mode}' is not supported."),
        };

        return Shared.GXDatabaseSelectionsJson.WithoutCatalogFields(
            JsonSerializer.Serialize(configuration, configuration.GetType(), SerializerOptions), SerializerOptions);
    }

    public async Task ImportSettingsJsonAsync(ApplicationMode mode, string json, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Import payload cannot be empty.");
        }

        switch (mode)
        {
            case ApplicationMode.Client:
                {
                    GXClientConfiguration clientConfiguration = JsonSerializer.Deserialize<GXClientConfiguration>(json, SerializerOptions)
                        ?? throw new InvalidOperationException("Client settings import payload was empty.");
                    if (mode != clientConfiguration.Mode)
                    {
                        throw new InvalidOperationException($"Client settings import mode '{clientConfiguration.Mode}' does not match '{mode}'.");
                    }

                    NormalizeClientConfiguration(clientConfiguration);
                    GXClientConfiguration? previousClient = await _configurationService.LoadClientAsync(cancellationToken);
                    previousClient?.EnsureRoutingIds();
                    await _configurationService.ImportClientAsync(clientConfiguration, cancellationToken);
                    await UpdateClientStateDatabasesAsync(previousClient?.Databases ?? [], clientConfiguration.Databases, cancellationToken);
                    break;
                }
            case ApplicationMode.Server:
                {
                    GXServerConfiguration serverConfiguration = JsonSerializer.Deserialize<GXServerConfiguration>(json, SerializerOptions)
                        ?? throw new InvalidOperationException("Server settings import payload was empty.");
                    if (mode != serverConfiguration.Mode)
                    {
                        throw new InvalidOperationException($"Server settings import mode '{serverConfiguration.Mode}' does not match '{mode}'.");
                    }

                    NormalizeServerConfiguration(serverConfiguration);
                    await _configurationService.ImportServerAsync(serverConfiguration, cancellationToken);
                    break;
                }
            case ApplicationMode.DataVault:
                {
                    GXDataVaultConfiguration dataVaultConfiguration = JsonSerializer.Deserialize<GXDataVaultConfiguration>(json, SerializerOptions)
                        ?? throw new InvalidOperationException("Data Vault settings import payload was empty.");
                    if (mode != dataVaultConfiguration.Mode)
                    {
                        throw new InvalidOperationException($"Data Vault settings import mode '{dataVaultConfiguration.Mode}' does not match '{mode}'.");
                    }

                    NormalizeDataVaultConfiguration(dataVaultConfiguration);
                    await _configurationService.ImportDataVaultAsync(dataVaultConfiguration, cancellationToken);
                    break;
                }
            default:
                throw new NotSupportedException($"Mode '{mode}' is not supported.");
        }
    }

    public async Task<IReadOnlyList<GXEventLog>> GetEventsAsync(
        ApplicationMode mode, int top, LogLevel? minimumLevel, int? databaseIndex, CancellationToken cancellationToken)
    {
        var page = await GetEventPageAsync(mode, new() { Count = top <= 0 ? 50 : top, MinimumLevel = minimumLevel, DatabaseIndex = databaseIndex }, cancellationToken);
        return page.Items;
    }

    public async Task<Shared.GXEventPage<GXEventLog>> GetEventPageAsync(
        ApplicationMode mode, Shared.GXEventPageRequest request, CancellationToken cancellationToken)
    {
        await using var native = _connectionFactory.CreateConnection(GetEventLogDatabaseConfiguration());
        await native.OpenAsync(cancellationToken);
        using var connection = new GXDbConnection(native);
        GXSelectArgs Query()
        {
            var query = GXSelectArgs.SelectAll<GXEventLog>(e => e.Mode == mode);
            query.UseQueryCache(new Gurux.Service.DB.GXQueryCache(_storeSettings.Type));
            if (request.MinimumLevel is LogLevel level) query.Where.And<GXEventLog>(e => e.Level >= level);
            if (request.DatabaseIndex is int database) query.Where.And<GXEventLog>(e => e.DatabaseIndex == database);
            query.Where.FilterBy(request.Filter);
            return query;
        }
        var countQuery = Query();
        countQuery.Columns.Clear();
        countQuery.Columns.Add<GXEventLog>(e => GXSql.Count(e.Id), e => e.Id);
        int total = await connection.ExecuteScalarAsync<int>(countQuery.ToString(false), cancellationToken);
        var query = Query();
        query.Descending = true;
        query.OrderBy.Add<GXEventLog>(e => e.Id);
        query.Index = (uint)Math.Max(0, request.StartIndex);
        query.Count = (uint)Math.Clamp(request.Count, 1, 500);
        return new()
        {
            Items = await connection.SelectAsync<GXEventLog>(query, cancellationToken),
            TotalCount = total
        };
    }

    public async Task<int> ClearEventsAsync(ApplicationMode mode, int? databaseIndex, CancellationToken cancellationToken)
    {
        GXDatabaseConfiguration eventLogDatabase = GetEventLogDatabaseConfiguration();

        await using DbConnection connection = _connectionFactory.CreateConnection(eventLogDatabase);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        GXDeleteArgs args = GXDeleteArgs.Delete<GXEventLog>(w => w.Mode == mode);
        int count = await guruxConnection.DeleteAsync(args, cancellationToken);
        _changes?.Publish(new(mode.ToString().ToLowerInvariant(), GXRelayChangeKind.Events));
        return count;
    }
    public Task TestConnectionAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken)
    {
        return _connectionTestService.TestConnectionAsync(configuration, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetTableNamesAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken)
    {
        return _metadataService.GetTableNamesAsync(configuration, cancellationToken);
    }

    public async Task<string> ExportDatabaseDiagramAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection(configuration);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        GXSchemaManager schemaManager = new(guruxConnection);
        return await Task.Run(() => schemaManager.ExportMermaid(), cancellationToken);
    }

    public Task<GXTableSchema> DescribeTableAsync(GXDatabaseConfiguration configuration, string tableName, CancellationToken cancellationToken)
    {
        return _metadataService.DescribeTableAsync(configuration, tableName, cancellationToken);
    }

    public async Task<IReadOnlyList<(int DatabaseIndex, GXDataVaultTableMapping Mapping)>> ListDataVaultMappingsAsync(CancellationToken cancellationToken)
    {
        GXDataVaultConfiguration configuration = await _configurationService.LoadDataVaultAsync(cancellationToken)
            ?? new GXDataVaultConfiguration();
        if (configuration.Databases.Count == 0)
        {
            return [];
        }
        NormalizeDataVaultConfiguration(configuration);

        List<(int DatabaseIndex, GXDataVaultTableMapping Mapping)> result = [];
        for (int databaseIndex = 0; databaseIndex < configuration.Databases.Count; ++databaseIndex)
        {
            foreach (GXDataVaultTableMapping mapping in configuration.Databases[databaseIndex].Mappings ?? [])
            {
                mapping.Database = configuration.Databases[databaseIndex].Id;
                result.Add((databaseIndex, mapping));
            }
        }

        return result;
    }

    public async Task<(int DatabaseIndex, GXDataVaultTableMapping Mapping)?> GetDataVaultMappingAsync(Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<(int DatabaseIndex, GXDataVaultTableMapping Mapping)> mappings = await ListDataVaultMappingsAsync(cancellationToken);
        foreach ((int DatabaseIndex, GXDataVaultTableMapping Mapping) value in mappings)
        {
            if (value.Mapping.Id == id)
            {
                return value;
            }
        }

        return null;
    }

    public async Task<GXDataVaultTableMapping> CreateDataVaultMappingAsync(
        int databaseIndex,
        GXDataVaultTableMapping mapping,
        CancellationToken cancellationToken)
    {
        GXDataVaultConfiguration configuration = await _configurationService.LoadDataVaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Data Vault configuration was not found.");
        NormalizeDataVaultConfiguration(configuration);

        GXDatabaseConfiguration database = GetDataVaultDatabase(configuration, databaseIndex);
        database.Mappings ??= [];

        GXDataVaultTableMapping item = Clone(mapping);
        if (item.Id == Guid.Empty)
        {
            item.Id = Guid.NewGuid();
        }

        item.Updated = DateTimeOffset.UtcNow;
        item.Database = database.Id;
        database.Mappings.Add(item);

        await _configurationService.SaveDataVaultAsync(configuration, cancellationToken);
        return item;
    }

    public async Task<GXDataVaultTableMapping> UpdateDataVaultMappingAsync(
        Guid id,
        int databaseIndex,
        GXDataVaultTableMapping mapping,
        CancellationToken cancellationToken)
    {
        GXDataVaultConfiguration configuration = await _configurationService.LoadDataVaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Data Vault configuration was not found.");
        NormalizeDataVaultConfiguration(configuration);

        GXDatabaseConfiguration targetDatabase = GetDataVaultDatabase(configuration, databaseIndex);
        targetDatabase.Mappings ??= [];

        // Remove from any database before adding the updated mapping to the requested target database.
        foreach (GXDatabaseConfiguration database in configuration.Databases)
        {
            database.Mappings?.RemoveAll(item => item.Id == id);
        }

        GXDataVaultTableMapping updated = Clone(mapping);
        updated.Id = id;
        updated.Updated = DateTimeOffset.UtcNow;
        updated.Database = targetDatabase.Id;
        targetDatabase.Mappings.Add(updated);

        await _configurationService.SaveDataVaultAsync(configuration, cancellationToken);
        return updated;
    }

    public async Task<bool> DeleteDataVaultMappingAsync(Guid id, CancellationToken cancellationToken)
    {
        GXDataVaultConfiguration configuration = await _configurationService.LoadDataVaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Data Vault configuration was not found.");
        NormalizeDataVaultConfiguration(configuration);

        bool removed = false;
        foreach (GXDatabaseConfiguration database in configuration.Databases)
        {
            if (database.Mappings is null)
            {
                continue;
            }

            removed |= database.Mappings.RemoveAll(item => item.Id == id) > 0;
        }

        if (removed)
        {
            await _configurationService.SaveDataVaultAsync(configuration, cancellationToken);
        }

        return removed;
    }

    private static GXDatabaseConfiguration GetDataVaultDatabase(GXDataVaultConfiguration configuration, int databaseIndex)
    {
        if (databaseIndex < 0 || databaseIndex >= configuration.Databases.Count)
        {
            throw new InvalidOperationException($"Data Vault database index {databaseIndex} is out of range.");
        }

        return configuration.Databases[databaseIndex];
    }

    private async Task<GXModeConfiguration> LoadOrCreateModeConfigurationAsync(ApplicationMode mode, CancellationToken cancellationToken)
    {
        return mode switch
        {
            ApplicationMode.Client => await _configurationService.LoadClientAsync(cancellationToken) ?? new GXClientConfiguration(),
            ApplicationMode.Server => await _configurationService.LoadServerAsync(cancellationToken) ?? new GXServerConfiguration(),
            ApplicationMode.DataVault => await _configurationService.LoadDataVaultAsync(cancellationToken) ?? new GXDataVaultConfiguration(),
            _ => throw new NotSupportedException($"Mode '{mode}' is not supported."),
        };
    }

    private static IReadOnlyList<GXDatabaseConfiguration> GetDatabasesFromModeConfiguration(GXModeConfiguration configuration)
    {
        return configuration switch
        {
            GXClientConfiguration client => client.Databases,
            GXServerConfiguration server => server.Databases,
            GXDataVaultConfiguration dataVault => dataVault.Databases,
            _ => [],
        };
    }

    private static void ApplyClientTableTransferState(
        IReadOnlyList<GXDatabaseConfiguration> databases,
        GXClientState state)
    {
        Dictionary<string, GXClientTableState> stateByName = state.Tables
            .GroupBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        for (int databaseIndex = 0; databaseIndex < databases.Count; ++databaseIndex)
        {
            GXDatabaseConfiguration database = databases[databaseIndex];
            foreach (GXTableConfiguration table in database.Tables ?? [])
            {
                string stateTableName = databases.Count == 1
                    ? table.Name
                    : $"source-{databaseIndex + 1}:{table.Name}";
                table.LastTransferred = stateByName.TryGetValue(stateTableName, out GXClientTableState? tableState)
                    ? tableState.LastSuccessfulTransfer
                    : null;
                table.NextTransferTime = table.Schedule.Type is ScheduleType.Manual or ScheduleType.DatabaseChange
                    ? null : tableState?.NextTransferTime;
            }
        }
    }

    private GXDatabaseConfiguration GetEventLogDatabaseConfiguration()
    {
        return new GXDatabaseConfiguration
        {
            Description = "RelayConfigurationStore",
            Type = _storeSettings.Type,
            ConnectionString = _storeSettings.ConnectionString,
        };
    }

    private static bool HasAnyDatabases(GXModeConfiguration configuration)
    {
        return configuration switch
        {
            GXClientConfiguration clientConfiguration => clientConfiguration.Databases.Count > 0,
            GXServerConfiguration serverConfiguration => serverConfiguration.Databases.Count > 0,
            GXDataVaultConfiguration dataVaultConfiguration => dataVaultConfiguration.Databases.Count > 0,
            _ => false,
        };
    }

    private static string GetSqlParameterPlaceholder(DatabaseType databaseType, string name)
    {
        string prefix = databaseType is DatabaseType.Oracle or DatabaseType.SapHana
            ? ":"
            : "@";
        return $"{prefix}{name}";
    }

    private static string GetSqlParameterName(string placeholder)
    {
        return placeholder.StartsWith(':') || placeholder.StartsWith('@')
            ? placeholder[1..]
            : placeholder;
    }

    private void NormalizeClientConfiguration(GXClientConfiguration configuration)
    {
        configuration.EnsureRoutingIds();
        GXTransportRouting.Validate(configuration.Databases, configuration.Transports, false,
            GXDataSourceServiceExtensions.GetProviderRouteIds(_sourceConfiguration));
        foreach (GXTableConfiguration table in configuration.Databases.SelectMany(db => db.Tables ?? []))
        {
            if (table.Schedule.Type != ScheduleType.Cron) continue;
            string? error = GXCronSchedule.Validate(table.Schedule.Expression);
            if (error != null) throw new InvalidOperationException($"Table '{table.Name}': {error}");
        }
    }

    private static void NormalizeServerConfiguration(GXServerConfiguration configuration)
    {
        configuration.EnsureRoutingIds();
        GXTransportRouting.Validate(configuration.Databases, configuration.Transports, true);
    }
    private static void NormalizeDataVaultConfiguration(GXDataVaultConfiguration configuration)
    {
        if (configuration.Databases.Count == 0)
        {
            throw new InvalidOperationException("Data Vault configuration must contain at least one database in Databases.");
        }


    }

    private static void PreserveTransportPasswords(
        IReadOnlyList<GXDatabaseConfiguration> incomingDatabases,
        IReadOnlyList<GXDatabaseConfiguration>? existingDatabases)
    {
        if (existingDatabases is null)
        {
            return;
        }

        for (int databaseIndex = 0; databaseIndex < incomingDatabases.Count; ++databaseIndex)
        {
            if (databaseIndex >= existingDatabases.Count)
            {
                break;
            }

            GXDatabaseConfiguration incomingDatabase = incomingDatabases[databaseIndex];
            GXDatabaseConfiguration existingDatabase = existingDatabases[databaseIndex];
            PreservePasswords(incomingDatabase.Transports, existingDatabase.Transports);
            foreach (GXTableConfiguration table in incomingDatabase.Tables ?? [])
            {
                GXTableConfiguration? existingTable = existingDatabase.Tables?.FirstOrDefault(item =>
                    string.Equals(item.Name, table.Name, StringComparison.OrdinalIgnoreCase));
                if (existingTable != null) PreservePasswords(table.Transports, existingTable.Transports);
            }
        }
    }

    private static void PreservePasswords(IReadOnlyList<GXTransportConfiguration> incomingTransports,
        IReadOnlyList<GXTransportConfiguration> existingTransports)
    {
        foreach (GXTransportConfiguration incoming in incomingTransports)
        {
            GXTransportConfiguration? existing = existingTransports.FirstOrDefault(t => t.Id == incoming.Id);
            if (existing != null && (string.IsNullOrWhiteSpace(incoming.Password) || incoming.Password == "********"))
                incoming.Password = existing.Password;
        }
    }
    private static T SanitizeSecrets<T>(T configuration) where T : GXModeConfiguration
    {
        switch (configuration)
        {
            case GXClientConfiguration client:
                client.EnsureRoutingIds();
                foreach (var transport in client.Transports) transport.Password = null;
                SanitizeDatabases(client.Databases);
                break;
            case GXServerConfiguration server:
                server.EnsureRoutingIds();
                foreach (var transport in server.Transports) transport.Password = null;
                SanitizeDatabases(server.Databases);
                break;
            case GXDataVaultConfiguration dataVault:
                SanitizeDatabases(dataVault.Databases);
                break;
        }

        return configuration;
    }

    private static void SanitizeDatabases(IEnumerable<GXDatabaseConfiguration> databases)
    {
        foreach (GXDatabaseConfiguration database in databases)
        {
            foreach (GXTransportConfiguration transport in database.Transports.Concat((database.Tables ?? []).SelectMany(table => table.Transports)))
            {
                transport.Password = null;
            }
        }
    }

    private GXClientConfiguration NormalizeAndReturn(GXClientConfiguration configuration)
    {
        NormalizeClientConfiguration(configuration);
        return configuration;
    }

    private static GXServerConfiguration NormalizeAndReturn(GXServerConfiguration configuration)
    {
        NormalizeServerConfiguration(configuration);
        return configuration;
    }

    private static GXDataVaultConfiguration NormalizeAndReturn(GXDataVaultConfiguration configuration)
    {
        NormalizeDataVaultConfiguration(configuration);
        return configuration;
    }

    private static T Clone<T>(T value)
    {
        string json = JsonSerializer.Serialize(value, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Failed to clone configuration object.");
    }
}




