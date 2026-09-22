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
using Gurux.Service.Orm.Common.Enums;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Gurux.Data.Relay.Shared;

[DataContract]
public sealed class GXDataSource : IUnique<Guid>, IGXEntityMetadata
{
    [DataMember] public Guid Id { get; set; } = Guid.NewGuid();
    [DataMember] public DateTimeOffset? CreationTime { get; set; }
    [DataMember] public DateTimeOffset? Updated { get; set; }
    [DataMember, StringLength(36), ConcurrencyCheck] public string? ConcurrencyStamp { get; set; }
    [DataMember] public string Provider { get; set; } = "";
    [DataMember] public bool Enabled { get; set; } = true;
    [DataMember] public bool Pull { get; set; } = true;
    [DataMember] public bool Streaming { get; set; }
    [DataMember] public string ConfigurationJson { get; set; } = "{}";
    [DataMember] public string ScheduleJson { get; set; } = "{}";
    [DataMember] public string LimitsJson { get; set; } = "{}";
    [DataMember] public int DeliveryAttempts { get; set; } = 3;
    [DataMember] public int RetryDelaySeconds { get; set; } = 1;
}

[DataContract]
public sealed class GXDataSourceRoute : IUnique<Guid>, IGXEntityMetadata
{
    [DataMember] public Guid Id { get; set; } = Guid.NewGuid();
    [DataMember] public DateTimeOffset? CreationTime { get; set; }
    [DataMember] public DateTimeOffset? Updated { get; set; }
    [DataMember, StringLength(36), ConcurrencyCheck] public string? ConcurrencyStamp { get; set; }
    [DataMember, ForeignKey(typeof(GXDataSource), OnDelete = ForeignKeyDelete.Cascade)] public Guid DataSource { get; set; }
    [DataMember, ForeignKey(typeof(GXTransport))] public Guid Transport { get; set; }
    [DataMember] public int Position { get; set; }
}

[DataContract]
public sealed class GXDataSourceSecret : IUnique<Guid>, IGXEntityMetadata
{
    [DataMember] public Guid Id { get; set; } = Guid.NewGuid();
    [DataMember] public DateTimeOffset? CreationTime { get; set; }
    [DataMember] public DateTimeOffset? Updated { get; set; }
    [DataMember, StringLength(36), ConcurrencyCheck] public string? ConcurrencyStamp { get; set; }
    [DataMember, ForeignKey(typeof(GXDataSource), OnDelete = ForeignKeyDelete.Cascade)] public Guid DataSource { get; set; }
    [DataMember] public string Name { get; set; } = "";
    [DataMember] public byte[] Nonce { get; set; } = [];
    [DataMember] public byte[] Ciphertext { get; set; } = [];
    [DataMember] public byte[] Tag { get; set; } = [];
}
