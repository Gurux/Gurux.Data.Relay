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

using System.Data;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Common;

namespace Gurux.Data.Relay.Configuration;

internal sealed class GXRelationalRuntimeStateStore(GXDbConnection connection, IDbTransaction transaction, CancellationToken cancellationToken)
{
    public async Task<object> LoadAsync(ApplicationMode mode)
    {
        if (mode == ApplicationMode.Client)
            return new GXClientState
            {
                Tables = (await connection.SelectAsync<GXClientTableState>(transaction,
                Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXClientTableState>(connection), cancellationToken)).OrderBy(r => r.Position).ToList()
            };
        if (mode == ApplicationMode.Server)
            return new GXServerState
            {
                TableMappings = (await connection.SelectAsync<GXTableMapping>(transaction, Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXTableMapping>(connection), cancellationToken)).OrderBy(r => r.Source, StringComparer.OrdinalIgnoreCase).ToList(),
                ProcessedMessages = (await connection.SelectAsync<GXProcessedMessageState>(transaction, Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXProcessedMessageState>(connection), cancellationToken)).OrderBy(r => r.Position).ToList()
            };
        throw new NotSupportedException($"Unsupported state mode: {mode}.");
    }

    public async Task SaveAsync(ApplicationMode mode, object state)
    {
        var existing = await connection.SingleOrDefaultAsync<RelayRuntimeState>(transaction,
            Gurux.Data.Relay.Database.GXMetadataQueries.Select<RelayRuntimeState>(connection, ("Id", mode)), cancellationToken);
        var record = new RelayRuntimeState { Id = mode, Version = 1 };
        GXEntityPersistence.CopyMetadata((IGXEntityMetadata)state, record);
        await GXEntityPersistence.SaveAsync(connection, transaction, record, existing, cancellationToken);
        GXEntityPersistence.CopyMetadata(record, (IGXEntityMetadata)state);
        switch (state)
        {
            case GXClientState client when mode == ApplicationMode.Client:
                for (int i = 0; i < client.Tables.Count; ++i)
                {
                    client.Tables[i].RuntimeState = mode; client.Tables[i].Position = i;
                    if (client.Tables[i].Id == Guid.Empty) client.Tables[i].Id = GXTransportRouting.StableId($"client-state:{client.Tables[i].DatabaseIndex}:{client.Tables[i].Name}");
                }
                await ReplaceAsync(client.Tables);
                break;
            case GXServerState server when mode == ApplicationMode.Server:
                for (int i = 0; i < server.TableMappings.Count; ++i)
                {
                    server.TableMappings[i].RuntimeState = mode;
                    if (server.TableMappings[i].Id == Guid.Empty) server.TableMappings[i].Id = GXTransportRouting.StableId($"server-mapping:{server.TableMappings[i].Source}");
                }
                for (int i = 0; i < server.ProcessedMessages.Count; ++i)
                {
                    server.ProcessedMessages[i].RuntimeState = mode; server.ProcessedMessages[i].Position = i;
                    if (server.ProcessedMessages[i].Id == Guid.Empty) server.ProcessedMessages[i].Id = GXTransportRouting.StableId($"server-message:{server.ProcessedMessages[i].RouteKey}:{server.ProcessedMessages[i].MessageId}");
                }
                await ReplaceAsync(server.TableMappings);
                await ReplaceAsync(server.ProcessedMessages);
                break;
            default: throw new InvalidOperationException("Runtime state type does not match its mode.");
        }

    }

    private async Task ReplaceAsync<T>(List<T> rows) where T : class, IUnique<Guid>, IGXEntityMetadata
    {
        var existing = (await connection.SelectAllAsync<T>(transaction, cancellationToken)).ToDictionary(row => row.Id);
        foreach (var row in rows)
            await GXEntityPersistence.SaveAsync(connection, transaction, row, existing.GetValueOrDefault(row.Id), cancellationToken);
        var retained = rows.Select(row => row.Id).ToHashSet();
        foreach (var id in existing.Keys.Where(id => !retained.Contains(id)))
            await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<T>(row => row.Id == id), cancellationToken);
    }
}

