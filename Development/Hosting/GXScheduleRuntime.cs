using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Log;
using Gurux.Data.Relay.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Gurux.Data.Relay.Hosting;

/// <summary>Restarts a web scheduler after committed settings changes.</summary>
internal sealed class GXScheduleRuntime(
    Action<Func<Task>> subscribe,
    Action<Func<Task>> unsubscribe,
    ApplicationMode mode,
    Func<CancellationToken, Task> run,
    ILogger logger)
{
    public GXScheduleRuntime(IGXClientConfigurationChanges changes, Func<CancellationToken, Task> run, ILogger logger)
        : this(handler => changes.ClientConfigurationSaved += handler, handler => changes.ClientConfigurationSaved -= handler,
            ApplicationMode.Client, run, logger) { }

    public GXScheduleRuntime(IGXDataVaultConfigurationChanges changes, Func<CancellationToken, Task> run, ILogger logger)
        : this(handler => changes.DataVaultConfigurationSaved += handler, handler => changes.DataVaultConfigurationSaved -= handler,
            ApplicationMode.DataVault, run, logger) { }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationToken _stoppingToken;
    private CancellationTokenSource? _cancellation;
    private Task? _running;
    private bool _stopped;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        subscribe(ReloadAsync);
        try
        {
            await ReloadAsync();
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        finally
        {
            unsubscribe(ReloadAsync);
            await _gate.WaitAsync();
            try
            {
                _stopped = true;
                await StopAsync();
            }
            finally { _gate.Release(); }
        }
    }

    private async Task ReloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_stopped || _stoppingToken.IsCancellationRequested) return;
            await StopAsync();
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
            _running = RunObservedAsync(_cancellation.Token);
        }
        finally { _gate.Release(); }
    }

    private async Task RunObservedAsync(CancellationToken cancellationToken)
    {
        GXRelayModeContext.Current.Value = mode;
        try { await run(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Mode} scheduler stopped. Save corrected settings to retry.", mode);
        }
    }

    private async Task StopAsync()
    {
        if (_cancellation is null) return;
        await _cancellation.CancelAsync();
        try { if (_running is not null) await _running; }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _running = null;
        }
    }
}
