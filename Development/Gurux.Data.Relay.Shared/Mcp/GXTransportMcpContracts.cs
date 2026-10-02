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

namespace Gurux.Data.Relay.Shared.Mcp;

public sealed class GXMcpServerTransportRequest
{
    /// <summary>Catalog database for the table route. Omit when creating a listener without routes.</summary>
    public Guid? Database { get; set; }
    public int Port { get; set; }
    public string? Host { get; set; }
    public string SourceTable { get; set; } = string.Empty;
    public string DestinationTable { get; set; } = string.Empty;
    public int IntervalSeconds { get; set; } = 60;
}

public sealed class GXMcpTransportInfo
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Host { get; set; }
    public int Port { get; set; }
    public Guid? Database { get; set; }
    public IReadOnlyList<string> Routes { get; set; } = [];
}
