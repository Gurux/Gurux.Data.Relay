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

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Gurux.Service.Orm.Common;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Log;

public sealed class GXTransportMessageLog : IUnique<long>, 
    IGXEntityMetadata
{
    [AutoIncrement]
    public long Id { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? Updated { get; set; }

    [System.Runtime.Serialization.DataMember]
    [System.ComponentModel.DataAnnotations.StringLength(36)]
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    [System.Runtime.Serialization.DataMember, System.Text.Json.Serialization.JsonIgnore,
     ForeignKey(typeof(GXSettings), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid Settings { get; set; }

    public DateTime? Timestamp { get; set; }

    public LogLevel Level { get; set; }

    public ApplicationMode Mode { get; set; }

    public TransportMessageDirection Direction { get; set; }

    public TransportType TransportType { get; set; }

    public Guid? MessageId { get; set; }

    public MessageType? MessageType { get; set; }

    public AcknowledgementStatus? AcknowledgementStatus { get; set; }

    public string? Endpoint { get; set; }

    public string? Message { get; set; }
}

