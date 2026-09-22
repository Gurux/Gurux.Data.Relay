namespace Gurux.Data.Relay.Transport;

public interface IGXMessageSerializer
{
    byte[] Serialize<T>(T value);

    T Deserialize<T>(ReadOnlySpan<byte> payload);
}
