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

using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Enums;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Gurux.Service.Orm.Common.Model;
using System.ComponentModel.DataAnnotations;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Configuration;

public sealed partial class GXDatabaseConfigurationService : IGXSettingsArchiveService
{
    public async Task<GXSettingsArchive> ExportAllSettingsAsync(CancellationToken cancellationToken)
    {
        await ConfigurationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConfigurationStoreAsync(cancellationToken);
            await using var native = CreateConfigurationConnection();
            await native.OpenAsync(cancellationToken);
            using var connection = new GXDbConnection(native);
            using var transaction = connection.BeginTransaction();
            var store = new GXRelationalConfigurationStore(connection, transaction, cancellationToken);
            async Task<GXSettings> Read(ApplicationMode mode)
            {
                var record = await connection.SingleOrDefaultAsync<RelayConfiguration>(transaction,
                    Gurux.Data.Relay.Database.GXMetadataQueries.Select<RelayConfiguration>(connection, ("Mode", mode)), cancellationToken);
                return record is null ? new GXSettings { Mode = mode } : await store.LoadAsync(record.PayloadId);
            }
            var archive = new GXSettingsArchive
            {
                Databases = await connection.SelectAllAsync<Shared.GXDatabase>(transaction, cancellationToken),
                Client = await Read(ApplicationMode.Client),
                Server = await Read(ApplicationMode.Server),
                DataVault = await Read(ApplicationMode.DataVault)
            };
            foreach (GXDatabase database in archive.Databases)
            {
                var configuration = new GXDatabaseConfiguration { Id = database.Id, Type = (Gurux.Service.Orm.Common.Enums.DatabaseType)database.Type, ConnectionString = database.ConnectionString };
                var names = archive.Client.Databases.Where(d => d.Id == database.Id).SelectMany(d => d.Tables ?? []).Select(t => t.Name)
                    .Concat(archive.DataVault.Databases.Where(d => d.Id == database.Id).SelectMany(d => d.Mappings ?? []).Select(m => m.TargetTable?.Name))
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (names.Length == 0) continue;
                await using var physical = _connectionFactory.CreateConnection(configuration);
                await physical.OpenAsync(cancellationToken);
                var schema = new GXSchemaManager(physical);
                archive.Schemas[database.Id] = names.Where(schema.TableExist).Select(schema.Describe).ToList();
            }
            transaction.Commit();
            return archive;
        }
        finally { ConfigurationGate.Release(); }
    }

    public async Task ImportAllSettingsAsync(GXSettingsArchive archive, IReadOnlyDictionary<Guid, Guid>? databaseMap, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.FormatVersion != 2) throw new ArgumentException("Only shared database archive format 2 is supported.");
        if (archive.Databases is null || archive.Databases.Any(d => d is null || d.Id == Guid.Empty) ||
            archive.Databases.Select(d => d.Id).Distinct().Count() != archive.Databases.Count)
            throw new ArgumentException("The archive requires a catalog with distinct database IDs.");
        foreach (var database in archive.Databases) ValidateCatalogDatabase(database);
        if (archive.Client?.Mode != ApplicationMode.Client || archive.Server?.Mode != ApplicationMode.Server ||
            archive.DataVault?.Mode != ApplicationMode.DataVault)
            throw new ArgumentException("The file must contain client, server and datavault settings with matching modes.");
        if (databaseMap is { Count: > 0 })
        {
            await EnsureConfigurationStoreAsync(cancellationToken);
            await using var native = CreateConfigurationConnection();
            await native.OpenAsync(cancellationToken);
            using var connection = new GXDbConnection(native);
            var catalog = await connection.SelectAllAsync<Shared.GXDatabase>(cancellationToken: cancellationToken);
            GXSettingsImportDatabaseMapper.RemapArchive(archive, databaseMap, catalog);
        }
        GXSettings[] settings = [archive.Client, archive.Server, archive.DataVault];
        foreach (var mode in settings)
            foreach (var selection in mode.Databases)
            {
                var database = archive.Databases.SingleOrDefault(d => d.Id == selection.Id)
                    ?? throw new ArgumentException($"Database '{selection.Id}' is not in the archive catalog.");
                selection.Type = database.Type;
                selection.Description = database.Description;
                selection.ConnectionString = database.ConnectionString;
                selection.RecordSource = database.RecordSource;
                selection.DeleteMode = database.DeleteMode;
                selection.DeletedColumn = database.DeletedColumn;
            }
        foreach (var value in settings) ValidateArchiveShape(value);
        GXDataVaultLegacyMappingRecovery.Recover(
            archive.DataVault.Databases.SelectMany(database => database.Mappings ?? [])
                .Concat(archive.DataVault.Mappings ?? []),
            archive.Schemas.ToDictionary(item => item.Key, item => (IReadOnlyList<GXTableSchema>)item.Value));
        // Validate before changing the database. Empty, not-yet-configured modes are valid.
        GXModeConfiguration[] configurations = [GXConfigurationMapper.FromEntities<GXClientConfiguration>(archive.Client),
            GXConfigurationMapper.FromEntities<GXServerConfiguration>(archive.Server),
            GXConfigurationMapper.FromEntities<GXDataVaultConfiguration>(archive.DataVault)];
        foreach (var configuration in configurations)
            Validator.ValidateObject(configuration, new ValidationContext(configuration), validateAllProperties: true);
        GXDataVaultMappingValidator.Validate(configurations.OfType<GXDataVaultConfiguration>().Single().GetMappings());
        foreach (var transport in configurations.OfType<GXServerConfiguration>().Single().Transports)
            GXServerTransportValidator.Validate(transport);
        await ConfigurationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConfigurationStoreAsync(cancellationToken);
            await using var native = CreateConfigurationConnection();
            await native.OpenAsync(cancellationToken);
            using var connection = new GXDbConnection(native);
            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var database in archive.Databases)
                {
                    var existing = await connection.SingleOrDefaultAsync<Shared.GXDatabase>(transaction,
                        Gurux.Data.Relay.Database.GXMetadataQueries.Select<Shared.GXDatabase>(connection, ("Id", database.Id)), cancellationToken);
                    if (existing is not null) GXEntityPersistence.CopyMetadata(existing, database);
                    else database.ConcurrencyStamp = null;
                    await GXEntityPersistence.SaveAsync(connection, transaction, database, existing, cancellationToken);
                }
                foreach (var value in settings)
                {
                    var mode = value.Mode;
                    var record = await connection.SingleOrDefaultAsync<RelayConfiguration>(transaction,
                        Gurux.Data.Relay.Database.GXMetadataQueries.Select<RelayConfiguration>(connection, ("Mode", mode)), cancellationToken)
                        ?? new RelayConfiguration
                        {
                            Mode = mode,
                            PayloadId = await GetSettingsIdAsync(connection, transaction, mode, cancellationToken)
                        };
                    // Settings parents are local; entity IDs and references within each mode remain intact.
                    value.Id = record.PayloadId;
                    GXConfigurationMapper.NormalizeDatabaseReferences(value);
                    record.Updated = DateTimeOffset.UtcNow;
                    await new GXRelationalConfigurationStore(connection, transaction, cancellationToken, restoringArchive: true)
                        .SaveAsync(record, value);
                }
                var retainedDatabases = archive.Databases.Select(d => d.Id).ToHashSet();
                foreach (var database in await connection.SelectAllAsync<Shared.GXDatabase>(transaction, cancellationToken))
                    if (!retainedDatabases.Contains(database.Id))
                        await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<Shared.GXDatabase>(d => d.Id == database.Id), cancellationToken);
                var retained = archive.DataVault.Databases.SelectMany(d => d.Mappings ?? [])
                    .Concat(archive.DataVault.Mappings ?? []).Select(m => m.Id).ToHashSet();
                foreach (var state in await connection.SelectAllAsync<GXDataVaultMappingState>(transaction, cancellationToken))
                    if (!retained.Contains(state.Id))
                        await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<GXDataVaultMappingState>(s => s.Id == state.Id), cancellationToken);
                transaction.Commit();
            }
            catch
            {
                if (transaction.Connection is not null) transaction.Rollback();
                throw;
            }
        }
        finally { ConfigurationGate.Release(); }
        foreach (GXDatabase database in archive.Databases)
        {
            if (!archive.Schemas.TryGetValue(database.Id, out var schemas)) continue;
            var configuration = new GXDatabaseConfiguration { Id = database.Id, Type = (Gurux.Service.Orm.Common.Enums.DatabaseType)database.Type, ConnectionString = database.ConnectionString };
            await using var physical = _connectionFactory.CreateConnection(configuration);
            await physical.OpenAsync(cancellationToken);
            var schema = new GXSchemaManager(physical);
            // SQLite identifiers are case-insensitive, but its catalog lookup can be case-sensitive.
            var sqliteTables = configuration.Type == Gurux.Service.Orm.Common.Enums.DatabaseType.SqLite
                ? schema.GetTables().ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null;
            foreach (var table in schemas)
            {
                bool exists = sqliteTables is not null
                    ? sqliteTables.Contains(table.Name)
                    : schema.TableExist(table.Name);
                if (exists) continue;
                Database.GXDataVaultSchemaCompatibility.Normalize(table, configuration.Type);
                schema.CreateTable(table);
                sqliteTables?.Add(table.Name);
            }
        }
        foreach (var value in settings)
            _changes?.Publish(new(value.Mode.ToString().ToLowerInvariant(), GXRelayChangeKind.Configuration));
        foreach (var handlers in new[] { ClientConfigurationSaved, ServerConfigurationSaved, DataVaultConfigurationSaved })
            if (handlers is not null)
                foreach (Func<Task> handler in handlers.GetInvocationList()) await handler();
    }

    private static void ValidateArchiveShape(GXSettings settings)
    {
        void Require(bool condition)
        {
            if (!condition) throw new ArgumentException($"Invalid {settings.Mode} settings: required objects and lists must not be null.");
        }
        Require(settings.Databases is not null && settings.Transports is not null && settings.CorsAllowedOrigins is not null &&
            settings.ColumnAliases is not null && settings.ColumnAliases.All(c => c != null && Shared.GXColumnAlias.IsValid(c)));
        void Table(GXTable table)
        {
            Require(table is not null);
            Require(table!.Columns is not null && table.Keys is not null && table.Schedule is not null && table.ChangeTracking is not null);
        }
        void Mapping(GXDataVaultTableMapping mapping)
        {
            Require(mapping is not null);
            Require(mapping!.Columns is not null && mapping.Schedule is not null);
            Table(mapping.SourceTable);
            Table(mapping.TargetTable);
            Require(mapping.Columns!.All(c => c is not null));
        }
        foreach (var database in settings.Databases!)
        {
            Require(database is not null);
            foreach (var table in database!.Tables ?? []) Table(table);
            foreach (var mapping in database.Mappings ?? []) Mapping(mapping);
        }
        foreach (var mapping in settings.Mappings ?? []) Mapping(mapping);
        foreach (var transport in settings.Transports!)
        {
            Require(transport is not null);
            Require(transport!.Tables is not null);
            Require(transport.Tables!.All(route => route is not null));
        }
    }
}
