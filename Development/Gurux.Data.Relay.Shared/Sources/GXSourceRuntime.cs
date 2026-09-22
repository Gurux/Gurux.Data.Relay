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
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace Gurux.Data.Relay.Shared.Sources;

/// <summary>Owns provider generations. Failures are isolated to a single registration.</summary>
public sealed class GXSourceRuntime(IGXSourceMessageSink sink, TimeProvider time,
    ILogger<GXSourceRuntime> logger)
{
    private sealed record Instance(CancellationTokenSource Stop, Task Task);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, Instance> instances = [];
    private readonly ConcurrentDictionary<Guid, GXPullSourceRunner> runners = new();
    private readonly ConcurrentDictionary<Guid, GXSourceStatus> statuses = new();
    private IReadOnlyList<GXSourceRegistration> registrations = [];
    private CancellationToken stopping;
    private bool running;
    public IReadOnlyCollection<GXSourceStatus> Status => statuses.Values.ToArray();

    public Task RunNowAsync(Guid id, CancellationToken token) => runners.TryGetValue(id, out var runner)
        ? runner.RunNowAsync(token) : throw new InvalidOperationException("Pull source is not running.");

    public async Task ReloadAsync(IReadOnlyList<GXSourceRegistration> values, CancellationToken token)
    {
        if (values.Any(v => v.Id == Guid.Empty) || values.Select(v => v.Id).Distinct().Count() != values.Count)
            throw new InvalidOperationException("Source IDs must be nonempty and unique.");
        await gate.WaitAsync(token);
        try
        {
            var desired = values.ToDictionary(v => v.Id);
            foreach (var pair in instances.ToArray())
            {
                if (!pair.Value.Stop.IsCancellationRequested && !pair.Value.Task.IsCompleted &&
                    desired.TryGetValue(pair.Key, out var wanted) && registrations.Contains(wanted)) continue;
                pair.Value.Stop.Cancel();
                await pair.Value.Task.WaitAsync(token);
                pair.Value.Stop.Dispose();
                instances.Remove(pair.Key);
            }
            registrations = values.ToArray();
            if (running)
                foreach (var value in registrations)
                {
                    if (instances.ContainsKey(value.Id)) continue;
                    var stop = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                    instances.Add(value.Id, new(stop, SuperviseAsync(value, stop.Token)));
                }
        }
        finally { gate.Release(); }
    }

    public async Task RunAsync(CancellationToken token)
    {
        stopping = token;
        running = true;
        try
        {
            await ReloadAsync(registrations, token);
            await Task.Delay(Timeout.Infinite, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            await gate.WaitAsync();
            try
            {
                running = false;
                foreach (var item in instances.Values) item.Stop.Cancel();
                foreach (var item in instances.Values) { await item.Task; item.Stop.Dispose(); }
                instances.Clear();
            }
            finally { gate.Release(); }
        }
    }

    private async Task SuperviseAsync(GXSourceRegistration registration, CancellationToken token)
    {
        int backoff = 1;
        while (!token.IsCancellationRequested)
        {
            var started = time.GetTimestamp();
            IGXDataSourceProvider? provider = null;
            using var generation = CancellationTokenSource.CreateLinkedTokenSource(token);
            GXSourceQueue? queue = null;
            GXPullSourceRunner? pullRunner = null;
            using var publishGate = new SemaphoreSlim(1, 1);
            var tasks = new List<Task>();
            try
            {
                if ((!registration.Pull && !registration.Streaming) || registration.DeliveryAttempts < 1 || registration.RetryDelaySeconds < 1)
                    throw new InvalidOperationException("Invalid source capabilities or retry settings.");
                provider = await registration.Create(generation.Token);
                if (registration.Pull && provider is not IGXPullDataSourceProvider || registration.Streaming && provider is not IGXStreamingDataSourceProvider)
                    throw new InvalidOperationException("Provider does not implement an enabled capability.");
                queue = new GXSourceQueue(registration.Id, new RetryingSink(sink, registration, time), registration.Limits ?? new());
                tasks.Add(queue.RunAsync(generation.Token));
                Func<GXDataMessage, CancellationToken, ValueTask> Publisher()
                {
                    int busy = 0;
                    return async (message, ct) =>
                    {
                        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
                            throw new InvalidOperationException("Await the previous publication for this capability.");
                        try
                        {
                            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, generation.Token);
                            await publishGate.WaitAsync(linked.Token);
                            try { await queue.PublishAsync(message, linked.Token); }
                            finally { publishGate.Release(); }
                        }
                        finally { Volatile.Write(ref busy, 0); }
                    };
                }
                if (registration.Pull)
                {
                    var schedule = registration.Schedule ?? throw new InvalidOperationException("Pull schedule is required.");
                    var runner = pullRunner = new GXPullSourceRunner((IGXPullDataSourceProvider)provider, Publisher(), time,
                        generation.Token, registration.DeliveryAttempts, TimeSpan.FromSeconds(registration.RetryDelaySeconds));
                    runners[registration.Id] = runner;
                    if (schedule.Type != ScheduleType.Manual) tasks.Add(runner.RunAsync(schedule, generation.Token));
                }
                if (registration.Streaming)
                    tasks.Add(((IGXStreamingDataSourceProvider)provider).RunAsync(Publisher(), generation.Token));
                statuses[registration.Id] = new(registration.Id, "Running");
                var finished = await Task.WhenAny(tasks);
                await finished;
                if (!token.IsCancellationRequested) throw new InvalidOperationException("Source operation ended unexpectedly.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                statuses[registration.Id] = new(registration.Id, "Failed", ex.GetType().Name);
                logger.LogWarning("Source {SourceId} failed: {ErrorType}", registration.Id, ex.GetType().Name);
            }
            finally
            {
                runners.TryRemove(registration.Id, out _);
                generation.Cancel();
                queue?.Complete();
                foreach (var task in tasks)
                    try { await task; } catch (Exception ex) { logger.LogDebug("Source task ended: {ErrorType}", ex.GetType().Name); }
                if (pullRunner != null) await pullRunner.WaitForIdleAsync();
                try
                {
                    if (provider is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
                    else if (provider is IDisposable disposable) disposable.Dispose();
                }
                catch (Exception ex) { logger.LogWarning("Source disposal failed: {ErrorType}", ex.GetType().Name); }
            }
            if (time.GetElapsedTime(started) >= TimeSpan.FromSeconds(30)) backoff = 1;
            try { await Task.Delay(TimeSpan.FromSeconds(backoff), time, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            backoff = Math.Min(30, backoff * 2);
        }
        statuses[registration.Id] = new(registration.Id, "Stopped");
    }

    private sealed class RetryingSink(IGXSourceMessageSink inner, GXSourceRegistration registration, TimeProvider time) : IGXSourceMessageSink
    {
        public async ValueTask DeliverAsync(Guid id, GXDataMessage message, CancellationToken token)
        {
            for (int attempt = 1; ; ++attempt)
            {
                token.ThrowIfCancellationRequested();
                try { await inner.DeliverAsync(id, message, token); return; }
                catch (Exception) when (!token.IsCancellationRequested && attempt < registration.DeliveryAttempts)
                { await Task.Delay(TimeSpan.FromSeconds(registration.RetryDelaySeconds), time, token); }
            }
        }
    }
}
