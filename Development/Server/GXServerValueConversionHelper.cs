using System.Text.Json;
using System.Globalization;
using Gurux.Service.Orm.Common.Enums;

namespace Gurux.Data.Relay.Server;

internal static class GXServerValueConversionHelper
{
    public static object? ConvertJsonValue(JsonElement value, Type targetType)
    {
        return ConvertJsonValue(value, targetType, databaseType: null);
    }

    public static object? ConvertJsonValue(JsonElement value,
        Type targetType,
        DatabaseType? databaseType)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (databaseType == DatabaseType.Oracle &&
            (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
        {
            return value.GetBoolean() ? 1 : 0;
        }

        Type actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (actualType == typeof(object))
        {
            return ConvertJsonObjectValue(value);
        }

        if (actualType == typeof(string))
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        if (actualType == typeof(byte[]) && value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            if (text is null)
            {
                return null;
            }

            if (Guid.TryParse(text, out Guid guidBytes))
            {
                return guidBytes.ToByteArray();
            }

            return Convert.FromBase64String(text);
        }

        if (actualType == typeof(Guid) && value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            return text is null ? null : Guid.Parse(text);
        }

        if (actualType == typeof(Guid) && value.ValueKind == JsonValueKind.Array)
        {
            byte[] bytes = JsonSerializer.Deserialize<byte[]>(value)!;
            return new Guid(bytes);
        }

        if (actualType == typeof(DateOnly) && value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            return text is null ? null : DateOnly.Parse(text);
        }

        if (actualType == typeof(TimeOnly) && value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            return text is null ? null : TimeOnly.Parse(text);
        }

        if (actualType == typeof(DateTimeOffset) && value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            return text is null ? null : DateTimeOffset.Parse(text);
        }

        if (actualType == typeof(DateTime) && value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            return text is null ? null : DateTime.Parse(text);
        }

        if (actualType == typeof(DateTime) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out DateTimeOffset dtoAsDateTime))
        {
            return dtoAsDateTime.UtcDateTime;
        }

        if (actualType == typeof(DateTimeOffset) && value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out DateTime dtAsOffset))
        {
            return new DateTimeOffset(dtAsOffset);
        }

        if (actualType == typeof(bool) && value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDecimal() != 0;
        }

        if (actualType == typeof(bool) && value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsedBool))
        {
            return parsedBool;
        }

        if (IsNumericType(actualType) && value.ValueKind == JsonValueKind.String)
        {
            string text = value.GetString() ?? string.Empty;
            return actualType switch
            {
                var t when t == typeof(decimal) => decimal.Parse(text, CultureInfo.InvariantCulture),
                var t when t == typeof(double) => double.Parse(text, CultureInfo.InvariantCulture),
                var t when t == typeof(float) => float.Parse(text, CultureInfo.InvariantCulture),
                var t when t == typeof(long) => long.Parse(text, CultureInfo.InvariantCulture),
                var t when t == typeof(ulong) => ulong.Parse(text, CultureInfo.InvariantCulture),
                var t when t == typeof(int) => int.Parse(text, CultureInfo.InvariantCulture),
                var t when t == typeof(uint) => uint.Parse(text, CultureInfo.InvariantCulture),
                var t when t == typeof(short) => short.Parse(text, CultureInfo.InvariantCulture),
                var t when t == typeof(ushort) => ushort.Parse(text, CultureInfo.InvariantCulture),
                var t when t == typeof(byte) => byte.Parse(text, CultureInfo.InvariantCulture),
                var t when t == typeof(sbyte) => sbyte.Parse(text, CultureInfo.InvariantCulture),
                _ => JsonSerializer.Deserialize(value, actualType),
            };
        }

        if (IsNumericType(actualType) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
        {
            int numericBool = value.GetBoolean() ? 1 : 0;
            return Convert.ChangeType(numericBool, actualType);
        }

        return JsonSerializer.Deserialize(value, actualType);
    }

    private static object? ConvertJsonObjectValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out long int64) => int64,
            JsonValueKind.Number when value.TryGetDecimal(out decimal decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.ToString(),
        };
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal);
    }
}

