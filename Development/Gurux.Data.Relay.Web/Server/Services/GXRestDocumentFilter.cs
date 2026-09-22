using System.Text.Json.Nodes;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Enums;
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
