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

using Gurux.Data.Relay.Web.Client;
using Gurux.Data.Relay.Web.Client.Services;
using Gurux.Data.Relay.Shared;
using Gurux.UI.Components;
using Gurux.UI.Components.Help;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<GXAdminApiClient>();
builder.Services.AddScoped<GXSoftwareUpdateClient>();
builder.Services.AddScoped<IGXDataSourceAdminApi>(provider =>
    provider.GetRequiredService<GXAdminApiClient>());
builder.Services.AddScoped<IGXProgress, GXProgressService>();
builder.Services.AddScoped<IGXTopMenu, GXTopMenuService>();
builder.Services.AddScoped<GXRealtimeClient>();
builder.Services.AddScoped<IGXNotifier, GXComponentNotifier>();
builder.Services.AddScoped<IGXLocalStorage, GXLocalStorage>();
builder.Services.AddScoped<IGXNotificationService, GXNotificationService>();
builder.Services.AddScoped(sp =>
{
    return new GXHelpService
    {
        BaseAddress = "/",
        HiddenUrls = ["/"]
    };
});
await builder.Build().RunAsync();

