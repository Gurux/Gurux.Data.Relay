using Gurux.Data.Relay.Enums;

namespace Gurux.Data.Relay.Server;

internal static class GXTypeCompatibilityHelper
{
    public static Type MapLogicalType(DataType dataType)
    {
        return dataType switch
        {
            DataType.Boolean => typeof(bool),
            DataType.SByte => typeof(sbyte),
            DataType.Byte => typeof(byte),
            DataType.Int16 => typeof(short),
            DataType.UInt16 => typeof(ushort),
            DataType.Int32 => typeof(int),
            DataType.UInt32 => typeof(uint),
            DataType.Int64 => typeof(long),
            DataType.UInt64 => typeof(ulong),
            DataType.Single => typeof(float),
            DataType.Double => typeof(double),
            DataType.Decimal => typeof(decimal),
            DataType.String => typeof(string),
            DataType.Guid => typeof(Guid),
            DataType.ByteArray => typeof(byte[]),
            DataType.DateTime => typeof(DateTime),
            DataType.DateTimeOffset => typeof(DateTimeOffset),
            DataType.DateOnly => typeof(DateOnly),
            DataType.TimeOnly => typeof(TimeOnly),
            _ => throw new NotSupportedException($"Logical data type '{dataType}' is not supported."),
        };
    }

    public static bool IsCompatible(Type? destinationType, Type logicalSourceType)
    {
        if (destinationType is null)
        {
            return false;
        }

        Type actualDestinationType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
        Type actualSourceType = Nullable.GetUnderlyingType(logicalSourceType) ?? logicalSourceType;
        if (actualDestinationType == actualSourceType)
        {
            return true;
        }

        if (actualDestinationType == typeof(object))
        {
            return true;
        }

        if (actualDestinationType == typeof(string))
        {
            return true;
        }

        if (IsNumericType(actualDestinationType) && IsNumericType(actualSourceType))
        {
            return true;
        }

        if (actualDestinationType == typeof(decimal) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(Guid) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(byte[]) && actualSourceType == typeof(Guid))
        {
            return true;
        }

        if (actualDestinationType == typeof(Guid) && actualSourceType == typeof(byte[]))
        {
            return true;
        }

        if (actualDestinationType == typeof(DateTimeOffset) && actualSourceType == typeof(DateTime))
        {
            return true;
        }

        if (actualDestinationType == typeof(DateTime) && actualSourceType == typeof(DateTimeOffset))
        {
            return true;
        }

        if (actualDestinationType == typeof(DateTime) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(DateTimeOffset) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(DateOnly) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(TimeOnly) && actualSourceType == typeof(string))
        {
            return true;
        }

        if (actualDestinationType == typeof(bool) && IsNumericType(actualSourceType))
        {
            return true;
        }

        if (IsNumericType(actualDestinationType) && actualSourceType == typeof(bool))
        {
            return true;
        }

        return false;
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

