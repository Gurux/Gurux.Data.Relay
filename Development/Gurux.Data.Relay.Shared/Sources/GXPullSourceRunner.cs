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
using Gurux.Data.Relay.Shared.Scheduling;

namespace Gurux.Data.Relay.Shared.Sources;

public sealed class GXPullSourceRunner(IGXPullDataSourceProvider provider,
    Func<GXDataMessage, CancellationToken, ValueTask> publishAsync, TimeProvider timeProvider,
    CancellationToken lifetime = default, int readAttempts = 1, TimeSpan? retryDelay = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task RunNowAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
        token = linked.Token;
        await gate.WaitAsync(token);
        try
        {
            GXDataMessage? message;
            for (int attempt = 1; ; ++attempt)
            {
                token.ThrowIfCancellationRequested();
                try { message = await provider.ReadAsync(token); break; }
                catch (Exception) when (!token.IsCancellationRequested && attempt < readAttempts)
                { await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(1), timeProvider, token); }
            }
            token.ThrowIfCancellationRequested();
            if (message is not null) await publishAsync(message, token);
        }
        finally { gate.Release(); }
    }
    public async Task WaitForIdleAsync()
    {
        await gate.WaitAsync();
        gate.Release();
    }
    public async Task RunAsync(GXSchedule schedule, CancellationToken token)
    {
        var next = GXScheduleTiming.Initial(schedule, timeProvider.GetUtcNow());
        while (next.HasValue)
        {
            var delay = next.Value - timeProvider.GetUtcNow();
            if (delay > TimeSpan.Zero) await Task.Delay(delay, timeProvider, token);
            await RunNowAsync(token);
            next = GXScheduleTiming.Next(schedule, timeProvider.GetUtcNow());
        }
    }
}
