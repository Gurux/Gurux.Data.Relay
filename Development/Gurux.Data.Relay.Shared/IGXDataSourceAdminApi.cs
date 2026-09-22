namespace Gurux.Data.Relay.Shared;

public interface IGXDataSourceAdminApi
{
    Task<GXDataSourceRequest> CreateDataSourceAsync(GXDataSourceRequest source, CancellationToken token);
}
