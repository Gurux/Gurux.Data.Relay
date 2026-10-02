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

using System.Text.Json.Nodes;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared.Enums;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Gurux.Data.Relay.Web.Server.Services;

/// <summary>Documents the enabled REST operations.</summary>
public sealed class GXRestDocumentFilter(IHttpContextAccessor http) : IDocumentFilter
{
    /// <inheritdoc/>
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        var enabled = http.HttpContext?.Items[GXWebApiPolicy.SwaggerPolicies] as Dictionary<ApplicationMode, GXModeConfiguration>;
        bool Visible(ApplicationMode mode) => enabled == null || enabled.ContainsKey(mode);
        var paths = new OpenApiPaths();
        foreach (var (path, item) in document.Paths)
        {
            if (!path.StartsWith("/api/", StringComparison.Ordinal)) continue;
            var mode = GXWebApiPolicy.ModeFromPath(path);
            bool generic = path.Contains("{mode}", StringComparison.Ordinal);
            if (!generic && !Visible(mode ?? ApplicationMode.Server)) continue;
            if (generic)
                foreach (var operation in item.Operations!.Values)
                    foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
                        if (parameter.Name == "mode")
                            parameter.Schema = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Enum = new[] { ApplicationMode.Client, ApplicationMode.Server, ApplicationMode.DataVault }
                                    .Where(Visible).Select(mode => (JsonNode)JsonValue.Create(mode.ToString().ToLowerInvariant())!).ToList()
                            };
            paths.Add(path, item);
        }
        document.Paths = paths;
    }
}
