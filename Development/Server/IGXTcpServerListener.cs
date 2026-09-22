using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Server;

public interface IGXTcpServerListener
{
    Task RunAsync(GXTransportConfiguration configuration, CancellationToken cancellationToken);
}
