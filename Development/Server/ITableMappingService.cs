using Gurux.Data.Relay.Shared.Protocol;

namespace Gurux.Data.Relay.Server;

public interface ITableMappingService
{
    Task<string> GetOrCreateDestinationTableAsync(GXDataMessage message, CancellationToken cancellationToken);
}
