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

using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Client;

/// <summary>Validates and encodes a generated hash for its physical destination column.</summary>
public static class GXDataVaultHashStorage
{
    public static bool IsHash(DataVaultColumnRole? role) => role is DataVaultColumnRole.HashKey or
        DataVaultColumnRole.ParentHashKey or DataVaultColumnRole.LinkHashKey or DataVaultColumnRole.HashDiff;

    public static void Validate(GXColumnSchema column, HashAlgorithmType algorithm)
    {
        int bytes = algorithm switch
        {
            HashAlgorithmType.SHA256 => 32,
            HashAlgorithmType.SHA512 => 64,
            HashAlgorithmType.MD5 => 16,
            _ => throw new ArgumentException($"Unsupported hash algorithm '{algorithm}'.")
        };
        bool binary = column.Type == typeof(byte[]);
        if (!binary && column.Type != typeof(string))
            throw new ArgumentException($"Hash column '{column.Name}' must be a binary or text column; actual type is {column.Type?.Name ?? column.DbType}.");
        int required = binary ? bytes : bytes * 2;
        if (column.MaxLength > 0 && column.MaxLength < required)
            throw new ArgumentException($"Hash column '{column.Name}' is too short for {algorithm}: requires {required} {(binary ? "bytes" : "characters")}, actual maximum is {column.MaxLength}.");
    }

    public static object Encode(string hex, GXColumnSchema column, HashAlgorithmType algorithm)
    {
        Validate(column, algorithm);
        return column.Type == typeof(byte[]) ? Convert.FromHexString(hex) : hex;
    }
}
