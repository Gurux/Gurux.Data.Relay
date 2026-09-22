using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Client;

public interface IClientSchemaSender
{
    Task<int> SendAsync(GXClientConfiguration configuration, CancellationToken cancellationToken);
}

