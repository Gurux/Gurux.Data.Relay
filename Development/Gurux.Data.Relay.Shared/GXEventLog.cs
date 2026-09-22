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

using Microsoft.Extensions.Logging;
using Gurux.Service.Orm.Common;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;
using System.Runtime.Serialization;
using System.ComponentModel.DataAnnotations;

namespace Gurux.Data.Relay.Log;

public sealed class GXEventLog : IUnique<long>, IGXEntityMetadata
{

    [AutoIncrement]
    public long Id { get; set; }

    [DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [DataMember]
    public DateTimeOffset? Updated { get; set; }

    [DataMember]
    [StringLength(36)]
    [ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    [DataMember, System.Text.Json.Serialization.JsonIgnore,
     ForeignKey(typeof(GXSettings), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid Settings { get; set; }
    /// <summary>
    /// Timestamp of the event log entry.
    /// </summary>
    [Filter(FilterType.GreaterOrEqual)]
    public DateTime? Timestamp { get; set; }

    /// <summary>
    /// Trace level of the event log entry.
    /// </summary>
    public LogLevel Level { get; set; }

    /// <summary>
    /// Application mode of the event log entry.
    /// </summary>
    public ApplicationMode Mode { get; set; }

    /// <summary>Database index within the mode's configuration; null for general events.</summary>
    public int? DatabaseIndex { get; set; }

    /// <summary>
    /// Source of the event log entry.
    /// </summary>
    [Filter(FilterType.Contains)]
    public string? Source { get; set; }
    /// <summary>
    /// Message of the event log entry.
    /// </summary>
    [Filter(FilterType.Contains)]
    public string? Message { get; set; }
    /// <summary>
    /// Gets or sets the associated data.
    /// </summary>
    [Filter(FilterType.Contains)]
    public string? Data { get; set; }
}

