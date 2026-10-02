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
