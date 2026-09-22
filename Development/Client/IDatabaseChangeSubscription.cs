using Gurux.Service.Orm;

namespace Gurux.Data.Relay.Client;

public interface IDatabaseChangeSubscription : IAsyncDisposable
{
    event EventHandler<GXDatabaseChangedEventArgs>? Changed;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

