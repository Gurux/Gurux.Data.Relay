using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Client;

public interface IDatabaseChangeNotifierFactory
{
    Task<IDatabaseChangeSubscription> CreateAsync(
        GXDatabaseConfiguration database,
        GXTableConfiguration table,
        CancellationToken cancellationToken);
}

