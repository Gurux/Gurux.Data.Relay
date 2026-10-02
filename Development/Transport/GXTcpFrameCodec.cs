//
// --------------------------------------------------------------------------
//  Gurux Ltd
// 
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License 
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of 
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2. 
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

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

