using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Configuration;

namespace Gurux.Data.Relay.Server;

public interface IDestinationTableService
{
    Task EnsureReadyAsync(string destinationTable, GXDataMessage message, CancellationToken cancellationToken);

    Task EnsureReadyAsync(GXDatabaseConfiguration database, string destinationTable, GXDataMessage message, CancellationToken cancellationToken);

    Task EnsureSchemaAsync(string destinationTable, GXDataMessage message, CancellationToken cancellationToken);
}

