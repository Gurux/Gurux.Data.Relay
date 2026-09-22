using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Client;

internal static class GXClientTransferSettings
{
    public static GXTransferConfiguration GetEffective(GXClientConfiguration configuration)
    {
        configuration.EnsureRoutingIds();
        return GetEffective(configuration.Transports);
    }

    public static GXTransferConfiguration GetEffective(IEnumerable<GXTransportConfiguration> transports)
    {
        List<GXTransferConfiguration> settings = transports
            .Select(transport => transport.Transfer ?? new GXTransferConfiguration())
            .ToList();
        if (settings.Count == 0)
        {
            return new GXTransferConfiguration();
        }

        int pingAfterSeconds = settings
            .Where(item => item.PingAfterSeconds > 0)
            .Select(item => item.PingAfterSeconds)
            .DefaultIfEmpty(0)
            .Min();

        return new GXTransferConfiguration
        {
            BatchSize = settings.Min(item => Math.Max(1, item.BatchSize)),
            RetryCount = settings.Max(item => Math.Max(1, item.RetryCount)),
            RetryDelaySeconds = settings.Min(item => Math.Max(1, item.RetryDelaySeconds)),
            PingAfterSeconds = pingAfterSeconds,
            AllowConcurrentRuns = settings.All(item => item.AllowConcurrentRuns),
        };
    }
}



