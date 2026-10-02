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

namespace Gurux.Data.Relay.Server;

public static class GXServerDatabaseContext
{
    private static readonly AsyncLocal<int?> CurrentIndex = new();
    private static readonly AsyncLocal<string?> CurrentDestination = new();
    private static readonly AsyncLocal<string?> CurrentRoute = new();

    public static string? DestinationTable => CurrentDestination.Value;
    public static string? RouteKey => CurrentRoute.Value;

    public static IDisposable Push(GXServerMessageRoute route)
    {
        Scope scope = new(DatabaseIndex, DestinationTable, RouteKey);
        DatabaseIndex = route.DatabaseIndex;
        CurrentDestination.Value = route.DestinationTable;
        CurrentRoute.Value = route.RouteKey;
        return scope;
    }

    private sealed class Scope(int? index, string? destination, string? route) : IDisposable
    {
        public void Dispose()
        {
            DatabaseIndex = index;
            CurrentDestination.Value = destination;
            CurrentRoute.Value = route;
        }
    }

    public static int? DatabaseIndex
    {
        get => CurrentIndex.Value;
        set => CurrentIndex.Value = value;
    }
}

