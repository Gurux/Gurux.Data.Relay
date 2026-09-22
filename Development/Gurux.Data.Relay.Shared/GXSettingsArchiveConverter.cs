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
using System.Text.Json.Serialization;

namespace Gurux.Data.Relay.Shared;

/// <summary>Archives store connection definitions once, and mode selections by ID.</summary>
public sealed class GXSettingsArchiveConverter : JsonConverter<GXSettingsArchive>
{
    public override GXSettingsArchive Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        JsonElement Get(string name) => root.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        if (Get("formatVersion").ValueKind != JsonValueKind.Number || Get("formatVersion").GetInt32() != 2)
            throw new JsonException("Only shared database archive format 2 is supported.");
        return new()
        {
            Databases = Get("databases").Deserialize<List<GXDatabase>>(options) ?? throw new JsonException("Database catalog is required."),
            Client = Get("client").Deserialize<GXSettings>(options) ?? throw new JsonException("Client settings are required."),
            Server = Get("server").Deserialize<GXSettings>(options) ?? throw new JsonException("Server settings are required."),
            DataVault = Get("dataVault").Deserialize<GXSettings>(options) ?? throw new JsonException("Data Vault settings are required.")
        };
    }

    public override void Write(Utf8JsonWriter writer, GXSettingsArchive value, JsonSerializerOptions options)
    {
        string Name(string name) => options.PropertyNamingPolicy?.ConvertName(name) ?? name;
        writer.WriteStartObject();
        writer.WriteNumber(Name(nameof(value.FormatVersion)), value.FormatVersion);
        writer.WritePropertyName(Name(nameof(value.Databases)));
        JsonSerializer.Serialize(writer, value.Databases, options);
        void Mode(string name, GXSettings settings)
        {
            var node = JsonSerializer.SerializeToNode(settings, options)!.AsObject();
            foreach (var database in node[Name(nameof(settings.Databases))]!.AsArray())
                foreach (var key in database!.AsObject().Select(p => p.Key).ToArray())
                    if (key != Name("Id") && key != Name("Tables") && key != Name("Mappings")) database.AsObject().Remove(key);
            writer.WritePropertyName(Name(name));
            node.WriteTo(writer, options);
        }
        Mode(nameof(value.Client), value.Client);
        Mode(nameof(value.Server), value.Server);
        Mode(nameof(value.DataVault), value.DataVault);
        writer.WriteEndObject();
    }
}
