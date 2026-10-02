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

public sealed class GXCreateVaultTableRequest
{
    public DataVaultObjectType ObjectType { get; set; }
    public bool AddMapping { get; set; } = true;
    public bool UseExistingTable { get; set; }
    public bool CreateIfMissing { get; set; } = true;
    /// <summary>Drop an existing target table, including its data, and recreate it. Defaults to false.</summary>
    public bool Overwrite { get; set; }
    public string? BusinessKeyColumn { get; set; }
    public string? TargetBusinessKeyColumn { get; set; }
    /// <summary>Maps generated column names to selected columns in an existing target table.</summary>
    public Dictionary<string, string> TargetColumns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string SourceTable { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = [];
    public List<GXVaultParentSelection> Parents { get; set; } = [];

    public string ResolveTargetColumn(string name)
        => TargetColumns.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value ?? name;
}

public sealed class GXVaultParentSelection
{
    public string Table { get; set; } = string.Empty;
    public string SourceColumn { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
}

public sealed class GXCreateVaultTableResult
{
    public bool RequiresCreation { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string? MappingError { get; set; }
}
