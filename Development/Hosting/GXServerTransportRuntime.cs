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

using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Gurux.Data.Relay.Hosting;

/// <summary>Reconciles running listeners with committed server settings.</summary>
internal sealed class GXServerTransportRuntime(
    IGXConfigurationService configurations,
    IGXServerConfigurationChanges changes,
    Func<GXTransportConfiguration, CancellationToken, Task> runListener,
    ILogger logger,
    Func<GXServerConfiguration, CancellationToken, Task>? configure = null)
{
    private sealed record Listener(Endpoint Settings, CancellationTokenSource Cancellation, Task Task);
    private sealed record Endpoint(TransportType Type, int Port, int MaximumMessageSize,
        string? Broker, string? Topic, string? AcknowledgementTopic, string? Username, string? Password,
        bool UseTls, int ConnectTimeoutSeconds, int AcknowledgementTimeoutSeconds);
    private readonly Dictionary<Guid, Listener> _listeners = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationToken _stoppingToken;
    private bool _stopped;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        changes.ServerConfigurationSaved += ReloadAsync;
        try
        {
            try { await ReloadAsync(); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogError("Server listeners could not start: {ErrorType}. Save corrected settings to retry.", ex.GetType().Name);
            }
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        finally
        {
            changes.ServerConfigurationSaved -= ReloadAsync;
            await _gate.WaitAsync();
            try
            {
                _stopped = true;
                foreach (Listener listener in _listeners.Values) listener.Cancellation.Cancel();
                foreach (Listener listener in _listeners.Values) await StopAsync(listener);
                _listeners.Clear();
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
            GXServerConfiguration? configuration = await configurations.LoadServerAsync(_stoppingToken);
            configuration ??= new();
            configuration.EnsureRoutingIds();
            GXTransportRouting.Validate(configuration.Databases, configuration.Transports, server: true);
            if (configure != null) await configure(configuration, _stoppingToken);
            var desired = configuration.Transports.ToDictionary(t => t.Id);
            foreach ((Guid id, Listener listener) in _listeners.ToArray())
            {
                if (!desired.TryGetValue(id, out var transport) || listener.Settings != Describe(transport) || listener.Task.IsCompleted)
                {
                    await StopAsync(listener);
                    _listeners.Remove(id);
                }
            }
            foreach (GXTransportConfiguration transport in configuration.Transports)
            {
                if (_listeners.ContainsKey(transport.Id)) continue;
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
                try
                {
                    // TCP binds synchronously before its first await, so bind errors reach the save caller.
                    Task task = runListener(transport, cancellation.Token);
                    if (task.IsCompleted) await task;
                    _listeners.Add(transport.Id, new(Describe(transport), cancellation, task));
                    _ = ObserveAsync(task, transport.Id, cancellation.Token);
                }
                catch
                {
                    cancellation.Cancel();
                    cancellation.Dispose();
                    throw;
                }
            }
        }
        finally { _gate.Release(); }
    }

    private async Task ObserveAsync(Task task, Guid id, CancellationToken cancellationToken)
    {
        try { await task; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError("Server transport {TransportId} stopped: {ErrorType}.", id, ex.GetType().Name); }
    }

    private static async Task StopAsync(Listener listener)
    {
        listener.Cancellation.Cancel();
        try { await listener.Task; }
        catch (Exception) { /* Observed and logged by ObserveAsync. */ }
        finally { listener.Cancellation.Dispose(); }
    }

    private static Endpoint Describe(GXTransportConfiguration t) => new(t.Type, t.Port, t.MaximumMessageSize,
        t.Broker, t.Topic, t.AcknowledgementTopic, t.Username, t.Password, t.UseTls,
        t.ConnectTimeoutSeconds, t.AcknowledgementTimeoutSeconds);
}


