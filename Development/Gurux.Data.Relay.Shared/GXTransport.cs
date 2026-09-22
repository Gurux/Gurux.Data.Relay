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
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common;
using Gurux.Service.Orm.Common.Enums;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Gurux.Data.Relay.Shared;

/// <summary>
/// Transport settings exchanged with the administration API.
/// </summary>
[DataContract]
public sealed class GXTransport : IUnique<Guid>, IGXEntityMetadata
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

    [DataMember(IsRequired = true),
        ForeignKey(typeof(GXSettings), OnDelete = ForeignKeyDelete.Cascade)]
    [JsonIgnore]
    public Guid Settings { get; set; }

    public List<GXTransportTable> Tables { get; set; } = [];

    [DataMember]
    public string? Description { get; set; }

    /// <summary>
    /// Transport type used for data transfer. TCP or MQTT.
    /// </summary>
    [DataMember]
    public TransportType Type { get; set; }

    public GXTransfer? Transfer { get; set; }

    [JsonIgnore, DataMember]
    public Guid? TransferId { get; set; }

    [DataMember]
    public int MaximumMessageSize { get; set; } = 4 * 1024 * 1024;
    [Range(1, 4194304, ErrorMessage = "Maximum message size must be between 1 and 4194304.")]
    [DataMember]
    public int ConnectTimeoutSeconds { get; set; } = 10;
    [DataMember]
    public int AcknowledgementTimeoutSeconds { get; set; } = 30;
    [DataMember]
    public string? Host { get; set; }

    [DataMember]
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    public int Port { get; set; }
    [DataMember]
    public string? Broker { get; set; }
    [DataMember]
    public string? Topic { get; set; }
    [DataMember]
    public string? AcknowledgementTopic { get; set; }
    [DataMember]
    public string? Username { get; set; }
    [DataMember]
    public string? Password { get; set; }
    [DataMember]
    public bool UseTls { get; set; }

    /// <summary>
    /// Database used by this transport, or null when it serves multiple databases.
    /// </summary>
    [DataMember, ForeignKey(typeof(GXDatabase))]
    public Guid? Database { get; set; }
}




