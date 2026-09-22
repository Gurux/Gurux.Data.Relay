using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Client;

public interface ITransferScheduler
{
    Task<int> RunAsync(GXClientConfiguration configuration, CancellationToken cancellationToken);
}
