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

namespace Gurux.Data.Relay.Shared;

public sealed class GXInformationMartDefinition
{
    public Guid HubMappingId { get; set; }
    public GXMartGrain Grain { get; set; } = GXMartGrain.Hub;
    public string SourceReference { get; set; } = string.Empty;
    public string SourceLink { get; set; } = string.Empty;
    public List<GXMartColumnSelection> Columns { get; set; } = [];
    public List<GXMartReferenceJoin> ReferenceJoins { get; set; } = [];
}

public enum GXMartGrain
{
    Hub,
    Link,
    Reference,
    Custom
}

public sealed class GXMartReferenceJoin
{
    public Guid FromMappingId { get; set; }
    public string FromColumn { get; set; } = string.Empty;
    public Guid ReferenceMappingId { get; set; }
    public string ReferenceColumn { get; set; } = string.Empty;
}

public sealed class GXMartColumnSelection
{
    public Guid MappingId { get; set; }
    public string Column { get; set; } = string.Empty;
    public string TargetColumn { get; set; } = string.Empty;
    public Aggregation Aggregation { get; set; }
    /// <summary>When false, a required hash key is used for grain/joining but omitted from Mart output.</summary>
    public bool IncludeInOutput { get; set; }
}

public sealed class GXMartPreview
{
    public List<GXMartHubOption> Hubs { get; set; } = [];
    public Guid HubMappingId { get; set; }
    public List<GXMartColumnOption> Columns { get; set; } = [];
    public List<string> Messages { get; set; } = [];
}

public sealed class GXMartHubOption
{
    public Guid MappingId { get; set; }
    public string Table { get; set; } = string.Empty;
}

public sealed class GXMartColumnOption
{
    public DataVaultObjectType ObjectType { get; set; }
    public Guid MappingId { get; set; }
    public string Table { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
    public string TargetColumn { get; set; } = string.Empty;
    public bool IsKey { get; set; }
    public bool RequiresAggregation { get; set; }
    public string JoinDescription { get; set; } = string.Empty;
}

public sealed class GXCreateMartRequest
{
    public bool UseExistingTable { get; set; }
    public GXSchedule Schedule { get; set; } = new();
    public string SourceTable { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public GXInformationMartDefinition Definition { get; set; } = new();
}

