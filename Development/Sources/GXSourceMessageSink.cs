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

using System.Collections.Concurrent;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Shared.Sources;
using Gurux.Data.Relay.Transport;

namespace Gurux.Data.Relay.Sources;

public sealed class GXSourceMessageSink(IGXConfigurationService configurations, IGXMessageDelivery delivery) : IGXSourceMessageSink
{
    private readonly ConcurrentDictionary<Guid, Guid[]> routes = new();
    public void SetRoutes(Guid id, IEnumerable<Guid> ids) => routes[id] = ids.Distinct().ToArray();
    public async ValueTask DeliverAsync(Guid sourceId, GXDataMessage message, CancellationToken token)
    {
        if (!routes.TryGetValue(sourceId, out var ids) || ids.Length == 0)
            throw new InvalidOperationException("Source must select at least one destination route.");
        var client = await configurations.LoadClientAsync(token)
            ?? throw new InvalidOperationException("Client transport configuration is missing.");
        var transports = client.Transports.Where(t => ids.Contains(t.Id)).ToArray();
        if (transports.Length != ids.Length) throw new InvalidOperationException("A source destination route no longer exists.");
        await delivery.DeliverAsync(transports, message, token);
    }
}
