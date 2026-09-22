using Gurux.Data.Relay.Shared.Protocol;

namespace Gurux.Data.Relay.Server;

public interface IProcessedMessageService
{
    Task<GXDataAcknowledgement?> TryGetAcknowledgementAsync(Guid messageId, CancellationToken cancellationToken);

    Task SaveAcknowledgementAsync(GXDataAcknowledgement acknowledgement, CancellationToken cancellationToken);
}
