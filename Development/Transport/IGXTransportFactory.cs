using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Transport;

public interface IGXTransportFactory
{
    IDataTransport Create(GXTransportConfiguration configuration);
}
