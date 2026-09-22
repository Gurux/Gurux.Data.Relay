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

using System.Runtime.Serialization;
using Gurux.Service.Orm.Common;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Configuration;
using Gurux.Service.Orm.Common.Enums;
using System.ComponentModel.DataAnnotations;

namespace Gurux.Data.Relay.Shared;

/// <summary>
/// Database settings exchanged with the administration API.
/// </summary>
[DataContract]
public sealed class GXDatabase : IUnique<Guid>, IGXEntityMetadata
{
    [DataMember]
    public Guid Id { get; set; } = Guid.NewGuid();

    [DataMember]
    public DateTimeOffset? CreationTime { get; set; }

    [DataMember]
    public DateTimeOffset? Updated { get; set; }

    [DataMember]
    [StringLength(36)]
    [ConcurrencyCheck]
    public string? ConcurrencyStamp { get; set; }

    /// <summary>
    /// Database type defines used database.
    /// </summary>
    [DataMember]
    public DatabaseType? Type { get; set; }

    /// <summary>
    /// Gets or sets whether the connection test was successful.
    /// </summary>
    [JsonIgnore]
    public bool IsConnectionSuccessful { get; set; } = true;

    /// <summary>
    /// Connection test error. 
    /// If the connection test fails, this property contains the error message.
    /// It's not serialized to JSON, as it's only used for UI.
    /// </summary>
    [JsonIgnore]
    public string? Error { get; set; }

    /// <summary>
    /// Database Description.
    /// </summary>
    [DataMember]
    public string? Description { get; set; }
    [DataMember]
    [StringLength(255)]
    public string? RecordSource { get; set; }

    /// <summary>
    /// Connection string used to connect to the database.
    /// </summary>
    [DataMember]
    [IsRequired]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// List of tables in the database. This property is optional and may be null if the tables are not loaded.
    /// </summary>
    public List<GXTable>? Tables { get; set; }

    /// <summary>
    /// List of table mappings for the database. This property is optional and may be null if the mappings are not loaded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GXDataVaultTableMapping>? Mappings { get; set; }

    [DataMember]
    public DeleteMode DeleteMode { get; set; } = DeleteMode.PhysicalDelete;

    [DataMember]
    public string? DeletedColumn { get; set; }
}





