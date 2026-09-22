using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Configuration;

public interface IGXDataVaultRuntimeStateStore
{
    Task<List<GXDataVaultMappingState>> LoadDataVaultStateAsync(CancellationToken cancellationToken);
    Task RecordDataVaultRunAsync(Guid mappingId, Guid runId, string status, long? rowCount,
        long? durationMs, string? error, CancellationToken cancellationToken);
}
