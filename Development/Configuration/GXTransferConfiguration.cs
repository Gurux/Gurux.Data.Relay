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

namespace Gurux.Data.Relay.Configuration;

public sealed class GXTransferConfiguration : Gurux.Service.Orm.Common.IUnique<Guid>,
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

    public int BatchSize { get; set; } = 1000;

    public int RetryCount { get; set; } = 5;

    public int RetryDelaySeconds { get; set; } = 10;

    public int PingAfterSeconds { get; set; } = 300;

    public bool AllowConcurrentRuns { get; set; }
}

