using Gurux.Service.Orm.Common.Enums;
using Gurux.Service.Orm.Enums;

namespace Gurux.Data.Relay.Database;

internal static class GXSqlParameterHelper
{
    public static string GetPlaceholder(DatabaseType databaseType, string name)
    {
        return $"{GetPrefix(databaseType)}{name}";
    }

    public static string GetParameterName(string placeholder)
    {
        return placeholder.StartsWith(':') ? placeholder[1..] : placeholder;
    }

    public static object? NormalizeParameterValue(DatabaseType databaseType, object? value)
    {
        if (databaseType == DatabaseType.Oracle && value is bool boolean)
        {
            return boolean ? 1 : 0;
        }
        if (databaseType == DatabaseType.PostgreSQL && value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToUniversalTime();
        }
        if (databaseType == DatabaseType.MSSQL && value is sbyte signedByte)
        {
            return checked((byte)signedByte);
        }

        return value;
    }

    private static string GetPrefix(DatabaseType databaseType)
    {
        return databaseType is DatabaseType.Oracle or DatabaseType.SapHana ? ":" : "@";
    }
}

