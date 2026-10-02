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

using Gurux.Data.Relay.Shared.Enums;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Gurux.Data.Relay.Web.Server.Services;

/// <summary>Builds CORS policies for public REST requests from the selected mode's settings.</summary>
public sealed class GXRestCorsPolicyProvider(GXWebApiPolicy policies) : ICorsPolicyProvider
{
    /// <inheritdoc/>
    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        if (!context.Request.Path.StartsWithSegments("/api")) return null;
        if (GXWebApiPolicy.IsStoreMaintenancePath(context.Request.Path)) return null;
        var settings = await policies.GetAsync(GXWebApiPolicy.ModeFromPath(context.Request.Path) ?? ApplicationMode.Server, context.RequestAborted);
        if (!settings.RestEnabled || !settings.CorsEnabled || settings.CorsAllowedOrigins.Count == 0) return null;
        var policy = new CorsPolicyBuilder().AllowAnyHeader().WithMethods("GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .WithExposedHeaders("ETag", "Location");
        if (settings.CorsAllowedOrigins.Contains("*")) policy.AllowAnyOrigin();
        else policy.WithOrigins(settings.CorsAllowedOrigins.ToArray());
        if (settings.CorsAllowCredentials) policy.AllowCredentials();
        return policy.Build();
    }
}
