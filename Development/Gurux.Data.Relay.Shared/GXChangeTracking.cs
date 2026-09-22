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
using Gurux.Data.Relay.Shared.Enums;
using System.ComponentModel.DataAnnotations;

namespace Gurux.Data.Relay.Shared;

/// <summary>
/// Change tracking settings for a database table.
/// </summary>
[DataContract]
public sealed class GXChangeTracking : IUnique<Guid>, IGXEntityMetadata
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

    [DataMember(IsRequired = true), ForeignKey(typeof(GXTable), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    [JsonIgnore]
    public Guid Table { get; set; }
    [DataMember]
    public ChangeTrackingType Type { get; set; } = ChangeTrackingType.None;

    /// <summary>
    /// Column name that indicates when a row was created. 
    /// This is used for change tracking purposes.
    /// </summary>
    [DataMember]
    public string? CreatedColumn { get; set; }
    /// <summary>
    /// Column name that indicates when a row was last updated.
    /// </summary>
    [DataMember]
    public string? UpdatedColumn { get; set; }
    /// <summary>
    /// Column name that indicates when a row was deleted.
    /// </summary>
    [DataMember]
    public string? DeletedColumn { get; set; }
    /// <summary>
    /// Column name used for change tracking purposes.
    /// </summary>
    [DataMember]
    public string? Column { get; set; }

    /// <summary>Comma-separated source columns to hash; empty means all transferred columns.</summary>
    [DataMember]
    public string? HashColumns { get; set; }
}



