using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm;

namespace Gurux.Data.Relay.Log;

internal static class GXLogParent
{
    public static async Task<Guid> ResolveAsync(
        GXDbConnection connection, 
        ApplicationMode mode, CancellationToken cancellationToken)
    {
        var settings = await connection.SelectAsync<GXSettings>(
            GXSelectArgs.SelectAll<GXSettings>(s => s.Mode == mode), cancellationToken);
        var existing = settings.SingleOrDefault();
        if (existing is not null) return existing.Id;
        var parent = new GXSettings
        {
            Id = Gurux.Data.Relay.Configuration.GXTransportRouting.StableId($"settings:{mode}"), Mode = mode
        };
        Gurux.Data.Relay.Configuration.GXEntityPersistence.Initialize(parent);
        try { await connection.InsertAsync(GXInsertArgs.Insert(parent), cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Another logger may have created this mode's parent concurrently.
            var created = await connection.SingleOrDefaultAsync<GXSettings>(GXSelectArgs.SelectAll<GXSettings>(s => s.Id == parent.Id), cancellationToken);
            if (created is null) throw;
        }
        return parent.Id;
    }
}


