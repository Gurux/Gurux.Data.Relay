using System.Buffers.Binary;
using System.Net.Sockets;

namespace Gurux.Data.Relay.Transport;

public static class GXTcpFrameCodec
{
    public static async Task WriteFrameAsync(NetworkStream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] header = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)payload.Length));
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
    }

    public static async Task<byte[]> ReadFrameAsync(NetworkStream stream, int maximumMessageSize, CancellationToken cancellationToken)
    {
        byte[]? payload = await TryReadFrameAsync(stream, maximumMessageSize, cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException("Unexpected end of TCP stream before frame header.");
        }

        return payload;
    }

    public static async Task<byte[]?> TryReadFrameAsync(NetworkStream stream, int maximumMessageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maximumMessageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumMessageSize));
        }

        byte[]? header = await TryReadExactAsync(stream, sizeof(uint), allowCleanEndOfStream: true, cancellationToken);
        if (header is null)
        {
            return null;
        }

        uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (payloadLength == 0 || payloadLength > maximumMessageSize)
        {
            throw new InvalidOperationException($"Invalid TCP frame length '{payloadLength}'.");
        }

        return await TryReadExactAsync(stream, checked((int)payloadLength), allowCleanEndOfStream: false, cancellationToken)
            ?? throw new InvalidOperationException($"Unexpected end of TCP stream while reading {payloadLength} bytes.");
    }

    private static async Task<byte[]?> TryReadExactAsync(
        NetworkStream stream,
        int length,
        bool allowCleanEndOfStream,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                if (offset == 0 && allowCleanEndOfStream)
                {
                    return null;
                }

                throw new InvalidOperationException($"Unexpected end of TCP stream after reading {offset}/{length} bytes.");
            }

            offset += read;
        }

        return buffer;
    }
}

