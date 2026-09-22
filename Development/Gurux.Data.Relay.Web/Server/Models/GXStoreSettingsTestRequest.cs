using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Web.Server.Models;

public sealed class GXStoreSettingsTestRequest
{
    public GXConfigurationStoreSettings Settings { get; set; } = new();
}

