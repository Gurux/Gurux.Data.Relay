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
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Shared.Protocol;

public sealed class GXDataMessage
{
    public int Version { get; set; } = 2;

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RecordSource { get; set; }

    public Guid MessageId { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public MessageType MessageType { get; set; }

    public GXTableSchema Table { get; set; } = new();

    /// <summary>Transfer keys selected by the user, independent of physical primary keys.</summary>
    public List<string> Keys { get; set; } = [];

    public GXTableSchema? Schema { get; set; }

    public List<GXDataChange> Changes { get; set; } = [];
}

