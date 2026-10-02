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
namespace Gurux.Data.Relay.Shared.Mcp;

/// <summary>An explicit Data Vault model proposed for one configured database.</summary>
public sealed class GXDataVaultModel
{
    public Guid Database { get; set; }
    /// <summary>Allows the apply operation to create missing target tables from generated schemas.</summary>
    public bool CreateIfMissing { get; set; }
    public List<GXDataVaultHubDefinition> Hubs { get; set; } = [];
    public List<GXDataVaultLinkDefinition> Links { get; set; } = [];
    public List<GXDataVaultSatelliteDefinition> Satellites { get; set; } = [];
    public List<GXDataVaultReferenceDefinition> References { get; set; } = [];
    public List<GXDataVaultMartDefinition> Marts { get; set; } = [];
}

/// <summary>A source-qualified column reference.</summary>
public sealed class GXDataVaultSourceColumn
{
    public string Table { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
}

/// <summary>A Hub and its ordered Business Keys.</summary>
public sealed class GXDataVaultHubDefinition
{
    public Dictionary<string, string> TargetColumns { get; set; } = [];
    public string Name { get; set; } = string.Empty;
    public List<GXDataVaultSourceColumn> BusinessKeys { get; set; } = [];
}

/// <summary>A Link and its participating Hubs.</summary>
public sealed class GXDataVaultLinkDefinition
{
    public Dictionary<string, string> TargetColumns { get; set; } = [];
    public string Name { get; set; } = string.Empty;
    public List<string> Hubs { get; set; } = [];
}

/// <summary>A Satellite and its parent Raw Data Vault object.</summary>
public sealed class GXDataVaultSatelliteDefinition
{
    public Dictionary<string, string> TargetColumns { get; set; } = [];
    public string Name { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;
    public List<GXDataVaultSourceColumn> Attributes { get; set; } = [];
}

/// <summary>A reference table with one source-qualified business key.</summary>
public sealed class GXDataVaultReferenceDefinition
{
    public Dictionary<string, string> TargetColumns { get; set; } = [];
    public string Name { get; set; } = string.Empty;
    public GXDataVaultSourceColumn BusinessKey { get; set; } = new();
    public List<GXDataVaultSourceColumn> Attributes { get; set; } = [];
}

/// <summary>An Information Mart rooted at an explicitly selected Hub.</summary>
public sealed class GXDataVaultMartDefinition
{
    public string Name { get; set; } = string.Empty;
    public string SourceTable { get; set; } = string.Empty;
    public string SourceHub { get; set; } = string.Empty;
    public Dictionary<string, string> TargetColumns { get; set; } = [];
    public List<string> Columns { get; set; } = [];
    public List<GXMartReferenceJoin> ReferenceJoins { get; set; } = [];
}
