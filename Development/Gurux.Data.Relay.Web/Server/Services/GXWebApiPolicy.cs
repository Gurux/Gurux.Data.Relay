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
using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Web.Server.Services;

/// <summary>Cached web API policies, invalidated by committed configuration changes.</summary>
public sealed class GXWebApiPolicy : IDisposable
{

    internal const string SwaggerPolicies = "Gurux.SwaggerPolicies";
    internal static bool IsStoreMaintenancePath(PathString path) =>
        path.StartsWithSegments("/api/databases") ||
        path.Equals(new PathString("/api/settings")) ||
        path.Equals(new PathString("/api/settings/test")) ||
        path.StartsWithSegments("/api/update");
    private readonly IGXConfigurationService _configuration;
    private readonly ConcurrentDictionary<ApplicationMode, Task<GXModeConfiguration>> _cache = new();

    public GXWebApiPolicy(IGXConfigurationService configuration)
    {
        _configuration = configuration;
        if (configuration is IGXClientConfigurationChanges client) client.ClientConfigurationSaved += InvalidateAsync;
        if (configuration is IGXServerConfigurationChanges server) server.ServerConfigurationSaved += InvalidateAsync;
        if (configuration is IGXDataVaultConfigurationChanges vault) vault.DataVaultConfigurationSaved += InvalidateAsync;
    }

    internal Task InvalidateAsync() { _cache.Clear(); return Task.CompletedTask; }

    internal async Task<GXModeConfiguration> GetAsync(ApplicationMode mode, CancellationToken token)
    {
        try { return await _cache.GetOrAdd(mode, LoadAsync).WaitAsync(token); }
        catch { _cache.TryRemove(mode, out _); throw; }
    }

    private async Task<GXModeConfiguration> LoadAsync(ApplicationMode mode) => mode switch
    {
        ApplicationMode.Client => await _configuration.LoadClientAsync(default) ?? new GXClientConfiguration(),
        ApplicationMode.DataVault => await _configuration.LoadDataVaultAsync(default) ?? new GXDataVaultConfiguration(),
        _ => await _configuration.LoadServerAsync(default) ?? new GXServerConfiguration()
    };

    internal static ApplicationMode? ModeFromPath(string path)
    {
        string[] parts = path.Trim('/').Split('/');
        int index = parts.Length > 1 && parts[1].Equals("database", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        return parts.Length > index && Enum.TryParse<ApplicationMode>(parts[index], true, out var mode) &&
            mode is ApplicationMode.Client or ApplicationMode.Server or ApplicationMode.DataVault ? mode : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_configuration is IGXClientConfigurationChanges client) client.ClientConfigurationSaved -= InvalidateAsync;
        if (_configuration is IGXServerConfigurationChanges server) server.ServerConfigurationSaved -= InvalidateAsync;
        if (_configuration is IGXDataVaultConfigurationChanges vault) vault.DataVaultConfigurationSaved -= InvalidateAsync;
    }
}
