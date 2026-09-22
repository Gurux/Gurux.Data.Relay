using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Enums;
using System.Security.Cryptography;
using System.Text;

namespace Gurux.Data.Relay.Configuration;

public static class GXTransportRouting
{
    internal static Guid StableId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    internal static void EnsureDatabaseIds(IReadOnlyList<GXDatabaseConfiguration> databases)
    {
        for (int i = 0; i < databases.Count; ++i)
            if (databases[i].Id == Guid.Empty)
                databases[i].Id = Guid.NewGuid();
    }

    internal static void EnsureTransportIds(IReadOnlyList<GXTransportConfiguration> transports)
    {
        for (int i = 0; i < transports.Count; ++i)
            if (transports[i].Id == Guid.Empty)
                transports[i].Id = Guid.NewGuid();
    }

    public static void Validate(IReadOnlyList<GXDatabaseConfiguration> databases,
        IReadOnlyList<GXTransportConfiguration> transports, bool server, IReadOnlySet<Guid>? providerRoutes = null)
    {
        if (databases.Select(db => db.Id).Distinct().Count() != databases.Count)
            throw new InvalidOperationException("Database IDs must be unique.");
        if (transports.Select(t => t.Id).Distinct().Count() != transports.Count)
            throw new InvalidOperationException("Transport IDs must be unique.");
        foreach (GXTransportConfiguration transport in transports)
        {
            if (!server && transport.Tables.Count == 0 && providerRoutes?.Contains(transport.Id) != true)
                throw new InvalidOperationException("Select at least one table for each transport.");
            HashSet<string> sources = new(StringComparer.OrdinalIgnoreCase);
            HashSet<(Guid, string)> tables = [];
            foreach (GXTransportTableConfiguration route in transport.Tables)
            {
                GXDatabaseConfiguration database = databases.SingleOrDefault(db => db.Id == route.DatabaseId)
                    ?? throw new InvalidOperationException("A transport references a removed database.");
                if (string.IsNullOrWhiteSpace(route.Table))
                    throw new InvalidOperationException("Select a table for each transport mapping.");
                if (!tables.Add((route.DatabaseId, route.Table.ToUpperInvariant())))
                    throw new InvalidOperationException("A table can only be selected once per transport.");
                if (server)
                {
                    if (string.IsNullOrWhiteSpace(route.Source) || !sources.Add(route.Source))
                        throw new InvalidOperationException("Each server transport mapping must have a unique source table name.");
                }
                else if (database.Tables?.Any(t => string.Equals(t.Name, route.Table, StringComparison.OrdinalIgnoreCase)) != true)
                {
                    throw new InvalidOperationException($"Source table '{route.Table}' is not configured in the selected database.");
                }
            }
        }
        if (server)
        {
            var duplicatePort = transports.Where(t => t.Type == TransportType.Tcp)
                .GroupBy(t => t.Port).FirstOrDefault(g => g.Count() > 1);
            if (duplicatePort != null)
                throw new InvalidOperationException($"TCP port {duplicatePort.Key} is configured for more than one transport. Combine its table mappings into one transport.");
            var duplicateSubscription = transports.Where(t => t.Type == TransportType.Mqtt)
                .GroupBy(t => (Broker: t.Broker?.Trim().ToUpperInvariant(), t.Port, t.Topic))
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateSubscription != null)
                throw new InvalidOperationException("An MQTT subscription is configured for more than one transport. Combine its table mappings into one transport.");
        }
    }
}


