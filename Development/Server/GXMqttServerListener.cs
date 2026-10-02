//
// --------------------------------------------------------------------------
//  Gurux Ltd
// 
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License 
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of 
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2. 
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using System.Buffers;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Log;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Transport;
using MQTTnet;
using MQTTnet.Protocol;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Server;

public sealed class GXMqttServerListener : IGXMqttServerListener
{
    private readonly IGXConfigurationService? _configurationService;
    private readonly IGXMessageSerializer _serializer;
    private readonly IGXMessageValidator _validator;
    private readonly IGXIncomingMessageHandler _handler;
    private readonly IGXTransportMessageLogService _transportMessageLogService;
    private readonly ILogger<GXMqttServerListener> _logger;

    public GXMqttServerListener(
        IGXMessageSerializer serializer,
        IGXMessageValidator validator,
        IGXIncomingMessageHandler handler,
        IGXTransportMessageLogService transportMessageLogService,
        ILogger<GXMqttServerListener> logger,
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
        MqttClientFactory factory = new();
        using IMqttClient client = factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += async eventArgs =>
        {
            if (eventArgs.ApplicationMessage.Topic != configuration.Topic)
            {
                return;
            }

            GXDataAcknowledgement acknowledgement = await ProcessMessageAsync(eventArgs.ApplicationMessage.Payload.ToArray(), configuration, cancellationToken);
            byte[] acknowledgementPayload = _serializer.Serialize(acknowledgement);
            MqttApplicationMessage ack = new MqttApplicationMessageBuilder()
                .WithTopic(configuration.AcknowledgementTopic)
                .WithPayload(acknowledgementPayload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            await client.PublishAsync(ack, cancellationToken);
            await _transportMessageLogService.WriteAcknowledgementAsync(
                TransportMessageDirection.Sent,
                configuration,
                acknowledgement,
                configuration.AcknowledgementTopic,
                cancellationToken);
        };

        await client.ConnectAsync(BuildOptions(configuration), cancellationToken);
        await client.SubscribeAsync(configuration.Topic!, MqttQualityOfServiceLevel.AtLeastOnce, cancellationToken);
        _logger.LogInformation("Subscribed to MQTT topic {Topic}.", configuration.Topic);

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync();
            }
        }
    }

    private async Task<GXDataAcknowledgement> ProcessMessageAsync(byte[] payload, GXTransportConfiguration configuration, CancellationToken cancellationToken)
    {
        GXDataMessage? message = null;
        try
        {
            message = _serializer.Deserialize<GXDataMessage>(payload);
            _validator.Validate(message, configuration.MaximumMessageSize);
            await _transportMessageLogService.WriteDataMessageAsync(
                TransportMessageDirection.Received,
                configuration,
                message,
                configuration.Topic,
                cancellationToken);
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
            return new GXDataAcknowledgement
            {
                MessageId = message?.MessageId ?? Guid.Empty,
                Status = AcknowledgementStatus.Error,
                Error = ex.Message,
            };
        }
    }

    private static MqttClientOptions BuildOptions(GXTransportConfiguration configuration)
    {
        MqttClientOptionsBuilder builder = new MqttClientOptionsBuilder()
            .WithTcpServer(configuration.Broker!, configuration.Port);

        if (!string.IsNullOrWhiteSpace(configuration.Username))
        {
            builder.WithCredentials(configuration.Username, configuration.Password);
        }

        if (configuration.UseTls)
        {
            builder.WithTlsOptions(options => options.UseTls());
        }

        return builder.Build();
    }
}

