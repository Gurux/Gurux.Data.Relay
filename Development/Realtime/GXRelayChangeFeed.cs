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
