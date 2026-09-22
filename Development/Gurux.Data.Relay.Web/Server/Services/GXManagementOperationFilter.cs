using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Gurux.Data.Relay.Web.Server.Services;

/// <summary>Documents request bodies and concurrency headers read directly by management controllers.</summary>
public sealed class GXManagementOperationFilter : IOperationFilter
{
    /// <inheritdoc/>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        string path = context.ApiDescription.RelativePath ?? "";
        operation.OperationId = context.MethodInfo.DeclaringType!.Name.Replace("Controller", "") + "_" + context.MethodInfo.Name +
            (path.EndsWith("import-csv") ? "Csv" : path.EndsWith("import-json") ? "Json" : "");
        if (context.MethodInfo.Name == "SaveDatabases")
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "If-Match", In = ParameterLocation.Header,
                Description = "Quoted settings concurrencyStamp from GET settings. The response ETag contains the new version.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }
        if (context.ApiDescription.HttpMethod is "PUT" or "DELETE" or "POST" or "PATCH")
        {
            operation.Responses ??= new();
            operation.Responses.TryAdd("409", new OpenApiResponse { Description = "The entity changed since it was read, or the requested resource already exists." });
        }
        if (path.EndsWith("import-csv") || path.EndsWith("import-json"))
        {
            bool json = path.EndsWith("import-json");
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = json ? "Array of row objects, keyed by database column name." : "CSV text, using the delimiter and header options in the query string.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    [json ? "application/json" : "text/csv"] = new()
                    {
                        Schema = json ? new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.Object, AdditionalPropertiesAllowed = true } }
                            : new OpenApiSchema { Type = JsonSchemaType.String }
                    }
                }
            };
        }
    }
}
