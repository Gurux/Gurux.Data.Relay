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

using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Log;

public static class GXEventLogContext
{
    /// <summary>
    /// Selects the relay mode for logging in the current asynchronous operation.
    /// </summary>
    /// <param name="mode">The mode whose logging settings and event stream are used.</param>
    /// <returns>A scope that restores the previous mode when disposed.</returns>
    public static IDisposable BeginMode(ApplicationMode mode)
    {
        var previous = GXRelayModeContext.Current.Value;
        GXRelayModeContext.Current.Value = mode;
        return new Scope(() => GXRelayModeContext.Current.Value = previous);
    }

    private static readonly AsyncLocal<int?> Current = new();
    public static int? DatabaseIndex => Current.Value;
    public static IDisposable BeginDatabase(int? index)
    {
        int? previous = Current.Value;
        Current.Value = index;
        return new Scope(() => Current.Value = previous);
    }
    private sealed class Scope(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}

