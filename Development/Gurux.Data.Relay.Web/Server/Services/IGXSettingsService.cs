using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Web.Server.Services;

public interface IGXSettingsService
{
    Task<GXConfigurationStoreSettings> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(GXConfigurationStoreSettings settings, CancellationToken cancellationToken);
    Task TestAsync(GXConfigurationStoreSettings settings, CancellationToken cancellationToken);
}

