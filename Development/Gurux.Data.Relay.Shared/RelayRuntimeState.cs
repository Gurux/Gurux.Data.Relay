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

using System.Runtime.Serialization;
using Gurux.Service.Orm.Common;
using Gurux.Data.Relay.Shared.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Gurux.Data.Relay.Shared;

[DataContract]
public sealed class RelayRuntimeState : IUnique<ApplicationMode>, IGXEntityMetadata
{
    [DataMember, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public ApplicationMode Id { get; set; }

    [DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [DataMember]
    public DateTimeOffset? Updated { get; set; }

    [DataMember]
    [StringLength(36)]
    [ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    [DataMember, System.ComponentModel.DefaultValue(0)]
    public int Version { get; set; }

    [IgnoreDataMember]
    public GXClientState? ClientState { get; set; }

    [IgnoreDataMember]
    public GXServerState? ServerState { get; set; }

}

