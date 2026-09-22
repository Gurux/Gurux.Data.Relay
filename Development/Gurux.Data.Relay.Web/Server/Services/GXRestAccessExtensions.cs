using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Gurux.Data.Relay.Web.Server.Services;

/// <summary>Applies mode settings to REST access and web administration.</summary>
public static class GXRestAccessExtensions
{
    /// <summary>Registers live REST, Swagger and CORS policies.</summary>
    public static IServiceCollection AddRelayRestAccess(this IServiceCollection services)
    {
        services.AddSingleton<GXWebApiPolicy>();
        services.AddCors();
        services.AddSingleton<ICorsPolicyProvider, GXRestCorsPolicyProvider>();
        return services;
    }

    /// <summary>Applies REST/Swagger availability and CORS after routing, before Swagger and controllers.</summary>
    public static IApplicationBuilder UseRelayRestAccess(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            // Store repair must work before the configuration schema can be loaded.
            if (GXWebApiPolicy.IsStoreMaintenancePath(context.Request.Path))
            {
                await next();
                return;
            }
            var policies = context.RequestServices.GetRequiredService<GXWebApiPolicy>();
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var settings = await policies.GetAsync(GXWebApiPolicy.ModeFromPath(context.Request.Path) ?? ApplicationMode.Server, context.RequestAborted);
                if (!settings.RestEnabled)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }
            if (context.Request.Path.StartsWithSegments("/swagger") || context.Request.Path.StartsWithSegments("/scalar"))
            {
                var enabled = new Dictionary<ApplicationMode, GXModeConfiguration>();
                foreach (var mode in new[] { ApplicationMode.Client, ApplicationMode.Server, ApplicationMode.DataVault })
                {
                    var settings = await policies.GetAsync(mode, context.RequestAborted);
                    if (settings.RestEnabled && settings.SwaggerEnabled) enabled[mode] = settings;
                }
                if (enabled.Count == 0) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
                context.Items[GXWebApiPolicy.SwaggerPolicies] = enabled;
            }
            await next();
        });
        app.UseCors();
        return app;
    }
}
