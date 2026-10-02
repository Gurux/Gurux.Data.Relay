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

using System.Reflection;
using Gurux.Data.Relay.Shared;
using Gurux.Updater.Enums;
using Gurux.Updater.Model;
using Gurux.Updater.Services;

namespace Gurux.Data.Relay.Web.Server.Services;

/// <summary>
/// This class checks if there are new versions available on GitHub.
/// </summary>
public sealed class GXSoftwareUpdateService : BackgroundService
{
    public const string HttpClientName = "SoftwareUpdates";
    private readonly HttpClient _client;
    private readonly GXUpdateMonitor _monitor;
    private readonly string _currentVersion;

    public GXSoftwareUpdateService(IHttpClientFactory clients, ILogger<GXSoftwareUpdateService> logger)
    {
        _currentVersion = typeof(GXSoftwareUpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? typeof(GXSoftwareUpdateService).Assembly.GetName().Version!.ToString();
        _client = clients.CreateClient(HttpClientName);
        _monitor = new GXUpdateMonitor(new GXGitHubUpdateService(_client), new GXUpdateTarget
        {
            Name = "Gurux.Data.Relay",
            Type = UpdateTargetType.Application,
            Repository = "Gurux/Gurux.Data.Relay",
            CurrentVersion = _currentVersion
        }, TimeSpan.FromHours(6));
        _monitor.StatusChanged += state =>
        {
            if (state.Error is { } error)
            {
                logger.LogWarning(error, "Unable to check for Data Relay software updates.");
            }
        };
    }

    public GXSoftwareUpdateStatus Status => ToStatus(_monitor.Status);

    public async Task<GXSoftwareUpdateStatus> CheckAsync(CancellationToken cancellationToken)
        => _currentVersion == "0.0.1-local"
            ? Status
            : ToStatus(await _monitor.CheckAsync(cancellationToken));

    private GXSoftwareUpdateStatus ToStatus(GXUpdateMonitor.State state) => new()
    {
        CurrentVersion = state.Update?.CurrentVersion ?? _currentVersion,
        LatestVersion = state.Update?.LatestVersion,
        UpdateAvailable = state.Update?.UpdateAvailable ?? false,
        ReleaseUrl = state.Update?.ReleaseUrl,
        CheckedAt = state.CheckedAt,
        Error = state.Error is null ? null : "Unable to check for software updates. Please try again later."
    };

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => _currentVersion == "0.0.1-local" ? Task.CompletedTask : _monitor.RunAsync(stoppingToken);

    public override void Dispose()
    {
        base.Dispose();
        _client.Dispose();
    }
}
