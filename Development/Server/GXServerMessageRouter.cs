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

using System.Runtime.CompilerServices;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Protocol;

namespace Gurux.Data.Relay.Server;

internal static class GXServerMessageRouter
{
    // TCP and MQTT must not overwrite each other's newly discovered mappings.
    private static readonly ConditionalWeakTable<IGXConfigurationService, SemaphoreSlim> SchemaGates = new();

    public static async Task<GXDataAcknowledgement> HandleAsync(IGXConfigurationService configurations,
        GXTransportConfiguration transport, GXDataMessage message, IGXIncomingMessageHandler handler,
        CancellationToken cancellationToken)
    {
        bool schema = message.MessageType == MessageType.Schema;
        SemaphoreSlim? gate = schema ? SchemaGates.GetValue(configurations, _ => new(1, 1)) : null;
        if (gate is not null) await gate.WaitAsync(cancellationToken);
        try
        {
            GXServerConfiguration server = await configurations.LoadServerAsync(cancellationToken)
                ?? throw new InvalidOperationException("Server configuration was not found.");
            if (message.MessageType == MessageType.Ping && string.IsNullOrWhiteSpace(message.Table.Name))
            {
                if (transport.Id != Guid.Empty && !server.Transports.Any(current => current.Id == transport.Id))
                    throw new InvalidOperationException("This transport has been removed from the server configuration.");
                using IDisposable pingScope = GXServerDatabaseContext.Push(new GXServerMessageRoute(null, null, null));
                return await handler.HandleAsync(message, cancellationToken);
            }
            GXServerMessageRoute route = GXServerMessageRoute.Resolve(server, transport, message.Table.Name, allowNewTable: schema);
            GXTransportConfiguration current = transport.Id == Guid.Empty ? transport
                : server.Transports.Single(t => t.Id == transport.Id);
            bool addMapping = schema && !current.Tables.Any(table => string.Equals(
                string.IsNullOrWhiteSpace(table.Source) ? table.Table : table.Source,
                message.Table.Name, StringComparison.OrdinalIgnoreCase));
            if (addMapping && !server.Transports.Contains(current))
                throw new InvalidOperationException("Save the server transport before receiving a new table schema.");

            using IDisposable scope = GXServerDatabaseContext.Push(route);
            GXDataAcknowledgement acknowledgement = await handler.HandleAsync(message, cancellationToken);
            if (addMapping && acknowledgement.Status == AcknowledgementStatus.Ok)
            {
                // Only publish the route once the destination schema has been created successfully.
                current.Tables.Add(new()
                {
                    DatabaseId = server.Databases[route.DatabaseIndex!.Value].Id,
                    Source = message.Table.Name,
                    Table = route.DestinationTable!
                });
                await configurations.SaveServerAsync(server, cancellationToken);
            }
            return acknowledgement;
        }
        finally { gate?.Release(); }
    }
}

