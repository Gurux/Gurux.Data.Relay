using System.Net;
using System.Net.Sockets;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Log;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Transport;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Server;

public sealed class GXTcpServerListener : IGXTcpServerListener
{
    private readonly IGXConfigurationService? _configurationService;
    private readonly IGXMessageSerializer _serializer;
    private readonly IGXMessageValidator _validator;
    private readonly IGXIncomingMessageHandler _handler;
    private readonly IGXTransportMessageLogService _transportMessageLogService;
    private readonly ILogger<GXTcpServerListener> _logger;

    public GXTcpServerListener(
        IGXMessageSerializer serializer,
        IGXMessageValidator validator,
        IGXIncomingMessageHandler handler,
        IGXTransportMessageLogService transportMessageLogService,
        ILogger<GXTcpServerListener> logger,
        IGXConfigurationService? configurationService = null)
    {
        _configurationService = configurationService;
        _serializer = serializer;
        _validator = validator;
        _handler = handler;
        _transportMessageLogService = transportMessageLogService;
        _logger = logger;
    }

    public async Task RunAsync(GXTransportConfiguration configuration, CancellationToken cancellationToken)
    {
        GXServerTransportValidator.Validate(configuration);

        TcpListener listener = new(IPAddress.Any, configuration.Port);
        listener.Start();
        using CancellationTokenRegistration registration = cancellationToken.Register(static state => ((TcpListener)state!).Stop(), listener);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _ = HandleClientAsync(client, configuration, cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, GXTransportConfiguration configuration, CancellationToken cancellationToken)
    {
        using var _ = client;
        EndPoint? remoteEndPoint = client.Client.RemoteEndPoint;

        try
        {
            _logger.LogInformation("Client connected: {RemoteEndPoint}", remoteEndPoint);
            using NetworkStream stream = client.GetStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Waiting for TCP frame from {RemoteEndPoint}. MaximumMessageSize is {MaximumMessageSize} bytes.",
                    remoteEndPoint,
                    configuration.MaximumMessageSize);
                byte[] payload;
                try
                {
                    byte[]? frame = await GXTcpFrameCodec.TryReadFrameAsync(stream, configuration.MaximumMessageSize, cancellationToken);
                    if (frame is null)
                    {
                        _logger.LogInformation("Client disconnected: {RemoteEndPoint}", remoteEndPoint);
                        break;
                    }

                    payload = frame;
                    _logger.LogInformation(
                        "Read TCP frame from {RemoteEndPoint}. Payload length is {PayloadLength} bytes.",
                        remoteEndPoint,
                        payload.Length);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Unexpected end of TCP stream", StringComparison.Ordinal))
                {
                    _logger.LogWarning(ex, "TCP frame from {RemoteEndPoint} ended before it was fully received.", remoteEndPoint);
                    break;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Invalid TCP frame length", StringComparison.Ordinal))
                {
                    _logger.LogError(
                        ex,
                        "Rejected TCP frame from {RemoteEndPoint}. MaximumMessageSize is {MaximumMessageSize} bytes.",
                        remoteEndPoint,
                        configuration.MaximumMessageSize);
                    break;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "TCP read from {RemoteEndPoint} failed.", remoteEndPoint);
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogWarning(ex, "TCP socket read from {RemoteEndPoint} failed.", remoteEndPoint);
                    break;
                }

                GXDataAcknowledgement acknowledgement = await ProcessMessageAsync(payload, configuration, cancellationToken);
                byte[] acknowledgementPayload = _serializer.Serialize(acknowledgement);
                try
                {
                    await GXTcpFrameCodec.WriteFrameAsync(stream, acknowledgementPayload, cancellationToken);
                    await _transportMessageLogService.WriteAcknowledgementAsync(
                        TransportMessageDirection.Sent,
                        configuration,
                        acknowledgement,
                        Convert.ToString(remoteEndPoint),
                        cancellationToken);
                    _logger.LogInformation(
                        "Sent TCP acknowledgement to {RemoteEndPoint}. MessageId: {MessageId}. Status: {Status}. Payload length is {PayloadLength} bytes.",
                        remoteEndPoint,
                        acknowledgement.MessageId,
                        acknowledgement.Status,
                        acknowledgementPayload.Length);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "TCP acknowledgement write to {RemoteEndPoint} failed. MessageId: {MessageId}. Status: {Status}. Payload length is {PayloadLength} bytes.",
                        remoteEndPoint,
                        acknowledgement.MessageId,
                        acknowledgement.Status,
                        acknowledgementPayload.Length);
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "TCP socket acknowledgement write to {RemoteEndPoint} failed. MessageId: {MessageId}. Status: {Status}. Payload length is {PayloadLength} bytes.",
                        remoteEndPoint,
                        acknowledgement.MessageId,
                        acknowledgement.Status,
                        acknowledgementPayload.Length);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle TCP client {RemoteEndPoint}.", remoteEndPoint);
        }
    }

    private async Task<GXDataAcknowledgement> ProcessMessageAsync(
        byte[] payload,
        GXTransportConfiguration configuration,
        CancellationToken cancellationToken)
    {
        GXDataMessage? message = null;
        string name = configuration.Description ?? $"Database {configuration.DatabaseIndex}";
        try
        {
            message = _serializer.Deserialize<GXDataMessage>(payload);
            _validator.Validate(message, configuration.MaximumMessageSize);
            await _transportMessageLogService.WriteDataMessageAsync(
                TransportMessageDirection.Received,
                configuration,
                message,
                null,
                cancellationToken);
            _logger.LogInformation(
                "Processing TCP message {MessageId} for {name}. Table: {TableName}. Change count: {ChangeCount}.",
                message.MessageId,
                name,
                message.Table.Name,
                message.Changes.Count);
            GXServerMessageRoute route;
            if (_configurationService is not null)
            {
                return await GXServerMessageRouter.HandleAsync(_configurationService, configuration, message, _handler, cancellationToken);
            }
            else
            {
                if (configuration.Tables.Count != 0)
                    throw new InvalidOperationException("Server configuration service is required to resolve transport routes.");
                route = new(configuration.DatabaseIndex, null, null);
            }
            using IDisposable scope = GXServerDatabaseContext.Push(route);
            return await _handler.HandleAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "TCP message processing failed. MessageId: {MessageId}. Database: {name}.",
                message?.MessageId,
                name);
            return new GXDataAcknowledgement
            {
                MessageId = message?.MessageId ?? Guid.Empty,
                Status = AcknowledgementStatus.Error,
                Error = ex.Message,
            };
        }
    }
}

