using System.Data;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Common;

namespace Gurux.Data.Relay.Configuration;

/// <summary>Persists configuration entities and their ordered references through Gurux.Service.</summary>
internal sealed class GXRelationalConfigurationStore(GXDbConnection connection, IDbTransaction transaction,
    CancellationToken cancellationToken, bool restoringArchive = false, DateTimeOffset? importedAt = null)
{
    internal static readonly Type[] EntityTypes =
    [
        typeof(GXSettings), typeof(GXDatabase), typeof(GXTable), typeof(GXTransport),
        typeof(GXDataVaultTableMapping), typeof(GXSchedule), typeof(GXChangeTracking),
        typeof(GXTransfer), typeof(GXTableColumn), typeof(GXTransportTable),
        typeof(GXDataVaultColumnMapping),
        typeof(GXDataSource), typeof(GXDataSourceRoute), typeof(GXDataSourceSecret),
        typeof(GXSettingsDatabaseReference), typeof(GXSettingsTransportReference),
        typeof(GXDatabaseTableReference), typeof(GXDatabaseMappingReference),
        typeof(GXTransportTableReference), typeof(GXMappingColumnReference),
        typeof(GXSettingsMappingReference), typeof(RelayConfiguration)
    ];

    private readonly HashSet<Guid> _savedMappings = [];
    private Dictionary<Guid, GXTable> _previousTableEntities = [];
    private Dictionary<Guid, GXDataVaultTableMapping> _previousMappingEntities = [];

    private async Task UpsertAsync<T>(T value) where T : class, IUnique<Guid>, IGXEntityMetadata
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var property in typeof(T).GetProperties())
            if (property.PropertyType == typeof(Guid) &&
                Attribute.GetCustomAttribute(property, typeof(ForeignKeyAttribute)) is ForeignKeyAttribute parent &&
                parent.OnDelete == Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade &&
                (Guid)property.GetValue(value)! == Guid.Empty)
                throw new InvalidOperationException($"{typeof(T).Name}.{property.Name} requires a parent ID.");
        if (value.Id == Guid.Empty) typeof(T).GetProperty(nameof(IUnique<Guid>.Id))!.SetValue(value, Guid.NewGuid());
        var existing = await connection.SingleOrDefaultAsync<T>(transaction, GXSelectArgs.SelectById<T>(value.Id), cancellationToken);
        if (restoringArchive)
        {
            // An explicit restore replaces the current values; backup concurrency stamps belong to another snapshot.
            if (existing is not null) GXEntityPersistence.CopyMetadata(existing, value);
            else value.ConcurrencyStamp = null;
        }
        await GXEntityPersistence.SaveAsync(connection, transaction, value, existing, cancellationToken, importedAt);
    }

    private async Task ReplaceReferencesAsync<T>(Guid configurationId) where T : class, IGXConfigurationReference
        => await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<T>(r => r.ConfigurationId == configurationId), cancellationToken);

    private async Task LinkAsync<T>(Guid configurationId, Guid owner, Guid target, int position)
        where T : class, IGXConfigurationReference, IGXEntityMetadata, new()
    {
        T reference = new()
        {
            ConfigurationId = configurationId,
            OwnerId = owner,
            TargetId = target
        };
        if (reference is IGXOrderedConfigurationReference ordered) ordered.Position = position;
        GXEntityPersistence.Initialize(reference);
        await connection.InsertAsync(transaction, GXInsertArgs.Insert(reference), cancellationToken);
    }

    public async Task SaveAsync(RelayConfiguration root, GXSettings settings)
    {
        _savedMappings.Clear();
        await ValidateSingleOwnersAsync(settings);
        ResolveTargetTables(settings);
        _previousTableEntities = await ReadEntitiesAsync<GXTable>();
        _previousMappingEntities = await ReadEntitiesAsync<GXDataVaultTableMapping>();
        var previousTables = await ReadReferencesAsync<GXDatabaseTableReference>(settings.Id);
        var previousDatabases = await ReadReferencesAsync<GXSettingsDatabaseReference>(settings.Id);
        var removedDatabases = previousDatabases.Select(r => r.TargetId).Except(settings.Databases.Select(d => d.Id)).ToHashSet();
        settings.Mappings?.RemoveAll(m => removedDatabases.Contains(m.Database));
        await UpsertAsync(settings);
        await ReplaceReferencesAsync<GXSettingsDatabaseReference>(settings.Id);
        await ReplaceReferencesAsync<GXSettingsTransportReference>(settings.Id);
        await ReplaceReferencesAsync<GXDatabaseTableReference>(settings.Id);
        await ReplaceReferencesAsync<GXDatabaseMappingReference>(settings.Id);
        await ReplaceReferencesAsync<GXTransportTableReference>(settings.Id);
        await ReplaceReferencesAsync<GXMappingColumnReference>(settings.Id);
        await ReplaceReferencesAsync<GXSettingsMappingReference>(settings.Id);

        for (int databaseIndex = 0; databaseIndex < settings.Databases.Count; ++databaseIndex)
        {
            GXDatabase database = settings.Databases[databaseIndex];
            var catalogEntry = await connection.SingleOrDefaultAsync<GXDatabase>(transaction,
                GXSelectArgs.SelectById<GXDatabase>(database.Id), cancellationToken)
                ?? throw new InvalidOperationException($"Database '{database.Id}' is not in the shared catalog. Add it in Databases first.");
            database.Description = catalogEntry.Description;
            database.ConnectionString = catalogEntry.ConnectionString;
            database.Type = catalogEntry.Type;
            database.RecordSource = catalogEntry.RecordSource;
            database.DeleteMode = catalogEntry.DeleteMode;
            database.DeletedColumn = catalogEntry.DeletedColumn;
            GXEntityPersistence.CopyMetadata(catalogEntry, database);
            await LinkAsync<GXSettingsDatabaseReference>(settings.Id, settings.Id, database.Id, databaseIndex);
            for (int index = 0; index < (database.Tables?.Count ?? 0); ++index)
            {
                GXTable table = database.Tables![index];
                table.Database = database.Id;
                if (_previousTableEntities.TryGetValue(table.Id, out var previousTable))
                    table.Schedule.Id = previousTable.ScheduleId;
                table.Schedule.Database = database.Id;
                table.Schedule.Settings = settings.Id;
                if (table.Schedule.Id == Guid.Empty) table.Schedule.Id = Guid.NewGuid();
                if (table.ChangeTracking.Id == Guid.Empty) table.ChangeTracking.Id = Guid.NewGuid();
                table.ScheduleId = table.Schedule.Id;
                table.ChangeTrackingId = table.ChangeTracking.Id;
                await UpsertAsync(table);
                table.Schedule.Table = table.Id;
                table.ChangeTracking.Table = table.Id;
                await UpsertAsync(table.Schedule);
                await UpsertAsync(table.ChangeTracking);
                await LinkAsync<GXDatabaseTableReference>(settings.Id, database.Id, table.Id, index);
                var existingColumns = (await connection.SelectAsync<GXTableColumn>(transaction,
                    GXSelectArgs.SelectAll<GXTableColumn>(column => column.Table == table), cancellationToken))
                    .ToDictionary(column => column.Id);
                HashSet<Guid> retainedColumns = [];
                foreach (string name in table.Columns.Concat(table.Keys).Distinct(StringComparer.Ordinal))
                {
                    int columnIndex = table.Columns.IndexOf(name), keyIndex = table.Keys.IndexOf(name);
                    var existingColumn = existingColumns.Values.SingleOrDefault(column => column.ColumnName == name);
                    GXTableColumn column = new()
                    {
                        Id = existingColumn?.Id ?? Guid.NewGuid(),
                        Table = table,
                        ColumnName = name,
                        ColumnIndex = columnIndex < 0 ? null : columnIndex,
                        KeyIndex = keyIndex < 0 ? null : keyIndex
                    };
                    if (existingColumn is not null) GXEntityPersistence.CopyMetadata(existingColumn, column);
                    await GXEntityPersistence.SaveAsync(connection, transaction, column, existingColumn, cancellationToken, importedAt);
                    retainedColumns.Add(column.Id);
                }
                foreach (Guid id in existingColumns.Keys.Where(id => !retainedColumns.Contains(id)))
                    await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<GXTableColumn>(column => column.Id == id), cancellationToken);
            }
            for (int index = 0; index < (database.Mappings?.Count ?? 0); ++index)
            {
                GXDataVaultTableMapping mapping = database.Mappings![index];
                mapping.Database = database.Id;
                mapping.Schedule.Database = database.Id;
                await SaveMappingAsync(settings.Id, mapping);
                await LinkAsync<GXDatabaseMappingReference>(settings.Id, database.Id, mapping.Id, index);
            }
        }
        for (int index = 0; index < (settings.Mappings?.Count ?? 0); ++index)
        {
            GXDataVaultTableMapping mapping = settings.Mappings![index];
            if (mapping.Database == Guid.Empty)
                mapping.Database = settings.Databases.Count == 1 ? settings.Databases[0].Id
                    : throw new InvalidOperationException("Root mapping requires an explicit Database ID when multiple databases are configured.");
            await SaveMappingAsync(settings.Id, mapping);
            await LinkAsync<GXSettingsMappingReference>(settings.Id, settings.Id, mapping.Id, index);
        }
        for (int transportIndex = 0; transportIndex < settings.Transports.Count; ++transportIndex)
        {
            GXTransport transport = settings.Transports[transportIndex];
            transport.Settings = settings.Id;
            var databaseIds = transport.Tables.Select(route => route.DatabaseId).Distinct().ToList();
            if (databaseIds.Count > 0)
                transport.Database = databaseIds.Count == 1 ? databaseIds[0] : null;
            if (transport.Transfer != null && transport.Transfer.Id == Guid.Empty) transport.Transfer.Id = Guid.NewGuid();
            transport.TransferId = transport.Transfer?.Id;
            await UpsertAsync(transport);
            if (transport.Transfer != null)
            {
                transport.Transfer.Transport = transport.Id;
                await UpsertAsync(transport.Transfer);
            }
            await LinkAsync<GXSettingsTransportReference>(settings.Id, settings.Id, transport.Id, transportIndex);
            for (int index = 0; index < transport.Tables.Count; ++index)
            {
                GXTransportTable route = transport.Tables[index];
                route.Transport = transport.Id;
                await UpsertAsync(route);
                await LinkAsync<GXTransportTableReference>(settings.Id, transport.Id, route.Id, index);
            }
        }
        root.PayloadId = settings.Id;
        root.Payload = settings;
        await UpsertAsync(root);
        await DeleteRemovedTablesAsync(previousTables.Select(r => r.TargetId).Distinct());
        await DeleteUnusedSchedulesAsync();
    }

    private async Task DeleteUnusedSchedulesAsync()
    {
        var tables = await ReadEntitiesAsync<GXTable>();
        var mappings = await ReadEntitiesAsync<GXDataVaultTableMapping>();
        HashSet<Guid> referenced = tables.Values.Select(t => t.ScheduleId)
            .Concat(mappings.Values.Select(m => m.Schedule.Id)).ToHashSet();
        foreach (var schedule in (await ReadEntitiesAsync<GXSchedule>()).Values)
            if (!referenced.Contains(schedule.Id))
                await connection.DeleteAsync(transaction,
                    GXDeleteArgs.Delete<GXSchedule>(s => s.Id == schedule.Id), cancellationToken);
    }

    private async Task DeleteRemovedTablesAsync(IEnumerable<Guid> candidates)
    {
        var tableLinks = await connection.SelectAllAsync<GXDatabaseTableReference>(transaction, cancellationToken);
        var routeLinks = await connection.SelectAllAsync<GXTransportTableReference>(transaction, cancellationToken);
        var routes = await connection.SelectAllAsync<GXTransportTable>(transaction, cancellationToken);
        HashSet<Guid> referencedTables = tableLinks.Select(r => r.TargetId).ToHashSet();
        foreach (var mapping in (await ReadEntitiesAsync<GXDataVaultTableMapping>()).Values)
        {
            referencedTables.Add(mapping.SourceTable.Id);
            referencedTables.Add(mapping.TargetTable.Id);
        }
        HashSet<Guid> referencedRoutes = routeLinks.Select(r => r.TargetId).ToHashSet();
        foreach (Guid tableId in candidates)
        {
            if (referencedTables.Contains(tableId)
                || routes.Any(r => r.TableId == tableId && referencedRoutes.Contains(r.Id))) continue;

            var removedTable = new GXTable { Id = tableId };
            // Remove unused route rows before deleting their target table.
            await connection.DeleteAsync(transaction,
                GXDeleteArgs.Delete<GXTransportTable>(r => r.TableId == tableId), cancellationToken);
            await connection.DeleteAsync(transaction,
                GXDeleteArgs.Delete<GXTableColumn>(c => c.Table == removedTable), cancellationToken);
            // Table schedules use NO ACTION to avoid multiple cascade paths on SQL Server.
            // Delete them before their parent, within the same configuration transaction.
            await connection.DeleteAsync(transaction,
                GXDeleteArgs.Delete<GXSchedule>(s => s.Table == tableId), cancellationToken);
            await connection.DeleteAsync(transaction,
                GXDeleteArgs.Delete<GXTable>(t => t.Id == tableId), cancellationToken);
        }
    }

    private async Task SaveMappingAsync(Guid configurationId, GXDataVaultTableMapping mapping)
    {
        if (mapping.Id == Guid.Empty) mapping.Id = Guid.NewGuid();
        if (!_savedMappings.Add(mapping.Id)) return;
        if (_previousMappingEntities.TryGetValue(mapping.Id, out var previous))
        {
            mapping.Schedule.Id = previous.Schedule.Id;
        }
        mapping.Schedule.Settings = configurationId;
        mapping.Schedule.Database = mapping.Database;
        if (mapping.Schedule.Id == Guid.Empty) mapping.Schedule.Id = Guid.NewGuid();
        mapping.Schedule.Table = null;
        mapping.Schedule = mapping.Schedule;
        mapping.SourceTable = await SaveMappingTableAsync(mapping.SourceTable, mapping.Database);
        mapping.TargetTable = await SaveMappingTableAsync(mapping.TargetTable, mapping.Database);
        await UpsertAsync(mapping);
        mapping.Schedule.Mapping = mapping.Id;
        await UpsertAsync(mapping.Schedule);
        for (int index = 0; index < mapping.Columns.Count; ++index)
        {
            GXDataVaultColumnMapping column = mapping.Columns[index];
            column.SourceMappingId = mapping.Id;
            await UpsertAsync(column);
            await LinkAsync<GXMappingColumnReference>(configurationId, mapping.Id, column.Id, index);
        }
    }

    private async Task<GXTable> SaveMappingTableAsync(GXTable table, Guid databaseId)
    {
        if (table == null || string.IsNullOrWhiteSpace(table.Name))
            throw new InvalidOperationException("Mapping source and target tables are required.");
        var tables = await ReadEntitiesAsync<GXTable>();
        // A new reference already has an ID, but may name an existing configured table.
        var existing = tables.GetValueOrDefault(table.Id) ?? (restoringArchive && importedAt is null ? null : tables.Values.FirstOrDefault(t =>
            t.Database == databaseId && string.Equals(t.Name, table.Name, StringComparison.OrdinalIgnoreCase)));
        if (existing != null)
        {
            if (existing.Database != databaseId)
                throw new InvalidOperationException("Mapping table belongs to another database.");
            if (!restoringArchive) return existing;
            // Legacy mode imports contain only a name and get a new ID when deserialized.
            // Reuse the stored table and its definition when resolving such a reference.
            if (existing.Id != table.Id) table = existing;
            // Preserve stored dependent IDs, which are not part of the JSON table reference.
            table.ScheduleId = existing.ScheduleId;
            table.ChangeTrackingId = existing.ChangeTrackingId;
        }
        table.Database = databaseId;
        if (table.Id == Guid.Empty) table.Id = Guid.NewGuid();
        await UpsertAsync(table);
        return table;
    }

    private async Task ValidateSingleOwnersAsync(GXSettings settings)
    {
        if (settings.Databases.Select(d => d.Id).Distinct().Count() != settings.Databases.Count)
            throw new InvalidOperationException("Each database can only be selected once per mode.");
        if (settings.Databases.SelectMany(d => (d.Mappings ?? []).Select(m => (m.Id, Database: d.Id)))
            .Where(m => m.Id != Guid.Empty).GroupBy(m => m.Id).Any(g => g.Select(m => m.Database).Distinct().Count() > 1))
            throw new InvalidOperationException("A mapping cannot belong to multiple databases. Copy it with a new ID first.");
        await ValidateAsync<GXDatabaseTableReference>(settings.Databases.SelectMany(d => d.Tables ?? []).Select(t => t.Id));
        await ValidateAsync<GXSettingsTransportReference>(settings.Transports.Select(t => t.Id));
        var mappingIds = settings.Databases.SelectMany(d => d.Mappings ?? []).Concat(settings.Mappings ?? []).Select(m => m.Id).ToHashSet();
        await ValidateAsync<GXDatabaseMappingReference>(mappingIds);
        await ValidateAsync<GXSettingsMappingReference>(mappingIds);

        async Task ValidateAsync<T>(IEnumerable<Guid> ids) where T : class, IGXConfigurationReference
        {
            var selected = ids.ToHashSet();
            var links = await connection.SelectAllAsync<T>(transaction, cancellationToken);
            if (links.Any(r => r.ConfigurationId != settings.Id && selected.Contains(r.TargetId)))
                throw new InvalidOperationException("An entity cannot belong to multiple settings parents. Copy it with a new ID before assigning it to another configuration.");
        }
    }

    private static void ResolveTargetTables(GXSettings settings)
    {
        foreach (GXTransport transport in settings.Transports)
        {
            foreach (GXTransportTable route in transport.Tables)
            {
                GXDatabase database = settings.Databases.SingleOrDefault(db => db.Id == route.DatabaseId)
                    ?? throw new InvalidOperationException("Transport table references a missing database.");
                GXTable? table = database.Tables?.SingleOrDefault(t => string.Equals(t.Name, route.Table, StringComparison.OrdinalIgnoreCase));
                if (table == null && settings.Mode == ApplicationMode.Server)
                {
                    Guid tableId = route.TableId is Guid existingId && existingId != Guid.Empty
                        ? existingId
                        : Guid.NewGuid();
                    table = new GXTable
                    {
                        Name = route.Table,
                        Id = tableId
                    };
                    (database.Tables ??= []).Add(table);
                }
                if (table == null) throw new InvalidOperationException($"Transport source table '{route.Table}' is not configured.");
                route.TableId = table.Id;
            }
        }
        foreach (var mapping in settings.Mappings ?? [])
            if (mapping.Database != Guid.Empty && !settings.Databases.Any(d => d.Id == mapping.Database))
                throw new InvalidOperationException("Mapping references a database not selected for this mode.");
    }

    private async Task<Dictionary<Guid, T>> ReadEntitiesAsync<T>() where T : IUnique<Guid>
    {
        var select = GXSelectArgs.SelectAll<T>();
        if (typeof(T) == typeof(GXDataVaultTableMapping))
        {
            select.Joins.AddLeftJoin<GXDataVaultTableMapping, GXTable>(m => m.SourceTable, t => t.Id);
            select.Columns.Add<GXTable>();
        }
        if (typeof(T) == typeof(GXTableColumn))
        {
            select.Joins.AddLeftJoin<GXTableColumn, GXTable>(c => c.Table, t => t.Id);
            select.Columns.Add<GXTable>();
        }
        var result = (await connection.SelectAsync<T>(transaction, select, cancellationToken)).ToDictionary(v => v.Id);
        if (typeof(T) == typeof(GXDataVaultTableMapping))
        {
            // Two properties reference GXTable; load each relationship independently.
            var targets = GXSelectArgs.SelectAll<GXDataVaultTableMapping>();
            targets.Joins.AddLeftJoin<GXDataVaultTableMapping, GXTable>(m => m.TargetTable, t => t.Id);
            targets.Columns.Add<GXTable>();
            foreach (var mapping in await connection.SelectAsync<GXDataVaultTableMapping>(transaction, targets, cancellationToken))
                ((GXDataVaultTableMapping)(object)result[mapping.Id]).TargetTable = mapping.TargetTable;
        }
        return result;
    }

    private async Task<List<T>> ReadReferencesAsync<T>(Guid configurationId) where T : class, IGXConfigurationReference
        => await connection.SelectAsync<T>(transaction, GXSelectArgs.SelectAll<T>(r => r.ConfigurationId == configurationId), cancellationToken);

    private static List<T> Resolve<T, TReference>(IEnumerable<TReference> references, Guid owner, Dictionary<Guid, T> entities)
        where TReference : IGXConfigurationReference
        => references.Where(r => r.OwnerId == owner).OrderBy(r => r is IGXOrderedConfigurationReference ordered ? ordered.Position : 0).Select(r => entities.TryGetValue(r.TargetId, out T? value)
            ? value : throw new InvalidOperationException($"Configuration reference to {typeof(T).Name} '{r.TargetId}' is missing.")).ToList();

    public async Task<GXSettings> LoadAsync(Guid settingsId)
    {
        GXSettings settings = await connection.SingleOrDefaultAsync<GXSettings>(transaction,
            GXSelectArgs.SelectAll<GXSettings>(s => s.Id == settingsId), cancellationToken)
            ?? throw new InvalidOperationException("Configuration payload reference is missing.");
        var databases = await ReadEntitiesAsync<GXDatabase>();
        var tables = await ReadEntitiesAsync<GXTable>();
        var schedules = await ReadEntitiesAsync<GXSchedule>();
        var tracking = await ReadEntitiesAsync<GXChangeTracking>();
        var transfers = await ReadEntitiesAsync<GXTransfer>();
        var transports = await ReadEntitiesAsync<GXTransport>();
        var routes = await ReadEntitiesAsync<GXTransportTable>();
        var columns = await ReadEntitiesAsync<GXTableColumn>();
        var mappings = await ReadEntitiesAsync<GXDataVaultTableMapping>();
        var mappingColumns = await ReadEntitiesAsync<GXDataVaultColumnMapping>();
        var databaseLinks = await ReadReferencesAsync<GXSettingsDatabaseReference>(settingsId);
        var tableLinks = await ReadReferencesAsync<GXDatabaseTableReference>(settingsId);
        var transportLinks = await ReadReferencesAsync<GXSettingsTransportReference>(settingsId);
        var routeLinks = await ReadReferencesAsync<GXTransportTableReference>(settingsId);
        var mappingLinks = await ReadReferencesAsync<GXDatabaseMappingReference>(settingsId);
        var rootMappingLinks = await ReadReferencesAsync<GXSettingsMappingReference>(settingsId);
        var mappingColumnLinks = await ReadReferencesAsync<GXMappingColumnReference>(settingsId);

        void RestoreMapping(GXDataVaultTableMapping mapping)
        {
            mapping.SourceTable = tables[mapping.SourceTable!.Id];
            mapping.TargetTable = tables[mapping.TargetTable!.Id];
            if (mapping.Schedule is GXSchedule s && s.Id != Guid.Empty)
            {
                mapping.Schedule = schedules[s.Id];
            }
            mapping.Columns = Resolve(mappingColumnLinks, mapping.Id, mappingColumns);
        }
        settings.Databases = Resolve(databaseLinks, settingsId, databases);
        foreach (GXDatabase database in settings.Databases)
        {
            database.Tables = Resolve(tableLinks, database.Id, tables);
            foreach (GXTable table in database.Tables)
            {
                table.Schedule = schedules[table.ScheduleId];
                table.ChangeTracking = tracking[table.ChangeTrackingId];
                table.Columns = columns.Values.Where(c => c.Table.Id == table.Id && c.ColumnIndex.HasValue).OrderBy(c => c.ColumnIndex).Select(c => c.ColumnName).ToList();
                table.Keys = columns.Values.Where(c => c.Table.Id == table.Id && c.KeyIndex.HasValue).OrderBy(c => c.KeyIndex).Select(c => c.ColumnName).ToList();
            }
            database.Mappings = Resolve(mappingLinks, database.Id, mappings);
            foreach (var mapping in database.Mappings) RestoreMapping(mapping);
        }
        settings.Mappings = Resolve(rootMappingLinks, settingsId, mappings);
        foreach (var mapping in settings.Mappings) RestoreMapping(mapping);
        settings.Transports = Resolve(transportLinks, settingsId, transports);
        foreach (GXTransport transport in settings.Transports)
        {
            transport.Transfer = transport.TransferId is Guid transferId ? transfers[transferId] : null;
            transport.Tables = Resolve(routeLinks, transport.Id, routes);
        }
        return settings;
    }
}


