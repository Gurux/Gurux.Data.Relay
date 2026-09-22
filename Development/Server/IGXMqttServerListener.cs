using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Server;

public interface IGXMqttServerListener
{
    Task RunAsync(GXTransportConfiguration configuration, CancellationToken cancellationToken);
}
