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

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Gurux.Data.Relay.Configuration;
using Gurux.Service.Orm.Common;
using Gurux.Service.Orm.Common.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace Gurux.Data.Relay.Shared;

/// <summary>Persisted execution state for a Data Vault mapping.</summary>
[DataContract]
public sealed class GXDataVaultMappingState : IUnique<Guid>, IGXEntityMetadata
{
    [DataMember, DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Gurux.Service.Orm.Common.ForeignKey(typeof(GXDataVaultTableMapping), OnDelete = ForeignKeyDelete.Cascade)]
    public Guid Id { get; set; }
    [DataMember] public DateTimeOffset? CreationTime { get; set; }
    [DataMember] public DateTimeOffset? Updated { get; set; }
    [DataMember, StringLength(36), ConcurrencyCheck] public string? ConcurrencyStamp { get; set; }
    [DataMember] public Guid RunId { get; set; }
    [DataMember] public DateTimeOffset? LastStarted { get; set; }
    [DataMember] public DateTimeOffset? LastCompleted { get; set; }
    [DataMember] public DateTimeOffset? LastSuccessfulRun { get; set; }
    [DataMember] public string Status { get; set; } = "Never run";
    [DataMember] public long? RowCount { get; set; }
    [DataMember] public long? DurationMs { get; set; }
    [DataMember] public string? Error { get; set; }
}
