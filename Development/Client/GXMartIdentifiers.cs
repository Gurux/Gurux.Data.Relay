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
using System.Text;
using Gurux.Service.Orm.Model;

namespace Gurux.Data.Relay.Client;

internal static class GXMartIdentifiers
{
    public static string Canonical(string name) => string.Join('.', Parts(name));
    // Describe can return schema.table (or [schema].[table]) in Name with Schema unset.
    public static GXTableSchema Normalize(GXTableSchema source)
    {
        var parts = Parts(source.Name);
        if (parts.Count > 2) throw new ArgumentException("Cross-catalog mart tables are not supported.");
        var schema = new GXTableSchema
        {
            Name = parts[^1], Schema = parts.Count == 2 ? parts[0] : string.IsNullOrEmpty(source.Schema) ? null : Parts(source.Schema).Single(),
            Catalog = source.Catalog, Comment = source.Comment, TableType = source.TableType
        };
        schema.Columns.AddRange(source.Columns);
        return schema;
    }

    private static List<string> Parts(string value)
    {
        List<string> parts = [];
        var part = new StringBuilder();
        char closing = '\0';
        for (int i = 0; i < value.Length; ++i)
        {
            char c = value[i];
            if (closing != '\0')
            {
                if (c == closing)
                {
                    if (i + 1 < value.Length && value[i + 1] == closing) { part.Append(c); ++i; }
                    else closing = '\0';
                }
                else part.Append(c);
            }
            else if (c is '[' or '"' or '`') closing = c == '[' ? ']' : c;
            else if (c == '.') { parts.Add(part.ToString()); part.Clear(); }
            else part.Append(c);
        }
        parts.Add(part.ToString());
        if (closing != '\0' || parts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Invalid qualified table name.");
        return parts;
    }
}
