using Gurux.Data.Relay.Enums;
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
