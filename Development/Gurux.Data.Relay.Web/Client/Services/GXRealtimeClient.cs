using Gurux.Data.Relay.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace Gurux.Data.Relay.Web.Client.Services;

public sealed class GXRealtimeClient : IAsyncDisposable
{
    private readonly HubConnection _hub;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<Guid, string> _editing = [];
    private readonly SemaphoreSlim _presenceGate = new(1);
    private Task? _runner;
    private bool _disposed;
    public event Func<GXRelayChange[], Task>? Changed;
    public event Action? StatusChanged;
    public bool Connected => _hub.State == HubConnectionState.Connected;
    public GXEditPresence[] Editors { get; private set; } = [];
    public string? ConnectionId => _hub.ConnectionId;

    public GXRealtimeClient(NavigationManager navigation)
    {
        _hub = new HubConnectionBuilder().WithUrl(navigation.ToAbsoluteUri("hubs/relay")).Build();
        _hub.On<GXRelayChange[]>("Changed", NotifyAsync);
        _hub.On<GXEditPresence[]>("Editors", editors =>
        {
            Editors = editors;
            StatusChanged?.Invoke();
        });
        _hub.Closed += _ => { Editors = []; StatusChanged?.Invoke(); return Task.CompletedTask; };
    }

    public void Start() => _runner ??= RunAsync();

    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                if (!Connected)
                {
                    await _hub.StartAsync(_lifetime.Token);
                    await SendEditingAsync();
                    StatusChanged?.Invoke();
                    // Reload after every connection, including initial connection, to cover missed updates.
                    await NotifyAsync([new(null, GXRelayChangeKind.All)]);
                }
                await Task.Delay(2000, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            catch
            {
                StatusChanged?.Invoke();
                try { await Task.Delay(2000, _lifetime.Token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task NotifyAsync(GXRelayChange[] changes)
    {
        if (Changed is null) return;
        // One failed view must not prevent other views receiving updates.
        foreach (Func<GXRelayChange[], Task> callback in Changed.GetInvocationList())
            try { await callback(changes); }
            catch (Exception) { /* Each view reports its own errors; continue notifying other views. */ }
    }

    public async Task SetEditingAsync(Guid owner, string? resource)
    {
        if (_disposed) return;
        if (resource is null) _editing.Remove(owner);
        else _editing[owner] = resource;
        await SendEditingAsync();
    }

    private async Task SendEditingAsync()
    {
        await _presenceGate.WaitAsync();
        try
        {
            if (Connected)
                await _hub.InvokeAsync("SetEditing", _editing.Values.Distinct().ToArray(), _lifetime.Token);
        }
        catch (Exception) { /* Reannounced on reconnect; cancellation needs no announcement. */ }
        finally { _presenceGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        if (_runner is not null) await _runner;
        await _hub.DisposeAsync();
        _lifetime.Dispose();
    }
}
