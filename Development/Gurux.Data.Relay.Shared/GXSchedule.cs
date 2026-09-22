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
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common;
using Gurux.Service.Orm.Common.Enums;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Gurux.Data.Relay.Shared;

/// <summary>
/// Transfer schedule for a database table.
/// </summary>
[DataContract]
public sealed class GXSchedule : IUnique<Guid>, IGXEntityMetadata
{
    /// <summary>
    /// Identifier of the schedule. 
    /// The schedule is owned by a table or mapping, 
    /// and will be deleted when the owner is deleted.
    /// </summary>
    [DataMember]
    public Guid Id { get; set; }

    [DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [DataMember]
    public DateTimeOffset? Updated { get; set; }

    [DataMember]
    [StringLength(36)]
    [ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    [DataMember(IsRequired = true), ForeignKey(typeof(GXSettings), OnDelete = ForeignKeyDelete.Cascade)]
    [JsonIgnore]
    public Guid Settings { get; set; }

    /// <summary>
    /// Owner mapping of the schedule.
    /// The schedule is deleted when the GXDataVaultTableMapping is deleted.
    /// </summary>
    [DataMember]
    [JsonIgnore]
    [ForeignKey(typeof(GXDataVaultTableMapping), OnDelete = ForeignKeyDelete.Cascade)]
    public Guid? Mapping { get; set; }

    /// <summary>
    /// Owner table of the schedule. 
    /// </summary>
    [DataMember]
    [ForeignKey(typeof(GXTable), OnDelete = ForeignKeyDelete.None)]
    [JsonIgnore]
    public Guid? Table { get; set; }

    [DataMember]
    public ScheduleType Type { get; set; } = ScheduleType.Manual;

    [DataMember]
    public int? IntervalSeconds { get; set; }

    [DataMember]
    public string? Time { get; set; }

    [DataMember]
    public string? Expression { get; set; }

    /// <summary>
    /// Database context. The owning table or mapping controls the schedule's lifetime.
    /// </summary>
    [DataMember, ForeignKey(typeof(GXDatabase))]
    public Guid? Database { get; set; }
}



