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

namespace Gurux.Data.Relay.Shared.Mcp;

/// <summary>
/// Explicit bindings from an existing Stage mapping to an existing target.
/// </summary>
public sealed class GXDataVaultMcpMappingRequest
{
    public Guid Database { get; set; }
    /// <summary>Creates the target table from the generated provider-neutral schema when it is missing.</summary>
    public bool CreateIfMissing { get; set; }
    public string SourceTable { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public DataVaultObjectType ObjectType { get; set; }
    public List<GXDataVaultMcpColumnBinding> Columns { get; set; } = [];
}

public sealed class GXDataVaultMcpColumnBinding
{
    public string SourceColumn { get; set; } = string.Empty;
    public string TargetColumn { get; set; } = string.Empty;
    public DataVaultColumnRole Role { get; set; }
}

/// <summary>Contains no configuration secrets. Applied means the configuration save completed.</summary>
public sealed class GXDataVaultMcpApplyResult
{
    public bool Applied { get; set; }
    public List<GXDataVaultMcpError> Errors { get; set; } = [];
    public List<GXDataVaultMcpCreatedMapping> Mappings { get; set; } = [];
}

public sealed class GXDataVaultMcpCreatedMapping
{
    public Guid Id { get; set; }
    public string Table { get; set; } = string.Empty;
}
