using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Database;

public interface IGXDatabaseConnectionTestService
{
    Task TestConnectionAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken);
}
