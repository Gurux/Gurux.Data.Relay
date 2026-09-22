using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Sources;

public sealed record GXStoredDataSource(GXDataSourceConfiguration Configuration, string SettingsJson,
    IReadOnlyDictionary<string, string> Secrets)
{
    /// <summary>Changes to persist; loaded sources always leave this empty.</summary>
    public IReadOnlyList<GXDataSourceSecretChange> SecretChanges { get; init; } = [];
}
