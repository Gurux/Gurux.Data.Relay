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

using Gurux.Data.Relay.Shared.Enums;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Gurux.Data.Relay.Shared;

/// <summary>Exports and merges one database's client settings.</summary>
public static class GXClientDatabaseSettings
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Export(GXSettings settings, Guid databaseId)
    {
        var copy = JsonSerializer.Deserialize<GXSettings>(JsonSerializer.Serialize(settings, Options), Options)!;
        copy.Databases = copy.Databases.Where(d => d.Id == databaseId).ToList();
        if (copy.Databases.Count != 1)
            throw new InvalidOperationException("The selected database has no client settings to export.");
        foreach (var transport in copy.Transports)
            transport.Tables = transport.Tables.Where(t => t.DatabaseId == databaseId).ToList();
        copy.Transports.RemoveAll(t => t.Tables.Count == 0);
        return GXDatabaseSelectionsJson.WithoutCatalogFields(JsonSerializer.Serialize(copy, Options), Options);
    }

    public static GXSettings Import(GXSettings current, GXDatabase database, string json)
    {
        var imported = JsonSerializer.Deserialize<GXSettings>(json, Options)
            ?? throw new JsonException("Settings must be a JSON object.");
        if (imported.Mode != ApplicationMode.Client)
            throw new InvalidOperationException("Select a client settings file.");
        var selection = imported.Databases.SingleOrDefault(d => d.Id == database.Id)
            ?? (imported.Databases.Count == 1 ? imported.Databases[0] : null)
            ?? throw new InvalidOperationException("The file contains multiple databases or no database settings. Export a single database's settings first.");
        bool copying = selection.Id != database.Id;
        var result = JsonSerializer.Deserialize<GXSettings>(JsonSerializer.Serialize(current, Options), Options)!;
        var target = result.Databases.SingleOrDefault(d => d.Id == database.Id);
        if (target is null)
        {
            target = JsonSerializer.Deserialize<GXDatabase>(JsonSerializer.Serialize(database, Options), Options)!;
            result.Databases.Add(target);
        }
        if (copying)
        {
            foreach (var table in selection.Tables!)
            {
                table.Id = Guid.NewGuid();
                table.Schedule.Id = Guid.NewGuid();
                table.Schedule.Database = database.Id;
                table.ChangeTracking.Id = Guid.NewGuid();
                table.Database = database.Id;
                table.LastTransferred = null;
                table.NextTransferTime = null;
            }
        }
        target.Tables = selection.Tables;
        foreach (var transport in result.Transports)
            transport.Tables.RemoveAll(t => t.DatabaseId == database.Id);
        foreach (var transport in imported.Transports)
        {
            transport.Tables = transport.Tables.Where(t => t.DatabaseId == selection.Id).ToList();
            if (transport.Tables.Count == 0) continue;
            foreach (var route in transport.Tables)
            {
                route.DatabaseId = database.Id;
                if (copying) route.Id = Guid.NewGuid();
            }
            transport.Database = database.Id;
            var existing = result.Transports.SingleOrDefault(t => t.Id == transport.Id);
            if (existing is null) result.Transports.Add(transport);
            else existing.Tables.AddRange(transport.Tables);
        }

        // File versions can be old. Use the freshly loaded versions of matching entities.
        var versions = new Dictionary<string, JsonObject>();
        void Visit(JsonNode? node, Action<JsonObject> action)
        {
            if (node is JsonObject obj)
            {
                action(obj);
                foreach (var child in obj.ToArray()) Visit(child.Value, action);
            }
            else if (node is JsonArray array)
                foreach (var child in array) Visit(child, action);
        }
        Visit(JsonSerializer.SerializeToNode(current, Options), obj =>
        {
            if (obj["id"] is JsonValue id) versions[id.ToString()] = obj;
        });
        var merged = JsonSerializer.SerializeToNode(result, Options)!;
        Visit(merged, obj =>
        {
            if (obj["id"] is not JsonValue id) return;
            versions.TryGetValue(id.ToString(), out var original);
            foreach (var key in new[] { "creationTime", "updated", "concurrencyStamp" })
                if (obj.ContainsKey(key)) obj[key] = original?[key]?.DeepClone();
        });
        return merged.Deserialize<GXSettings>(Options)!;
    }
}
