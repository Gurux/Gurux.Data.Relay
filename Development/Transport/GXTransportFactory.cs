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

