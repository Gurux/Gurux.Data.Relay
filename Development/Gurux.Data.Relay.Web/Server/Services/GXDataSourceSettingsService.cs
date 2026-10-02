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

using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Sources;
using System.Text.Json;

namespace Gurux.Data.Relay.Web.Server.Services;

public sealed class GXDataSourceSettingsService(IGXDataSourceConfigurationService configurationService, IEnumerable<GXDataSourceFactory> factories)
{
    private const string Redacted = "••••••••";

    public async Task<GXDataSourceOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        GXDataSourceOverview overview = new()
        {
            Providers = factories.Select(factory => factory.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList()
        };
        foreach (GXStoredDataSource stored in await configurationService.LoadAsync(cancellationToken))
        {
            GXDataSourceConfiguration source = stored.Configuration;
            overview.Sources.Add(new GXDataSourceSettingsSummary
            {
                InstanceId = source.InstanceId,
                Provider = source.Provider,
                Enabled = source.Enabled,
                Pull = source.Pull,
                Streaming = source.Streaming,
                RouteIds = source.RouteIds,
                Settings = GetSettings(stored.SettingsJson)
            });
        }
        return overview;
    }

    public Task SaveAsync(GXStoredDataSource source, CancellationToken cancellationToken) => configurationService.SaveAsync(source, cancellationToken);
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => configurationService.DeleteAsync(id, cancellationToken);

    private static Dictionary<string, string> GetSettings(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind != JsonValueKind.Object ? [] : document.RootElement.EnumerateObject()
            .ToDictionary(value => value.Name, value => IsSecret(value.Name) ? Redacted : value.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSecret(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("connectionstring", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("api-key", StringComparison.OrdinalIgnoreCase);
}
