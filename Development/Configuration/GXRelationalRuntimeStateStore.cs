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
                GXSelectArgs.SelectAll<GXClientTableState>(), cancellationToken)).OrderBy(r => r.Position).ToList()
            };
        if (mode == ApplicationMode.Server)
            return new GXServerState
            {
                TableMappings = (await connection.SelectAsync<GXTableMapping>(transaction, GXSelectArgs.SelectAll<GXTableMapping>(), cancellationToken)).OrderBy(r => r.Source, StringComparer.OrdinalIgnoreCase).ToList(),
                ProcessedMessages = (await connection.SelectAsync<GXProcessedMessageState>(transaction, GXSelectArgs.SelectAll<GXProcessedMessageState>(), cancellationToken)).OrderBy(r => r.Position).ToList()
            };
        throw new NotSupportedException($"Unsupported state mode: {mode}.");
    }

    public async Task SaveAsync(ApplicationMode mode, object state)
    {
        var existing = await connection.SingleOrDefaultAsync<RelayRuntimeState>(transaction,
            GXSelectArgs.SelectAll<RelayRuntimeState>(r => r.Id == mode), cancellationToken);
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

