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

using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Web.Client.Services;

/// <summary>
/// Provides schema queries used by the table description view.
/// </summary>
public static class GXTableSchemaExtensions
{
    /// <summary>
    /// Determines whether a column belongs to the primary key or an index.
    /// </summary>
    /// <param name="schema">The table schema containing the indexes.</param>
    /// <param name="column">The column to check.</param>
    /// <returns>Whether the column is a primary key or appears in an index.</returns>
    public static bool IsIndexed(this GXTableSchema schema, GXColumnSchema column) =>
        column.IsPrimaryKey || schema.Indexes.Any(index => index.Columns.Any(item =>
            string.Equals(item.Name, column.Name, StringComparison.OrdinalIgnoreCase)));
}
