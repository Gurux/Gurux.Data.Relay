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

using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Gurux.Data.Relay.Transport;

/// <summary>Protocol-neutral delivery with acknowledgement validation.</summary>
public sealed class GXMessageDelivery(IGXTransportFactory factory, ILogger? logger = null) : IGXMessageDelivery
{
    public async Task<GXDataAcknowledgement> DeliverAsync(IReadOnlyList<GXTransportConfiguration> transports,
        GXDataMessage message, CancellationToken cancellationToken)
    {
        GXDataAcknowledgement? last = null;
        for (int index = 0; index < transports.Count; ++index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configuration = transports[index];
            string name = string.IsNullOrWhiteSpace(configuration.Description)
                ? $"{index + 1}/{transports.Count}" : configuration.Description;
            logger?.LogInformation("Sending {MessageType} message {MessageId} to server {ServerName} using {TransportType}.",
                message.MessageType, message.MessageId, name, configuration.Type);
            await using var transport = factory.Create(configuration);
            await transport.ConnectAsync(cancellationToken);
            await transport.SendAsync(message, cancellationToken);
            last = await transport.ReceiveAcknowledgementAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (last.MessageId != message.MessageId)
                throw new InvalidOperationException("The acknowledgement does not match the sent message.");
            if (last.Status != AcknowledgementStatus.Ok)
                throw new InvalidOperationException($"Server {name} rejected message {last.MessageId}: {last.Error}");
        }
        return last ?? throw new InvalidOperationException("At least one transport must be configured.");
    }
}
