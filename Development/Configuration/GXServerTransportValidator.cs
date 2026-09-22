using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Enums;

namespace Gurux.Data.Relay.Configuration;

/// <summary>Checks server transport values without starting listeners or changing settings.</summary>
internal static class GXServerTransportValidator
{
    public static void Validate(GXTransportConfiguration transport)
    {
        if (transport.Type is not (TransportType.Tcp or TransportType.Mqtt))
            throw new InvalidOperationException($"Transport type '{transport.Type}' is not supported.");
        if (transport.Port is < 1 or > 65535)
            throw new InvalidOperationException($"{transport.Type} port must be between 1 and 65535.");
        if (transport.MaximumMessageSize <= 0)
            throw new InvalidOperationException("Maximum message size must be greater than zero.");
        if (transport.Type == TransportType.Mqtt)
        {
            if (string.IsNullOrWhiteSpace(transport.Broker))
                throw new InvalidOperationException("MQTT broker is not configured.");
            if (string.IsNullOrWhiteSpace(transport.Topic))
                throw new InvalidOperationException("MQTT topic is not configured.");
            if (string.IsNullOrWhiteSpace(transport.AcknowledgementTopic))
                throw new InvalidOperationException("MQTT acknowledgement topic is not configured.");
        }
    }
}
