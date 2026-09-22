using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Log;

/// <summary>Identifies the relay mode within a concurrent web-host execution.</summary>
internal static class GXRelayModeContext
{
    public static readonly AsyncLocal<ApplicationMode?> Current = new();
}

