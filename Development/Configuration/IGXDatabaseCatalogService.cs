namespace Gurux.Data.Relay.Configuration;

public interface IGXDatabaseCatalogService
{
    Task<List<Shared.GXDatabase>> GetDatabasesAsync(CancellationToken token);
    Task SaveDatabaseAsync(Shared.GXDatabase database, CancellationToken token);
    Task DeleteDatabaseAsync(Guid id, string? concurrencyStamp, CancellationToken token);
}
