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

using Gurux.Service.Orm.Common;
using Gurux.Data.Relay.Enums;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Configuration;
/// <summary>
/// Configuration for the relay application.
/// </summary>
[DataContract]
internal sealed class RelayConfiguration : IUnique<Guid>, IGXEntityMetadata
{

    /// <summary>
    /// Unique identifier of the configuration record.
    /// </summary>
    [DataMember, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? Updated { get; set; }

    [System.Runtime.Serialization.DataMember]
    [System.ComponentModel.DataAnnotations.StringLength(36)]
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    [DataMember]
    public ApplicationMode Mode { get; set; }

    /// <summary>
    /// Configuration graph loaded through the reference tables.
    /// </summary>
    [IgnoreDataMember]
    public GXSettings? Payload { get; set; }

    [DataMember(Name = "Payload"), Gurux.Service.Orm.Common.ForeignKey(typeof(GXSettings), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid PayloadId { get; set; }

}



