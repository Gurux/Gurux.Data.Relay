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

using System.Text.Json.Serialization;
using Gurux.Service.Orm.Enums;
using Gurux.Data.Relay.Enums;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Common;

namespace Gurux.Data.Relay.Configuration;

/// <summary>
/// Database configuration class for GXDataRelay. 
/// This class contains properties that define the database connection, tables, 
/// mappings, transports, and other settings related to the database.
/// </summary>
public sealed class GXDatabaseConfiguration : IUnique<Guid>,
    IGXEntityMetadata
{

    public Guid Id { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [System.Runtime.Serialization.DataMember]
    public DateTimeOffset? Updated { get; set; }

    [System.Runtime.Serialization.DataMember]
    [System.ComponentModel.DataAnnotations.StringLength(36)]
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    /// <summary>
    /// Database description. This is used for display purposes only and does not affect the database connection or behavior.
    /// </summary>
    public string? Description { get; set; }
    [System.Runtime.Serialization.DataMember]
    [System.ComponentModel.DataAnnotations.StringLength(255)]
    public string? RecordSource { get; set; }

    /// <summary>
    /// Database type. This is used to determine the database provider and connection settings.
    /// </summary>
    public DatabaseType Type { get; set; }

    /// <summary>
    /// Database connection string. This is used to connect to the database and must be valid for the specified database type.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Database tables configuration. This is used to define the tables that will be created or managed in the database. If null, no tables will be created or managed.
    /// </summary>
    public List<GXTableConfiguration>? Tables { get; set; }

    /// <summary>
    /// Database table mappings configuration. 
    /// This is used to define the mappings between source and target tables for data transfer.
    /// If null, no mappings will be defined.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GXDataVaultTableMapping>? Mappings { get; set; }

    /// <summary>
    /// Transient transports used by the configuration editor.
    /// </summary>
    [JsonIgnore]
    public List<GXTransportConfiguration> Transports { get; set; } = [];


    /// <summary>
    /// Delete mode for the database. This is used to determine how records are deleted from the database.
    /// </summary>
    public DeleteMode DeleteMode { get; set; } = DeleteMode.PhysicalDelete;

    /// <summary>
    /// The name of the column that indicates whether a record has been deleted. This is used when the delete mode is set to logical delete.
    /// </summary>
    public string? DeletedColumn { get; set; }

    public override string? ToString()
    {
        if (!string.IsNullOrEmpty(Description))
        {
            return $"{Type} ({Description})";
        }
        return base.ToString();
    }
}




