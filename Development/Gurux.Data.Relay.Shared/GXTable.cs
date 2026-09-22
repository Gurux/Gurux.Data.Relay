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

using System.Text.Json.Serialization;

using System.Runtime.Serialization;
using Gurux.Service.Orm.Common;
using Gurux.Service.Orm.Common.Enums;
using System.ComponentModel.DataAnnotations;

namespace Gurux.Data.Relay.Shared;

/// <summary>
/// Table settings exchanged with the administration API.
/// </summary>
[DataContract]
public sealed class GXTable : IUnique<Guid>, IGXEntityMetadata
{

    [DataMember]
    public Guid Id { get; set; } = Guid.NewGuid();

    [DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [DataMember]
    public DateTimeOffset? Updated { get; set; }

    [DataMember]
    [StringLength(36)]
    [ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    [DataMember]
    public string Name { get; set; } = string.Empty;

    public List<string> Columns { get; set; } = [];

    public List<string> Keys { get; set; } = [];

    [JsonIgnore]
    public List<GXTransport> Transports { get; set; } = [];

    [DataMember]
    public string? IncrementalColumn { get; set; }
    
    [DataMember]
    [StringLength(255)]
    public string? RecordSource { get; set; }
   
    public GXChangeTracking ChangeTracking { get; set; } = new();
    
    public GXSchedule Schedule { get; set; } = new();

    [JsonIgnore, DataMember]
    public Guid ScheduleId { get; set; }

    [JsonIgnore, DataMember]
    public Guid ChangeTrackingId { get; set; }

    /// <summary>
    /// Gets the time when the table was last transferred.
    /// This property is set by the server and is read-only for the client.
    /// </summary>
    [DataMember]
    public bool DeleteSourceRowsAfterTransfer { get; set; }

    public DateTimeOffset? LastTransferred { get; set; }

    /// <summary>
    /// Gets the scheduled time for the next table transfer.
    /// This property is set by the server and is read-only for the client.
    /// </summary>
    public DateTimeOffset? NextTransferTime { get; set; }

    /// <summary>
    /// Database that owns this table.
    /// </summary>
    [DataMember(IsRequired = true), 
        ForeignKey(typeof(GXDatabase), OnDelete = ForeignKeyDelete.Cascade)]
    public Guid Database { get; set; }
}





