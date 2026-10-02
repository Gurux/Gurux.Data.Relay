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

using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Common;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXTransportConfiguration : IUnique<Guid>,
    IGXEntityMetadata
{

    public Guid Id { get; set; } = Guid.NewGuid();

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? Updated { get; set; }

    [System.Runtime.Serialization.DataMember]
    [System.ComponentModel.DataAnnotations.StringLength(36)]
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }
    public Guid? Database { get; set; }

    public List<GXTransportTableConfiguration> Tables { get; set; } = [];


    [JsonIgnore]
    public int? DatabaseIndex { get; set; }

    public string? Description { get; set; }

    public TransportType Type { get; set; }

    public GXTransferConfiguration? Transfer { get; set; }

    public int MaximumMessageSize { get; set; } = 4 * 1024 * 1024;

    [DefaultValue(10)]
    public int ConnectTimeoutSeconds { get; set; } = 10;

    public int AcknowledgementTimeoutSeconds { get; set; } = 30;

    public string? Host { get; set; }

    public int Port { get; set; }

    public string? Broker { get; set; }

    public string? Topic { get; set; }

    public string? AcknowledgementTopic { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool UseTls { get; set; }

    public override string? ToString()
    {
        if (!string.IsNullOrEmpty(Description))
        {
            return $"{Type} ({Description})";
        }
        return base.ToString();
    }
}


