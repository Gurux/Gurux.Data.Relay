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

/// <summary>Safe database identity exposed to Data Vault MCP clients.</summary>
public sealed class GXDataVaultMcpDatabase
{
    /// <summary>Gets or sets the configured database identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the user-facing database description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the provider name.</summary>
    public string Provider { get; set; } = string.Empty;
}

/// <summary>Safe table identity exposed to Data Vault MCP clients.</summary>
public sealed class GXDataVaultMcpTable
{
    /// <summary>Gets or sets the physical table name.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>Safe physical table schema exposed to MCP clients.</summary>
public sealed class GXDataVaultMcpSchema
{
    /// <summary>Gets or sets the table name.</summary>
    public string Table { get; set; } = string.Empty;

    /// <summary>Gets the columns in physical order.</summary>
    public List<GXDataVaultMcpColumn> Columns { get; } = [];

    /// <summary>Gets the mapped primary-key column names.</summary>
    public List<string> PrimaryKey { get; } = [];
}

/// <summary>Safe source column metadata exposed to MCP clients.</summary>
public sealed class GXDataVaultMcpColumn
{
    /// <summary>Gets or sets the physical column name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the CLR data type name.</summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the column accepts null.</summary>
    public bool Nullable { get; set; }

    /// <summary>Gets or sets whether the provider generates the value.</summary>
    public bool Identity { get; set; }

    /// <summary>Gets or sets the maximum string/binary length when available.</summary>
    public long? MaxLength { get; set; }

    /// <summary>Gets or sets the numeric precision when available.</summary>
    public int? Precision { get; set; }

    /// <summary>Gets or sets the numeric scale when available.</summary>
    public int? Scale { get; set; }

    /// <summary>Gets or sets the provider default expression when available.</summary>
    public string? DefaultValue { get; set; }
}

/// <summary>Bounded, security-filtered source data sample.</summary>
public sealed class GXDataVaultMcpSample
{
    /// <summary>Gets the returned column names.</summary>
    public List<string> Columns { get; } = [];

    /// <summary>Gets the security-filtered rows.</summary>
    public List<Dictionary<string, object?>> Rows { get; } = [];
}

/// <summary>Inexpensive table-level profiling metadata.</summary>
public sealed class GXDataVaultMcpTableStatistics
{
    /// <summary>Gets or sets the physical table name.</summary>
    public string Table { get; set; } = string.Empty;

    /// <summary>Gets or sets the row count.</summary>
    public int RowCount { get; set; }

    /// <summary>Gets or sets the number of physical columns.</summary>
    public int ColumnCount { get; set; }
}

/// <summary>Structured Data Vault MCP validation issue.</summary>
public sealed class GXDataVaultMcpError
{
    /// <summary>Gets or sets the stable machine-readable error code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the affected object name.</summary>
    public string? Object { get; set; }

    /// <summary>Gets or sets the human-readable explanation.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Structured validation result with separate errors and warnings.</summary>
public sealed class GXDataVaultMcpValidationResult
{
    /// <summary>Gets or sets whether the supplied model is valid.</summary>
    public bool Valid { get; set; }

    /// <summary>Gets the validation errors.</summary>
    public List<GXDataVaultMcpError> Errors { get; } = [];

    /// <summary>Gets the non-blocking validation warnings.</summary>
    public List<GXDataVaultMcpError> Warnings { get; } = [];
}
