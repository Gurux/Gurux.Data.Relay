namespace Gurux.Data.Relay.Server;

internal static class GXDeleteMarkerHelper
{
    public static object GetSoftDeleteMarkerValue(Type? targetType)
    {
        Type actualType = Nullable.GetUnderlyingType(targetType ?? typeof(DateTimeOffset)) ?? targetType ?? typeof(DateTimeOffset);
        if (actualType == typeof(DateTimeOffset))
        {
            return DateTimeOffset.UtcNow;
        }
        if (actualType == typeof(DateTime))
        {
            return DateTime.UtcNow;
        }
        if (actualType == typeof(DateOnly))
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
        if (actualType == typeof(string))
        {
            return DateTimeOffset.UtcNow.ToString("O");
        }
        if (actualType == typeof(bool))
        {
            return true;
        }

        throw new InvalidOperationException($"Soft delete column type '{actualType.Name}' is not supported.");
    }
}
