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
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Gurux.Data.Relay.Shared;

/// <summary>
/// Source and target column settings for a data vault mapping.
/// </summary>
[DataContract]
public sealed class GXDataVaultColumnMapping : IUnique<Guid>, IGXEntityMetadata
{
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

    [DataMember(IsRequired = true), ForeignKey(typeof(GXDataVaultTableMapping), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    [System.Text.Json.Serialization.JsonIgnore]

    public Guid Mapping { get; set; }
    [DataMember]

    public string SourceColumn { get; set; } = string.Empty;

    /// <summary>Raw Vault mapping supplying this Mart column.</summary>
    public Guid? SourceMappingId { get; set; }

    public Aggregation Aggregation { get; set; }

    public string TargetColumn { get; set; } = string.Empty;

    public DataVaultColumnRole? Role { get; set; } = DataVaultColumnRole.HashKey;
}