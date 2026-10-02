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

using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Gurux.Data.Relay.Web.Server.Services;

/// <summary>Registers the documented Relay management API and its interactive Swagger UI.</summary>
public static class GXOpenApiExtensions
{
    /// <summary>Adds OpenAPI generation for all Relay controllers.</summary>
    public static IServiceCollection AddRelayOpenApi(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Gurux Data Relay management API",
                Version = "v1",
                Description = "Manage client, server and Data Vault settings, databases, transports, mappings, transfer state and events. " +
                    "Read settings before updating them and retain entity IDs and concurrencyStamp values. " +
                    "PUT settings replaces the configuration; preserve collections that you are not changing. " +
                    "Database-list updates use the If-Match header. A stale update returns HTTP 409."
            });
            options.CustomSchemaIds(SchemaId);
            options.MapType<Type>(() => new OpenApiSchema { Type = JsonSchemaType.String, Description = "CLR type name, for example System.Int32." });
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "Gurux.Data.Relay.Web.Server.xml"), includeControllerXmlComments: true);
            options.OperationFilter<GXManagementOperationFilter>();
            options.DocumentFilter<GXRestDocumentFilter>();
        });
        return services;
    }

    private static string SchemaId(Type type) => type.IsGenericType
        ? (type.GetGenericTypeDefinition().FullName ?? type.Name).Split('`')[0].Replace('+', '.') + "Of" + string.Join("And", type.GetGenericArguments().Select(SchemaId))
        : (type.FullName ?? type.Name).Replace('+', '.').Replace("[]", "Array");

    /// <summary>Exposes Swagger, Scalar and their shared OpenAPI document.</summary>
    public static WebApplication MapRelayOpenApi(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("v1/swagger.json", "Gurux Data Relay v1");
            options.DocumentTitle = "Gurux Data Relay API";
            options.DisplayRequestDuration();
        });
        app.MapScalarApiReference(options => options.WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json"));
        return app;
    }
}
