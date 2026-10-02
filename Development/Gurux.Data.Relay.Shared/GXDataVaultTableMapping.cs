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

using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common;
using Gurux.Service.Orm.Common.Enums;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXDataVaultTableMapping : IUnique<Guid>, IGXEntityMetadata
{
    /// <summary>
    /// Identifier of the mapping. This is set by the database and should not be modified by the application.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Creation time of the mapping. This is set by the database and should not be modified by the application.
    /// </summary>
    [DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    /// <summary>
    /// Update time of the mapping. This is set by the database and should not be modified by the application.
    /// </summary>
    [DataMember]
    public DateTimeOffset? Updated { get; set; }

    /// <summary>
    /// Concurrency stamp for optimistic concurrency control. This is set by the database and should not be modified by the application.
    /// </summary>
    [DataMember]
    [StringLength(36)]
    [ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    [ForeignKey(typeof(GXDatabase), OnDelete = ForeignKeyDelete.None)]
    public Guid Database { get; set; }

    [ForeignKey(typeof(GXTable), OnDelete = ForeignKeyDelete.Cascade)]

    public GXTable? SourceTable { get; set; }

    [ForeignKey(typeof(GXTable), OnDelete = ForeignKeyDelete.None)]
    public GXTable? TargetTable { get; set; }

    [DataMember]
    [IsRequired]
    public DataVaultObjectType? ObjectType { get; set; }

    public List<GXDataVaultColumnMapping> Columns { get; set; } = [];

    [IgnoreDataMember]
    public GXSchedule Schedule { get; set; } = new();

    [JsonIgnore, DataMember]
    public Guid ScheduleId { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

}

