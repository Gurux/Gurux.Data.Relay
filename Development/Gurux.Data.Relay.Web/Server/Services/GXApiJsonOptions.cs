using System.Text.Json.Serialization;
using Gurux.Data.Relay.Transport;
using Microsoft.AspNetCore.Mvc;

namespace Gurux.Data.Relay.Web.Server.Services;

public static class GXApiJsonOptions
{
    public static void Configure(JsonOptions options)
    {
        // ORM columns refer back to their table through Parent.
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new GXSystemTypeJsonConverter());
    }
}

