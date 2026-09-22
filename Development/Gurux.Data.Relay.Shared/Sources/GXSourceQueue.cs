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

using Gurux.Data.Relay.Shared.Protocol;
using System.Text.Json;
using System.Threading.Channels;

namespace Gurux.Data.Relay.Shared.Sources;

/// <summary>Single-reader, acknowledged publication boundary. Callers must await each publication.</summary>
public sealed class GXSourceQueue
{
    private sealed record Envelope(GXDataMessage Message, CancellationToken Token, TaskCompletionSource Completion);
    private readonly Channel<Envelope> channel;
    private readonly Guid sourceId;
    private readonly IGXSourceMessageSink sink;
    private readonly GXSourceLimits limits;
    private int publishing;

    public GXSourceQueue(Guid sourceId, IGXSourceMessageSink sink, GXSourceLimits limits)
    {
        if (limits.Capacity < 1 || limits.MaximumRows < 1 || limits.MaximumBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(limits));
        this.sourceId = sourceId; this.sink = sink; this.limits = limits;
        channel = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(limits.Capacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }

    public async ValueTask PublishAsync(GXDataMessage message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref publishing, 1, 0) != 0)
            throw new InvalidOperationException("Await the previous source publication before publishing another message.");
        try
        {
            if (message.Changes.Count > limits.MaximumRows || JsonSerializer.SerializeToUtf8Bytes(message).Length > limits.MaximumBytes)
                throw new InvalidOperationException("Source message exceeds configured limits.");
            var envelope = new Envelope(message, token, new(TaskCreationOptions.RunContinuationsAsynchronously));
            await channel.Writer.WriteAsync(envelope, token);
            using var cancellation = token.Register(() => envelope.Completion.TrySetCanceled(token));
            await envelope.Completion.Task;
        }
        finally { Volatile.Write(ref publishing, 0); }
    }

    public void Complete() => channel.Writer.TryComplete();

    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(token))
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, item.Token);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    await sink.DeliverAsync(sourceId, item.Message, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    item.Completion.TrySetResult();
                }
                catch (OperationCanceledException) { item.Completion.TrySetCanceled(linked.Token); }
                catch (Exception ex) { item.Completion.TrySetException(ex); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            channel.Writer.TryComplete();
            while (channel.Reader.TryRead(out var pending)) pending.Completion.TrySetCanceled();
        }
    }
}
