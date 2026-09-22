using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Client;

public interface IClientBatchSender
{
    Task<int> SendAsync(GXClientConfiguration configuration, CancellationToken cancellationToken);

    Task<int> SendTableAsync(GXClientConfiguration configuration, int databaseIndex, GXTableConfiguration tableConfiguration, CancellationToken cancellationToken);
}

