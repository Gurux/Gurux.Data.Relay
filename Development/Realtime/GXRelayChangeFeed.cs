using System.Threading.Channels;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Realtime;

public interface IGXRelayChangePublisher
{
    void Publish(GXRelayChange change);
}

public sealed class GXRelayChangeFeed : IGXRelayChangePublisher
{
    private readonly object _gate = new();
    private readonly HashSet<GXRelayChange> _pending = [];
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

    public void Publish(GXRelayChange change)
    {
        lock (_gate)
        {
            _pending.Add(change);
            _signal.Writer.TryWrite(true);
        }
    }

    public async Task<GXRelayChange[]> ReadAsync(CancellationToken cancellationToken)
    {
        await _signal.Reader.ReadAsync(cancellationToken);
        lock (_gate)
        {
            var result = _pending.ToArray();
            _pending.Clear();
            return result;
        }
    }
}
