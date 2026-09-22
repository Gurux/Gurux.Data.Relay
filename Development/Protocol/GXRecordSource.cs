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
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Shared.Protocol;

internal static class GXRecordSource
{
    public const string ColumnName = "RECORD_SOURCE";

    public static string? Resolve(GXDatabaseConfiguration database, GXTableConfiguration table)
    {
        string? source = string.IsNullOrWhiteSpace(table.RecordSource) ? database.RecordSource : table.RecordSource;
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (source.Length > 255) throw new InvalidOperationException("Record source must not exceed 255 characters.");
        return source;
    }

    public static void Prepare(GXDataMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.RecordSource)) return;
        if (message.RecordSource.Length > 255) throw new InvalidOperationException("Record source must not exceed 255 characters.");
        AddColumn(message.Table);
        if (message.Schema is not null) AddColumn(message.Schema);
        foreach (var change in message.Changes.Where(c => c.Operation is DataOperation.Insert or DataOperation.Update))
        {
            if (change.Values is null) continue;
            string key = change.Values.Keys.FirstOrDefault(k => string.Equals(k, ColumnName, StringComparison.OrdinalIgnoreCase)) ?? ColumnName;
            change.Values[key] = JsonSerializer.SerializeToElement(message.RecordSource);
        }
    }

    private static void AddColumn(GXTableSchema schema)
    {
        if (!schema.Columns.Any(c => string.Equals(c.Name, ColumnName, StringComparison.OrdinalIgnoreCase)))
            schema.Columns.Add(new GXColumnSchema { Name = ColumnName, Type = typeof(string), MaxLength = 255, IsNullable = true, Parent = schema });
    }
}
