using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Transport;

public sealed class GXTransportFactory : IGXTransportFactory
{
    private readonly IGXMessageSerializer _serializer;
    private readonly Log.IGXTransportMessageLogService _transportMessageLogService;

    public GXTransportFactory(
        IGXMessageSerializer serializer,
        Log.IGXTransportMessageLogService transportMessageLogService)
    {
        _serializer = serializer;
        _transportMessageLogService = transportMessageLogService;
    }

    public IDataTransport Create(GXTransportConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.Type switch
        {
            TransportType.Tcp => new GXTcpDataTransport(configuration, _serializer, _transportMessageLogService),
            TransportType.Mqtt => new GXMqttDataTransport(configuration, _serializer, _transportMessageLogService),
            _ => throw new NotSupportedException($"Transport type '{configuration.Type}' is not supported."),
        };
    }
}

