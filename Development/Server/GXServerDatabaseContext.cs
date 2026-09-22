namespace Gurux.Data.Relay.Server;

public static class GXServerDatabaseContext
{
    private static readonly AsyncLocal<int?> CurrentIndex = new();
    private static readonly AsyncLocal<string?> CurrentDestination = new();
    private static readonly AsyncLocal<string?> CurrentRoute = new();

    public static string? DestinationTable => CurrentDestination.Value;
    public static string? RouteKey => CurrentRoute.Value;

    public static IDisposable Push(GXServerMessageRoute route)
    {
        Scope scope = new(DatabaseIndex, DestinationTable, RouteKey);
        DatabaseIndex = route.DatabaseIndex;
        CurrentDestination.Value = route.DestinationTable;
        CurrentRoute.Value = route.RouteKey;
        return scope;
    }

    private sealed class Scope(int? index, string? destination, string? route) : IDisposable
    {
        public void Dispose()
        {
            DatabaseIndex = index;
            CurrentDestination.Value = destination;
            CurrentRoute.Value = route;
        }
    }

    public static int? DatabaseIndex
    {
        get => CurrentIndex.Value;
        set => CurrentIndex.Value = value;
    }
}

