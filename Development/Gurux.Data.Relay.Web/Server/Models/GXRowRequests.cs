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
using System.Text.Json;

namespace Gurux.Data.Relay.Web.Server.Models;

/// <summary>Values for one new row. Omitted columns use database defaults.</summary>
public sealed class GXInsertRowRequest
{
    /// <summary>Column names and scalar JSON values. Encode binary values as Base64 strings.</summary>
    [Required, MinLength(1)]
    public Dictionary<string, JsonElement> Values { get; set; } = [];
}

/// <summary>The complete primary key of one row.</summary>
public class GXRowKeyRequest
{
    /// <summary>Every primary-key column and its value, including all parts of a composite key.</summary>
    [Required, MinLength(1)]
    public Dictionary<string, JsonElement> Keys { get; set; } = [];
}

/// <summary>Changes to the non-key columns of one existing row.</summary>
public sealed class GXUpdateRowRequest : GXRowKeyRequest
{
    /// <summary>Only columns to change; omitted columns retain their existing values.</summary>
    [Required, MinLength(1)]
    public Dictionary<string, JsonElement> Values { get; set; } = [];
}

/// <summary>The number of rows changed by a successful request.</summary>
public sealed record GXRowWriteResult(int AffectedRows);
