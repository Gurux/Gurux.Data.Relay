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
using Gurux.Data.Relay.Configuration;
using Gurux.Service.Orm.Common;

namespace Gurux.Data.Relay.Shared;

/// <summary>An association between configuration entities.</summary>
public interface IGXConfigurationReference : IUnique<Guid>
{
    Guid ConfigurationId { get; set; }
    Guid OwnerId { get; set; }
    Guid TargetId { get; set; }
}
public interface IGXOrderedConfigurationReference : IGXConfigurationReference
{
    int Position { get; set; }
}
[DataContract]
public sealed class GXSettingsDatabaseReference : IGXOrderedConfigurationReference, IGXEntityMetadata
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

    [DataMember, ForeignKey(typeof(GXSettings))]
    public Guid ConfigurationId { get; set; }

    [DataMember(IsRequired = true), ForeignKey(typeof(GXSettings), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid OwnerId { get; set; }

    [DataMember, ForeignKey(typeof(GXDatabase))]
    public Guid TargetId { get; set; }

    [DataMember]
    public int Position { get; set; }
}

[DataContract]
public sealed class GXSettingsTransportReference : IGXConfigurationReference, IGXEntityMetadata
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

    [DataMember, ForeignKey(typeof(GXSettings))]
    public Guid ConfigurationId { get; set; }

    [DataMember(IsRequired = true), ForeignKey(typeof(GXSettings), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid OwnerId { get; set; }

    [DataMember, ForeignKey(typeof(GXTransport))]
    public Guid TargetId { get; set; }

}

[DataContract]
public sealed class GXDatabaseTableReference : IGXConfigurationReference, IGXEntityMetadata
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

    [DataMember, ForeignKey(typeof(GXSettings))]
    public Guid ConfigurationId { get; set; }

    [DataMember(IsRequired = true), ForeignKey(typeof(GXDatabase), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid OwnerId { get; set; }

    [DataMember, ForeignKey(typeof(GXTable))]
    public Guid TargetId { get; set; }

}

[DataContract]
public sealed class GXDatabaseMappingReference : IGXOrderedConfigurationReference, IGXEntityMetadata
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

    [DataMember, ForeignKey(typeof(GXSettings))]
    public Guid ConfigurationId { get; set; }

    [DataMember(IsRequired = true), ForeignKey(typeof(GXDatabase), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid OwnerId { get; set; }

    [DataMember, ForeignKey(typeof(GXDataVaultTableMapping))]
    public Guid TargetId { get; set; }

    [DataMember]
    public int Position { get; set; }
}

[DataContract]
public sealed class GXTransportTableReference : IGXConfigurationReference, IGXEntityMetadata
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

    [DataMember, ForeignKey(typeof(GXSettings))]
    public Guid ConfigurationId { get; set; }

    [DataMember(IsRequired = true), ForeignKey(typeof(GXTransport), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid OwnerId { get; set; }

    [DataMember, ForeignKey(typeof(GXTransportTable))]
    public Guid TargetId { get; set; }

}

[DataContract]
public sealed class GXMappingColumnReference : IGXOrderedConfigurationReference, IGXEntityMetadata
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

    [DataMember, ForeignKey(typeof(GXSettings))]
    public Guid ConfigurationId { get; set; }

    [DataMember(IsRequired = true), ForeignKey(typeof(GXDataVaultTableMapping), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid OwnerId { get; set; }

    [DataMember, ForeignKey(typeof(GXDataVaultColumnMapping))]
    public Guid TargetId { get; set; }

    [DataMember]
    public int Position { get; set; }
}

[DataContract]
public sealed class GXSettingsMappingReference : IGXOrderedConfigurationReference, IGXEntityMetadata
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

    [DataMember, ForeignKey(typeof(GXSettings))]
    public Guid ConfigurationId { get; set; }

    [DataMember(IsRequired = true), ForeignKey(typeof(GXSettings), OnDelete = Gurux.Service.Orm.Common.Enums.ForeignKeyDelete.Cascade)]
    public Guid OwnerId { get; set; }

    [DataMember, ForeignKey(typeof(GXDataVaultTableMapping))]
    public Guid TargetId { get; set; }

    [DataMember]
    public int Position { get; set; }
}



