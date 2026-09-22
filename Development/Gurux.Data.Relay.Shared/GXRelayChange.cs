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

namespace Gurux.Data.Relay.Shared;

[Flags]
public enum GXRelayChangeKind
{
    Configuration = 1,
    State = 2,
    Events = 4,
    Data = 8,
    Store = 16,
    All = Configuration | State | Events | Data | Store
}

// Invalidation only: configuration values and credentials never travel through the hub.
public sealed record GXRelayChange(string? Mode, GXRelayChangeKind Kind);
public sealed record GXEditPresence(string Resource, string[] Connections);

public sealed class GXLiveUpdateState
{
    private bool _pending;
    public bool HasConflict { get; private set; }

    public bool Receive(bool editing, bool affectsDraft)
    {
        if (!editing) return true;
        _pending = true;
        HasConflict |= affectsDraft;
        return false;
    }

    public bool Resume(bool editing)
    {
        if (editing || !_pending) return false;
        _pending = false;
        HasConflict = false;
        return true;
    }
}
