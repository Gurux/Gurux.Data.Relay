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
