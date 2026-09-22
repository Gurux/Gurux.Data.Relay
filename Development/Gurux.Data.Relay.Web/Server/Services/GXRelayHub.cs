using Gurux.Data.Relay.Realtime;
using Gurux.Data.Relay.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Gurux.Data.Relay.Web.Server.Services;

public sealed class GXEditRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<string>> _editors = [];

    public GXEditPresence[] Set(string connection, string[] resources)
    {
        lock (_gate)
        {
            if (resources.Length == 0) _editors.Remove(connection);
            else _editors[connection] = resources.ToHashSet(StringComparer.Ordinal);
            return Snapshot();
        }
    }

    public GXEditPresence[] Snapshot()
    {
        lock (_gate)
            return _editors.SelectMany(e => e.Value.Select(r => (Resource: r, Connection: e.Key)))
                .GroupBy(e => e.Resource).Select(g => new GXEditPresence(g.Key, g.Select(e => e.Connection).ToArray())).ToArray();
    }
}

public sealed class GXRelayHub(GXEditRegistry editors) : Hub
{
    public const string Path = "/hubs/relay";

    public Task SetEditing(string[] resources)
    {
        if (resources.Length > 4 || resources.Any(r => r is not ("client" or "server" or "datavault" or "store")))
            throw new HubException("Invalid edit resource.");
        return Clients.All.SendAsync("Editors", editors.Set(Context.ConnectionId, resources));
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Editors", editors.Snapshot());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.All.SendAsync("Editors", editors.Set(Context.ConnectionId, []));
        await base.OnDisconnectedAsync(exception);
    }
}

public sealed class GXRelayNotificationService(GXRelayChangeFeed feed, IHubContext<GXRelayHub> hub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var changes = await feed.ReadAsync(stoppingToken);
            if (changes.Length != 0)
                await hub.Clients.All.SendAsync("Changed", changes, stoppingToken);
            // Coalesce frequent transfer/event updates without blocking persistence.
            await Task.Delay(250, stoppingToken);
        }
    }
}
