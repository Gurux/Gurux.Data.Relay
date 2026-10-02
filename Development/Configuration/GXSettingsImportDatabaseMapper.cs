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
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Configuration;

public static class GXSettingsImportDatabaseMapper
{
    public static void RemapSettings(
        GXSettings settings,
        IReadOnlyDictionary<Guid, Guid> databaseMap,
        IReadOnlyList<GXDatabase> targetCatalog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(databaseMap);
        ArgumentNullException.ThrowIfNull(targetCatalog);
        if (settings.Databases.Count == 0) return;

        var importedIds = settings.Databases.Select(database => database.Id).ToArray();
        ValidateMappings(importedIds, databaseMap, targetCatalog);
        var targets = targetCatalog.ToDictionary(database => database.Id);
        var idMap = importedIds.Distinct().ToDictionary(sourceId => sourceId, sourceId => databaseMap[sourceId]);

        foreach (GXDatabase database in settings.Databases)
        {
            GXDatabase target = targets[idMap[database.Id]];
            database.Id = target.Id;
            ApplyCatalogFields(database, target);
            foreach (GXTable table in database.Tables ?? []) table.Database = target.Id;
            foreach (GXDataVaultTableMapping mapping in database.Mappings ?? []) mapping.Database = target.Id;
        }

        foreach (GXDataVaultTableMapping mapping in settings.Mappings ?? [])
            if (mapping.Database != Guid.Empty && idMap.TryGetValue(mapping.Database, out Guid targetId))
                mapping.Database = targetId;

        foreach (GXTransport transport in settings.Transports)
        {
            if (transport.Database.HasValue && idMap.TryGetValue(transport.Database.Value, out Guid targetId))
                transport.Database = targetId;
            foreach (GXTransportTable table in transport.Tables)
                if (idMap.TryGetValue(table.DatabaseId, out targetId))
                    table.DatabaseId = targetId;
        }
    }

    public static void RemapArchive(
        GXSettingsArchive archive,
        IReadOnlyDictionary<Guid, Guid> databaseMap,
        IReadOnlyList<GXDatabase> targetCatalog)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(databaseMap);
        ArgumentNullException.ThrowIfNull(targetCatalog);
        if (archive.Databases.Count == 0) return;

        var importedIds = archive.Databases.Select(database => database.Id).ToArray();
        ValidateMappings(importedIds, databaseMap, targetCatalog);
        var targets = targetCatalog.ToDictionary(database => database.Id);
        var idMap = importedIds.Distinct().ToDictionary(sourceId => sourceId, sourceId => databaseMap[sourceId]);

        RemapSettings(archive.Client, databaseMap, targetCatalog);
        RemapSettings(archive.Server, databaseMap, targetCatalog);
        RemapSettings(archive.DataVault, databaseMap, targetCatalog);

        archive.Databases = importedIds.Distinct().Select(sourceId => targets[idMap[sourceId]]).Select(CloneCatalog).ToList();

        var remappedSchemas = new Dictionary<Guid, List<GXTableSchema>>();
        foreach ((Guid sourceId, List<GXTableSchema> schemas) in archive.Schemas)
        {
            if (!idMap.TryGetValue(sourceId, out Guid targetId))
                throw new InvalidOperationException($"Imported schema references database '{sourceId}' without a selected target database.");
            remappedSchemas[targetId] = schemas;
        }
        archive.Schemas = remappedSchemas;
    }

    public static IReadOnlyDictionary<Guid, IReadOnlyList<GXTableSchema>> RemapSchemas(
        IReadOnlyDictionary<Guid, IReadOnlyList<GXTableSchema>> schemas,
        IReadOnlyDictionary<Guid, Guid> databaseMap)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(databaseMap);
        if (schemas.Count == 0 || databaseMap.Count == 0) return schemas;

        Dictionary<Guid, IReadOnlyList<GXTableSchema>> remapped = [];
        foreach ((Guid sourceId, IReadOnlyList<GXTableSchema> value) in schemas)
        {
            if (!databaseMap.TryGetValue(sourceId, out Guid targetId))
                throw new InvalidOperationException($"Imported schema references database '{sourceId}' without a selected target database.");
            remapped[targetId] = value;
        }
        return remapped;
    }

    private static void ValidateMappings(
        IReadOnlyList<Guid> importedIds,
        IReadOnlyDictionary<Guid, Guid> databaseMap,
        IReadOnlyList<GXDatabase> targetCatalog)
    {
        if (importedIds.Count == 0) return;

        var importedSet = importedIds.Distinct().ToHashSet();
        foreach (Guid sourceId in importedSet)
            if (!databaseMap.TryGetValue(sourceId, out Guid selected) || selected == Guid.Empty)
                throw new InvalidOperationException($"Select a target database for imported database '{sourceId}'.");

        foreach (Guid sourceId in databaseMap.Keys)
            if (!importedSet.Contains(sourceId))
                throw new InvalidOperationException($"Import mapping includes unknown source database '{sourceId}'.");

        var selectedTargets = importedSet.Select(sourceId => databaseMap[sourceId]).ToArray();
        if (selectedTargets.Distinct().Count() != selectedTargets.Length)
            throw new InvalidOperationException("Each imported database must map to a different target database.");

        var targetIds = targetCatalog.Select(database => database.Id).ToHashSet();
        foreach (Guid targetId in selectedTargets)
            if (!targetIds.Contains(targetId))
                throw new InvalidOperationException($"Selected target database '{targetId}' does not exist in the current catalog.");
    }

    private static GXDatabase CloneCatalog(GXDatabase value) => new()
    {
        Id = value.Id,
        Type = value.Type,
        Description = value.Description,
        ConnectionString = value.ConnectionString,
        RecordSource = value.RecordSource,
        DeleteMode = value.DeleteMode,
        DeletedColumn = value.DeletedColumn,
        ConcurrencyStamp = value.ConcurrencyStamp,
        CreationTime = value.CreationTime,
        Updated = value.Updated,
    };

    private static void ApplyCatalogFields(GXDatabase target, GXDatabase source)
    {
        target.Type = source.Type;
        target.Description = source.Description;
        target.ConnectionString = source.ConnectionString;
        target.RecordSource = source.RecordSource;
        target.DeleteMode = source.DeleteMode;
        target.DeletedColumn = source.DeletedColumn;
    }
}