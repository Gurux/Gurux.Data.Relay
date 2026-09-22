namespace Gurux.Data.Relay.Sources;

public interface IGXDataSourceConfigurationService
{
    event Func<Task>? Changed;
    Task<IReadOnlyList<GXStoredDataSource>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(GXStoredDataSource source, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
