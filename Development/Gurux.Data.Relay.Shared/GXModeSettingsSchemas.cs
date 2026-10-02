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
using System.Text.Json.Serialization;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Shared;

public static class GXModeSettingsSchemas
{
    public static IReadOnlyDictionary<Guid, IReadOnlyList<GXTableSchema>> Read(string json, JsonSerializerOptions options)
    {
        JsonObject root = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("Settings must be a JSON object.");
        JsonObject? schemas = root["schemas"]?.AsObject();
        if (schemas is null) return new Dictionary<Guid, IReadOnlyList<GXTableSchema>>();
        var result = new Dictionary<Guid, IReadOnlyList<GXTableSchema>>();
        foreach ((string id, JsonNode? value) in schemas)
            result[Guid.Parse(id)] = value?.Deserialize<List<GXTableSchema>>(SchemaOptions(options)) ?? [];
        return result;
    }

    public static string Write(string settingsJson, IReadOnlyDictionary<Guid, IReadOnlyList<GXTableSchema>> schemas, JsonSerializerOptions options)
    {
        JsonObject root = JsonNode.Parse(settingsJson)?.AsObject() ?? throw new JsonException("Settings must be a JSON object.");
        if (schemas.Count == 0) { root.Remove("schemas"); return root.ToJsonString(options); }
        JsonObject values = [];
        foreach ((Guid id, IReadOnlyList<GXTableSchema> tables) in schemas)
            values[id.ToString()] = JsonSerializer.SerializeToNode(tables, SchemaOptions(options));
        root["schemas"] = values;
        return root.ToJsonString(options);
    }

    private static JsonSerializerOptions SchemaOptions(JsonSerializerOptions source)
    {
        var options = new JsonSerializerOptions(source);
        options.Converters.Add(new SchemaConverter());
        options.Converters.Add(new TypeConverter());
        return options;
    }

    private sealed class SchemaConverter : JsonConverter<GXTableSchema>
    {
        public override GXTableSchema Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            GXTableSchema schema = new() { Name = root.GetProperty("name").GetString() ?? string.Empty };
            if (root.TryGetProperty("columns", out JsonElement columns))
                foreach (JsonElement column in columns.EnumerateArray())
                {
                    GXColumnSchema item = column.Deserialize<GXColumnSchema>(options)!;
                    item.Parent = schema;
                    schema.Columns.Add(item);
                }
            return schema;
        }
        public override void Write(Utf8JsonWriter writer, GXTableSchema schema, JsonSerializerOptions options)
        {
            writer.WriteStartObject(); writer.WriteString("name", schema.Name); writer.WritePropertyName("columns"); JsonSerializer.Serialize(writer, schema.Columns, options); writer.WriteEndObject();
        }
    }

    private sealed class TypeConverter : JsonConverter<Type>
    {
        public override Type? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => Type.GetType(reader.GetString()!, true);
        public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options) => writer.WriteStringValue(value.AssemblyQualifiedName);
    }
}
