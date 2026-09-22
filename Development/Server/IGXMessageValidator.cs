using Gurux.Data.Relay.Shared.Protocol;

namespace Gurux.Data.Relay.Server;

public interface IGXMessageValidator
{
    void Validate(GXDataMessage message, int maximumMessageSize);
}
