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

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gurux.Data.Relay.Shared;

/// <summary>Mode exports contain database selections and omit local concurrency stamps.</summary>
public static class GXDatabaseSelectionsJson
{
    public static string WithoutCatalogFields(string json, JsonSerializerOptions options)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var databases = root.FirstOrDefault(p => p.Key.Equals("Databases", StringComparison.OrdinalIgnoreCase)).Value as JsonArray;
        foreach (var database in databases ?? [])
            foreach (var key in database!.AsObject().Select(p => p.Key).ToArray())
                if (!new[] { "Id", "Tables", "Mappings" }.Contains(key, StringComparer.OrdinalIgnoreCase))
                    database.AsObject().Remove(key);
        RemoveConcurrencyStamps(root);
        return root.ToJsonString(options);
    }

    public static string WithoutConcurrencyStamps(string json, JsonSerializerOptions options)
    {
        var root = JsonNode.Parse(json);
        RemoveConcurrencyStamps(root);
        return root!.ToJsonString(options);
    }

    private static void RemoveConcurrencyStamps(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var property in obj.ToArray())
            {
                if (property.Key.Equals("ConcurrencyStamp", StringComparison.OrdinalIgnoreCase))
                    obj.Remove(property.Key);
                else RemoveConcurrencyStamps(property.Value);
            }
        else if (node is JsonArray array)
            foreach (var item in array) RemoveConcurrencyStamps(item);
    }
}
