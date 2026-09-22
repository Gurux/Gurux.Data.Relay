using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Log;

public static class GXEventLogContext
{
    /// <summary>
    /// Selects the relay mode for logging in the current asynchronous operation.
    /// </summary>
    /// <param name="mode">The mode whose logging settings and event stream are used.</param>
    /// <returns>A scope that restores the previous mode when disposed.</returns>
    public static IDisposable BeginMode(ApplicationMode mode)
    {
        var previous = GXRelayModeContext.Current.Value;
        GXRelayModeContext.Current.Value = mode;
        return new Scope(() => GXRelayModeContext.Current.Value = previous);
    }

    private static readonly AsyncLocal<int?> Current = new();
    public static int? DatabaseIndex => Current.Value;
    public static IDisposable BeginDatabase(int? index)
    {
        int? previous = Current.Value;
        Current.Value = index;
        return new Scope(() => Current.Value = previous);
    }
    private sealed class Scope(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}

