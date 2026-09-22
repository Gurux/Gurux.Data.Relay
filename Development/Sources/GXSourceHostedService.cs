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

using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Security.Cryptography;
using Gurux.Data.Relay.Shared.Sources;

namespace Gurux.Data.Relay.Sources;

public sealed class GXSourceHostedService(IGXDataSourceConfigurationService configurationService, IEnumerable<GXDataSourceFactory> factories,
    GXSourceMessageSink sink, GXSourceRuntime runtime, ILogger<GXSourceHostedService> logger) : BackgroundService
{
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { await base.StopAsync(cancellationToken); }
        finally
        {
            if (ExecuteTask is { IsCompleted: false })
                foreach (var source in runtime.Status.Where(s => s.State != "Stopped"))
                    logger.LogError("Source {SourceId} exceeded the host shutdown deadline; cleanup is still running.", source.Id);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var updates = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        configurationService.Changed += ReloadAsync;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var running = runtime.RunAsync(stop.Token);
        updates.Writer.TryWrite(true);
        var fingerprints = new Dictionary<Guid, (string Fingerprint, GXSourceRegistration Registration)>();
        try
        {
            await foreach (var _ in updates.Reader.ReadAllAsync(token))
            {
                var registrations = new List<GXSourceRegistration>();
                var next = new Dictionary<Guid, (string Fingerprint, GXSourceRegistration Registration)>();
                var routes = new Dictionary<Guid, Guid[]>();
                try
                {
                    var ids = new HashSet<Guid>();
                    foreach (GXStoredDataSource stored in await configurationService.LoadAsync(token))
                    {
                        try
                        {
                            GXDataSourceConfiguration source = stored.Configuration;
                            if (source.InstanceId == Guid.Empty || !ids.Add(source.InstanceId)) throw new DuplicateSourceException();
                            if (!source.Enabled) continue;
                            var factory = factories.SingleOrDefault(f => f.Name.Equals(source.Provider, StringComparison.OrdinalIgnoreCase))
                                ?? throw new InvalidOperationException("Unknown provider.");
                            // In-memory fingerprint only; never log configuration values.
                            string secretFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
                                stored.Secrets.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => pair.Key + "=" + pair.Value)))));
                            string fingerprint = source.InstanceId + "\n" + source.Provider + "\n" + stored.SettingsJson + "\n" + secretFingerprint;
                            GXSourceRegistration registration;
                            if (fingerprints.TryGetValue(source.InstanceId, out var previous) && previous.Fingerprint == fingerprint)
                                registration = previous.Registration;
                            else
                            {
                                var values = new Dictionary<string, string?>(stored.Secrets, StringComparer.OrdinalIgnoreCase);
                                await using var json = new MemoryStream(Encoding.UTF8.GetBytes(stored.SettingsJson));
                                var snapshot = new ConfigurationBuilder().AddJsonStream(json).AddInMemoryCollection(values).Build();
                                registration = new(source.InstanceId, ct => factory.Create(snapshot, ct), source.Pull, source.Streaming,
                                    source.Schedule, source.Limits, source.DeliveryAttempts, source.RetryDelaySeconds);
                            }
                            next.Add(source.InstanceId, (fingerprint, registration));
                            registrations.Add(registration);
                            routes.Add(source.InstanceId, source.RouteIds.ToArray());
                        }
                        catch (DuplicateSourceException) { throw; }
                        catch (Exception ex) { logger.LogWarning("Source configuration rejected: {ErrorType}", ex.GetType().Name); }
                    }
                    // Stop old generations before changing routing; the runtime creates new ones below.
                    var unchanged = registrations.Where(r => fingerprints.TryGetValue(r.Id, out var old) && old.Registration == r).ToArray();
                    await runtime.ReloadAsync(unchanged, token);
                    foreach (var route in routes) sink.SetRoutes(route.Key, route.Value);
                    await runtime.ReloadAsync(registrations, token);
                    fingerprints = next;
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                { logger.LogWarning("Source reload rejected: {ErrorType}", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { stop.Cancel(); await running; }

        Task ReloadAsync() { updates.Writer.TryWrite(true); return Task.CompletedTask; }
    }
    private sealed class DuplicateSourceException : InvalidOperationException { }
}
