using System.Net.Sockets;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Log;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Enums;

namespace Gurux.Data.Relay.Transport;

public sealed class GXTcpDataTransport : IDataTransport
{
    private readonly GXTransportConfiguration _configuration;
    private readonly IGXMessageSerializer _serializer;
    private readonly IGXTransportMessageLogService _transportMessageLogService;
    private readonly int _maximumMessageSize;

    private TcpClient? _client;
    private NetworkStream? _stream;

    public GXTcpDataTransport(
        GXTransportConfiguration configuration,
        IGXMessageSerializer serializer,
        IGXTransportMessageLogService transportMessageLogService,
        int? maximumMessageSize = null)
    {
        _configuration = configuration;
        _serializer = serializer;
        _transportMessageLogService = transportMessageLogService;
        _maximumMessageSize = maximumMessageSize ?? configuration.MaximumMessageSize;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_stream is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_configuration.Host))
        {
            throw new InvalidOperationException("TCP host is not configured.");
        }

        if (_configuration.Port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("TCP port is not configured.");
        }

        _client = new TcpClient();
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _configuration.ConnectTimeoutSeconds)));
            await _client.ConnectAsync(_configuration.Host, _configuration.Port, timeout.Token);
            _stream = _client.GetStream();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DisposeClient();
            throw new InvalidOperationException(
                $"TCP connection to {_configuration.Host}:{_configuration.Port} timed out after {_configuration.ConnectTimeoutSeconds} seconds.",
                new TimeoutException());
        }
        catch (SocketException ex)
        {
            DisposeClient();
            throw new InvalidOperationException($"TCP connection to {_configuration.Host}:{_configuration.Port} failed: {ex.Message}", ex);
        }
    }

    public async Task SendAsync(GXDataMessage message, CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream ?? throw new InvalidOperationException("TCP transport is not connected.");
        byte[] payload = _serializer.Serialize(message);
        if (payload.Length > _maximumMessageSize)
        {
            throw new InvalidOperationException($"Payload length '{payload.Length}' exceeds the maximum TCP message size.");
        }

        try
        {
            await GXTcpFrameCodec.WriteFrameAsync(stream, payload, cancellationToken);
            await _transportMessageLogService.WriteDataMessageAsync(
                TransportMessageDirection.Sent,
                _configuration,
                message,
                $"{_configuration.Host}:{_configuration.Port}",
                cancellationToken);
        }
        catch (IOException ex)
        {
            DisposeClient();
            throw new InvalidOperationException(
                $"TCP send to {_configuration.Host}:{_configuration.Port} failed while writing {payload.Length} bytes. " +
                $"The remote endpoint closed the connection. Client MaximumMessageSize is {_maximumMessageSize} bytes. " +
                $"Check that the server is running, the port belongs to the matching destination database, and both TCP transports allow a MaximumMessageSize of at least {payload.Length} bytes.",
                ex);
        }
        catch (SocketException ex)
        {
            DisposeClient();
            throw new InvalidOperationException(
                $"TCP send to {_configuration.Host}:{_configuration.Port} failed while writing {payload.Length} bytes. " +
                $"The remote endpoint closed the connection. Client MaximumMessageSize is {_maximumMessageSize} bytes. " +
                $"Check that the server is running, the port belongs to the matching destination database, and both TCP transports allow a MaximumMessageSize of at least {payload.Length} bytes.",
                ex);
        }
    }

    public async Task<GXDataAcknowledgement> ReceiveAcknowledgementAsync(CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream ?? throw new InvalidOperationException("TCP transport is not connected.");
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _configuration.AcknowledgementTimeoutSeconds)));
            byte[] payload = await GXTcpFrameCodec.ReadFrameAsync(stream, _maximumMessageSize, timeout.Token);
            GXDataAcknowledgement acknowledgement = _serializer.Deserialize<GXDataAcknowledgement>(payload);
            await _transportMessageLogService.WriteAcknowledgementAsync(
                TransportMessageDirection.Received,
                _configuration,
                acknowledgement,
                $"{_configuration.Host}:{_configuration.Port}",
                cancellationToken);
            return acknowledgement;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DisposeClient();
            throw new InvalidOperationException(
                $"TCP acknowledgement from {_configuration.Host}:{_configuration.Port} timed out after {_configuration.AcknowledgementTimeoutSeconds} seconds.",
                new TimeoutException());
        }
        catch (IOException ex)
        {
            DisposeClient();
            throw new InvalidOperationException($"TCP acknowledgement from {_configuration.Host}:{_configuration.Port} failed: {ex.Message}", ex);
        }
        catch (SocketException ex)
        {
            DisposeClient();
            throw new InvalidOperationException($"TCP acknowledgement from {_configuration.Host}:{_configuration.Port} failed: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            DisposeClient();
            throw new InvalidOperationException($"TCP acknowledgement from {_configuration.Host}:{_configuration.Port} failed: {ex.Message}", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeClient();
        return ValueTask.CompletedTask;
    }

    private void DisposeClient()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }
}

