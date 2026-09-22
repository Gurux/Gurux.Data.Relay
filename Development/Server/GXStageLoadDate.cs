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

using System.Text.Json;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Shared.Server;

internal static class GXStageLoadDate
{
    public const string ColumnName = "LOAD_DATE";

    public static void AddColumn(GXTableSchema schema)
    {
        if (!schema.Columns.Any(c => string.Equals(c.Name, ColumnName, StringComparison.OrdinalIgnoreCase)))
            schema.Columns.Add(new GXColumnSchema { Name = ColumnName, Type = typeof(DateTimeOffset), IsNullable = true, Parent = schema });
    }

    public static void Apply(GXDataMessage message, GXTableSchema schema, DateTimeOffset localTime)
    {
        var column = schema.Columns.FirstOrDefault(c => string.Equals(c.Name, ColumnName, StringComparison.OrdinalIgnoreCase));
        if (column is null) return;
        JsonElement timestamp = column.Type == typeof(DateTime)
            ? JsonSerializer.SerializeToElement(localTime.DateTime)
            : JsonSerializer.SerializeToElement(localTime);
        foreach (var change in message.Changes.Where(c => c.Operation is DataOperation.Insert or DataOperation.Update))
        {
            if (change.Values is null || change.Values.Count == 0) continue;
            string key = change.Values.Keys.FirstOrDefault(k => string.Equals(k, column.Name, StringComparison.OrdinalIgnoreCase)) ?? column.Name;
            change.Values[key] = timestamp;
        }
    }
}
