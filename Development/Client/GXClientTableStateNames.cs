namespace Gurux.Data.Relay.Client;

internal static class GXClientTableStateNames
{
    public static string Get(string tableName, int databaseIndex, int databaseCount)
    {
        return databaseCount == 1 ? tableName : $"source-{databaseIndex + 1}:{tableName}";
    }
}

