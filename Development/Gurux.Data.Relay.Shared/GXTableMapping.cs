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
using System.Text.Json.Serialization;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common;

namespace Gurux.Data.Relay.Shared;

[DataContract]
public sealed class GXTableMapping : IUnique<Guid>, IGXEntityMetadata
{
    [DataMember, JsonIgnore] public Guid Id { get; set; }

    [DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [DataMember]
    public DateTimeOffset? Updated { get; set; }

    [DataMember]
    [StringLength(36)]
    [ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    [DataMember(IsRequired = true), JsonIgnore, ForeignKey(typeof(RelayRuntimeState), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public ApplicationMode RuntimeState { get; set; }

    [DataMember]
    public string Source { get; set; } = string.Empty;

    [DataMember]
    public string Destination { get; set; } = string.Empty;
}

