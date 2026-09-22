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

using Gurux.Data.Relay.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared.Enums;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Shared.Sources;

namespace Gurux.Data.Relay.Sources;

public static class GXDataSourceServiceExtensions
{
    public static IReadOnlySet<Guid> GetProviderRouteIds(IConfiguration? configuration)
    {
        var result = new HashSet<Guid>();
        if (configuration == null) return result;
        foreach (var source in configuration.GetSection("DataSources").GetChildren())
        {
            // Disabled sources retain their routing configuration for later re-enabling.
            foreach (var route in source.GetSection("RouteIds").GetChildren())
                if (Guid.TryParse(route.Value, out var id) && id != Guid.Empty) result.Add(id);
        }
        return result;
    }

    public static bool ShouldRun(GXCommandLineOptions options) =>
        ((options.WebHost || options.RunClientAndServer || options.Mode == ApplicationMode.Client) &&
        !options.Update && !options.ShowHelp && !options.ShowSettings && !options.ShowEvents && !options.Configure &&
        !options.ResetClientState && !options.SendSchema && !options.RefreshInformationMart && options.RestEnabled == null &&
        options.ExportSettingsPath == null && options.ImportSettingsPath == null && options.ParseError == null);

    public static IServiceCollection AddRelayDataSources(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IGXMessageDelivery>(sp => new GXMessageDelivery(
            sp.GetRequiredService<IGXTransportFactory>(), sp.GetRequiredService<ILogger<GXMessageDelivery>>()));
        /*
        services.AddSingleton(new GXDataSourceFactory("modbus-tcp", (config, token) =>
        {
            token.ThrowIfCancellationRequested();
            var settings = config.Get<GXModbusDataSourceSettings>() ?? throw new InvalidOperationException("Modbus settings are required.");
            // Binder appends to initialized collections; explicit keys replace the defaults.
            settings.Keys = config.GetSection("Keys").Get<List<string>>() ?? ["DeviceId", "ObservationId"];
            return ValueTask.FromResult<IGXDataSourceProvider>(new GXModbusDataSourceProvider(settings));
        }));
        services.AddSingleton(new GXDataSourceFactory("dlms-wrapper", (config, token) =>
        {
            token.ThrowIfCancellationRequested();
            var settings = config.Get<GXDlmsWrapperDataSourceSettings>() ?? throw new InvalidOperationException("DLMS settings are required.");
            settings.ObisMappings = config.GetSection("ObisMappings").Get<List<GXDlmsObisMapping>>() ?? [];
            var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in new[] { "Password", "AuthenticationKey", "BlockCipherKey", "SystemTitle" })
                if (config[name] is { Length: > 0 } value) secrets.Add(name, value);
            return ValueTask.FromResult<IGXDataSourceProvider>(new GXDlmsWrapperDataSourceProvider(settings, secrets));
        }));
        */
        services.AddSingleton<GXSourceMessageSink>();
        services.AddSingleton<IGXSourceMessageSink>(sp => sp.GetRequiredService<GXSourceMessageSink>());
        services.AddSingleton<GXSourceRuntime>();
        services.AddHostedService<GXSourceHostedService>();
        return services;
    }
}
