using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Enums;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXServerConfiguration : GXModeConfiguration,
    System.Text.Json.Serialization.IJsonOnDeserialized, System.Text.Json.Serialization.IJsonOnSerializing
{
    public List<GXDatabaseConfiguration> Databases { get; set; } = [];
    public List<GXTransportConfiguration> Transports { get; set; } = [];

    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized() => EnsureRoutingIds();
    void System.Text.Json.Serialization.IJsonOnSerializing.OnSerializing() => EnsureRoutingIds();

    public void EnsureRoutingIds()
    {
        GXTransportRouting.EnsureDatabaseIds(Databases);
        GXTransportRouting.EnsureTransportIds(Transports);
    }

    public GXServerConfiguration()
    {
        Mode = ApplicationMode.Server;
    }

    public IReadOnlyList<GXDatabaseConfiguration> GetTargetDatabases()
    {
        EnsureRoutingIds();
        if (Databases.Count == 0)
        {
            throw new InvalidOperationException("Server configuration must contain at least one destination database in Databases.");
        }

        return Databases;
    }
}



