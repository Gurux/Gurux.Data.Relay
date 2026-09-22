using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Enums;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXClientConfiguration : GXModeConfiguration,
    System.Text.Json.Serialization.IJsonOnDeserialized, System.Text.Json.Serialization.IJsonOnSerializing
{
    public List<GXDatabaseConfiguration> Databases { get; set; } = [];
    public List<GXTransportConfiguration> Transports { get; set; } = [];

    public GXClientConfiguration()
    {
        Mode = ApplicationMode.Client;
    }

    public IReadOnlyList<GXDatabaseConfiguration> GetSourceDatabases()
    {
        if (Databases.Count == 0)
        {
            throw new InvalidOperationException("Client configuration must contain at least one source database in Databases.");
        }

        EnsureRoutingIds();
        return Databases;
    }

    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized() => EnsureRoutingIds();
    void System.Text.Json.Serialization.IJsonOnSerializing.OnSerializing() => EnsureRoutingIds();

    public void EnsureRoutingIds()
    {
        GXTransportRouting.EnsureDatabaseIds(Databases);
        GXTransportRouting.EnsureTransportIds(Transports);
    }

    public IReadOnlyList<GXTransportConfiguration> GetTransports(int databaseIndex, string tableName)
    {
        EnsureRoutingIds();
        Guid databaseId = Databases[databaseIndex].Id;
        return Transports.Where(t => t.Tables.Any(route => route.DatabaseId == databaseId &&
            string.Equals(route.Table, tableName, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}



