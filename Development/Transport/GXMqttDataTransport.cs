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
using System.Threading.Channels;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Log;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Enums;
using MQTTnet;
using MQTTnet.Protocol;

namespace Gurux.Data.Relay.Transport;

public sealed class GXMqttDataTransport : IDataTransport
{
    private readonly GXTransportConfiguration _configuration;
    private readonly IGXMessageSerializer _serializer;
    private readonly IGXTransportMessageLogService _transportMessageLogService;
    private readonly Channel<GXDataAcknowledgement> _ackChannel = Channel.CreateUnbounded<GXDataAcknowledgement>();

    private IMqttClient? _client;

    public GXMqttDataTransport(
        GXTransportConfiguration configuration,
        IGXMessageSerializer serializer,
        IGXTransportMessageLogService transportMessageLogService)
    {
        _configuration = configuration;
        _serializer = serializer;
        _transportMessageLogService = transportMessageLogService;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client?.IsConnected == true)
        {
            return;
        }

        ValidateConfiguration();
        MqttClientFactory factory = new();
        _client = factory.CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += eventArgs =>
        {
            if (eventArgs.ApplicationMessage.Topic != _configuration.AcknowledgementTopic)
            {
                return Task.CompletedTask;
            }

            byte[] payload = eventArgs.ApplicationMessage.Payload.ToArray();
            GXDataAcknowledgement acknowledgement = _serializer.Deserialize<GXDataAcknowledgement>(payload);
            _ackChannel.Writer.TryWrite(acknowledgement);
            return _transportMessageLogService.WriteAcknowledgementAsync(
                TransportMessageDirection.Received,
                _configuration,
                acknowledgement,
                eventArgs.ApplicationMessage.Topic,
                CancellationToken.None);
        };

        await _client.ConnectAsync(BuildOptions(), cancellationToken);
        await _client.SubscribeAsync(_configuration.AcknowledgementTopic!, MqttQualityOfServiceLevel.AtLeastOnce, cancellationToken);
    }

    public async Task SendAsync(GXDataMessage message, CancellationToken cancellationToken)
    {
        IMqttClient client = _client ?? throw new InvalidOperationException("MQTT transport is not connected.");
        byte[] payload = _serializer.Serialize(message);
        MqttApplicationMessage mqttMessage = new MqttApplicationMessageBuilder()
            .WithTopic(_configuration.Topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await client.PublishAsync(mqttMessage, cancellationToken);
        await _transportMessageLogService.WriteDataMessageAsync(
            TransportMessageDirection.Sent,
            _configuration,
            message,
            _configuration.Topic,
            cancellationToken);
    }

    public async Task<GXDataAcknowledgement> ReceiveAcknowledgementAsync(CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _configuration.AcknowledgementTimeoutSeconds)));
            return await _ackChannel.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"MQTT acknowledgement on topic '{_configuration.AcknowledgementTopic}' timed out after {_configuration.AcknowledgementTimeoutSeconds} seconds.",
                new TimeoutException());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync();
            }

            _client.Dispose();
        }

        _ackChannel.Writer.TryComplete();
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_configuration.Broker))
        {
            throw new InvalidOperationException("MQTT broker is not configured.");
        }
        if (_configuration.Port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("MQTT port is not configured.");
        }
        if (string.IsNullOrWhiteSpace(_configuration.Topic))
        {
            throw new InvalidOperationException("MQTT topic is not configured.");
        }
        if (string.IsNullOrWhiteSpace(_configuration.AcknowledgementTopic))
        {
            throw new InvalidOperationException("MQTT acknowledgement topic is not configured.");
        }
    }

    private MqttClientOptions BuildOptions()
    {
        MqttClientOptionsBuilder builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_configuration.Broker!, _configuration.Port);

        if (!string.IsNullOrWhiteSpace(_configuration.Username))
        {
            builder.WithCredentials(_configuration.Username, _configuration.Password);
        }

        if (_configuration.UseTls)
        {
            builder.WithTlsOptions(options => options.UseTls());
        }

        return builder.Build();
    }
}

