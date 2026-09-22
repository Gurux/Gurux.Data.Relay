using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Transport;

public sealed class GXJsonMessageSerializer : IGXMessageSerializer
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                typeInfo =>
                {
                    if (typeInfo.Type != typeof(GXColumnSchema)) return;
                    // Parent is a local ORM back-reference, not part of the wire schema.
                    JsonPropertyInfo? parent = typeInfo.Properties.FirstOrDefault(property =>
                        string.Equals(property.Name, nameof(GXColumnSchema.Parent), StringComparison.OrdinalIgnoreCase));
                    if (parent != null) typeInfo.Properties.Remove(parent);
                }
            }
        },
        Converters =
        {
            new JsonStringEnumConverter(),
            new GXTableSchemaJsonConverter(),
            new GXSystemTypeJsonConverter(),
        },
    };

    public byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, _options);
    }

    public T Deserialize<T>(ReadOnlySpan<byte> payload)
    {
        T? value = JsonSerializer.Deserialize<T>(payload, _options);
        if (value is null)
        {
            throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} payload.");
        }

        return value;
    }
}

internal sealed class GXTableSchemaJsonConverter : JsonConverter<GXTableSchema>
{
    public override GXTableSchema Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        GXTableSchema schema = new();
        if (TryGetProperty(root, "catalog", out JsonElement catalog))
        {
            schema.Catalog = catalog.GetString();
        }
        if (TryGetProperty(root, "schema", out JsonElement schemaName))
        {
            schema.Schema = schemaName.GetString();
        }
        if (TryGetProperty(root, "name", out JsonElement name))
        {
            schema.Name = name.GetString() ?? string.Empty;
        }
        if (TryGetProperty(root, "tableType", out JsonElement tableType))
        {
            schema.TableType = tableType.GetString();
        }
        if (TryGetProperty(root, "comment", out JsonElement comment))
        {
            schema.Comment = comment.GetString();
        }
        if (TryGetProperty(root, "columns", out JsonElement columns) && columns.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement columnElement in columns.EnumerateArray())
            {
                GXColumnSchema? column = columnElement.Deserialize<GXColumnSchema>(options);
                if (column is not null)
                {
                    column.Parent = schema;
                    schema.Columns.Add(column);
                }
            }
        }

        return schema;
    }

    public override void Write(Utf8JsonWriter writer, GXTableSchema value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        WriteString(writer, "catalog", value.Catalog);
        WriteString(writer, "schema", value.Schema);
        WriteString(writer, "name", value.Name);
        WriteString(writer, "tableType", value.TableType);
        WriteString(writer, "comment", value.Comment);
        writer.WritePropertyName("columns");
        JsonSerializer.Serialize(writer, value.Columns, options);
        writer.WriteEndObject();
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        return root.TryGetProperty(name, out value) ||
            root.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value);
    }

    private static void WriteString(Utf8JsonWriter writer, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            writer.WriteString(name, value);
        }
    }
}

public sealed class GXSystemTypeJsonConverter : JsonConverter<Type?>
{
    public override Type? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        string? name = reader.GetString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        Type? type = Type.GetType(name, throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        return name switch
        {
            "System.Boolean" => typeof(bool),
            "System.Byte" => typeof(byte),
            "System.SByte" => typeof(sbyte),
            "System.Int16" => typeof(short),
            "System.UInt16" => typeof(ushort),
            "System.Int32" => typeof(int),
            "System.UInt32" => typeof(uint),
            "System.Int64" => typeof(long),
            "System.UInt64" => typeof(ulong),
            "System.Single" => typeof(float),
            "System.Double" => typeof(double),
            "System.Decimal" => typeof(decimal),
            "System.String" => typeof(string),
            "System.Guid" => typeof(Guid),
            "System.DateTime" => typeof(DateTime),
            "System.DateTimeOffset" => typeof(DateTimeOffset),
            "System.DateOnly" => typeof(DateOnly),
            "System.TimeOnly" => typeof(TimeOnly),
            "System.Byte[]" => typeof(byte[]),
            _ => throw new JsonException($"Unsupported type name '{name}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, Type? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.FullName);
    }
}

