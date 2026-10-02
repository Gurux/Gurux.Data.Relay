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
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Web.Server.Services;

public static class GXVaultTableSchemaBuilder
{
    public static GXTableSchema Build(GXCreateVaultTableRequest request, GXTableSchema source, Func<string, GXTableSchema> describe,
        Func<string, DataVaultObjectType?>? parentType = null)
    {
        if (request.TargetColumns is null || request.TargetColumns.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.TargetColumns.Count)
            throw new ArgumentException("Target column selections must have distinct column names.");
        var logicalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var target = new GXTableSchema { Name = request.TargetTable, Schema = source.Schema };
        GXColumnSchema SourceColumn(string? name) => source.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Select a valid source column.");
        void Add(GXColumnSchema column)
        {
            if (!logicalNames.Add(column.Name)) throw new ArgumentException("Source column roles must have distinct names.");
            column.Name = request.ResolveTargetColumn(column.Name);
            if (string.IsNullOrWhiteSpace(column.Name) || target.Columns.Any(c => string.Equals(c.Name, column.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Column names must be nonempty and distinct, including generated columns.");
            target.Columns.Add(column);
        }
        GXColumnSchema Copy(GXColumnSchema column, string name, bool key = false) => new()
        {
            Name = name,
            Type = column.Type,
            DbType = column.DbType,
            MaxLength = column.MaxLength,
            Precision = column.Precision,
            Scale = column.Scale,
            DateTimePrecision = column.DateTimePrecision,
            IsNullable = key ? false : column.IsNullable,
            IsPrimaryKey = key
        };
        GXColumnSchema Hash(string name, bool key = false) => new() { Name = name, Type = typeof(string), MaxLength = 128, IsNullable = false, IsPrimaryKey = key };
        bool satellite = request.ObjectType == DataVaultObjectType.Satellite;
        switch (request.ObjectType)
        {
            case DataVaultObjectType.Hub:
                var businessKey = SourceColumn(request.BusinessKeyColumn);
                var bk = Copy(businessKey, string.IsNullOrWhiteSpace(request.TargetBusinessKeyColumn)
                    ? businessKey.Name + "_BK" : request.TargetBusinessKeyColumn);
                bk.IsNullable = false;
                Add(bk);
                Add(new() { Name = "HK_" + businessKey.Name, Type = typeof(byte[]), MaxLength = 32, IsNullable = false, IsPrimaryKey = true });
                break;
            case DataVaultObjectType.Link:
            case DataVaultObjectType.Satellite:
                if (satellite ? request.Parents.Count != 1 : request.Parents.Count < 2)
                    throw new ArgumentException(satellite ? "Select one parent Hub or Link." : "Select at least two Hub references.");
                if (!satellite) Add(Hash("HK_" + (request.TargetTable.StartsWith("LNK_", StringComparison.OrdinalIgnoreCase) ? request.TargetTable[4..] : request.TargetTable), true));
                foreach (var parent in request.Parents)
                {
                    SourceColumn(parent.SourceColumn);
                    var schema = describe(parent.Table);
                    var mappedType = parentType?.Invoke(schema.ToString()) ?? parentType?.Invoke(parent.Table);
                    bool hub = mappedType == DataVaultObjectType.Hub || mappedType is null && schema.Name.StartsWith("HUB_", StringComparison.OrdinalIgnoreCase);
                    bool link = mappedType == DataVaultObjectType.Link || mappedType is null &&
                        (schema.Name.StartsWith("LNK_", StringComparison.OrdinalIgnoreCase) || schema.Name.StartsWith("LINK_", StringComparison.OrdinalIgnoreCase));
                    if (!hub && !(satellite && link))
                        throw new ArgumentException("Select a parent Hub (or a Link for a Satellite).");
                    var keys = schema.Columns.Where(c => c.IsPrimaryKey).ToArray();
                    if (keys.Length != 1)
                        throw new ArgumentException("The parent must have one hash primary key.");
                    if (!System.Text.RegularExpressions.Regex.IsMatch(parent.Column ?? "", @"^HK_[A-Za-z0-9_]+$"))
                        throw new ArgumentException("Name each parent reference HK_<role>, using letters, digits and underscores.");
                    var key = Copy(keys[0], parent.Column!, satellite);
                    key.IsNullable = false;
                    Add(key);
                }
                if (satellite) Add(Hash("HASH_DIFF"));
                break;
            case DataVaultObjectType.Reference:
                var code = SourceColumn(request.BusinessKeyColumn);
                Add(Copy(code, code.Name, true));
                break;
            default:
                throw new ArgumentException("Unsupported Data Vault table type.");
        }
        Add(new() { Name = "LOAD_DATE", Type = typeof(DateTimeOffset), IsNullable = false, IsPrimaryKey = satellite });
        Add(new() { Name = "RECORD_SOURCE", Type = typeof(string), MaxLength = 255, IsNullable = false });
        if (satellite || request.ObjectType == DataVaultObjectType.Reference)
        {
            if (request.Columns.Count == 0) throw new ArgumentException("Select at least one descriptive column.");
            foreach (var name in request.Columns) Add(Copy(SourceColumn(name), name));
        }
        if (request.TargetColumns.Any(p => !logicalNames.Contains(p.Key) || string.IsNullOrWhiteSpace(p.Value)))
            throw new ArgumentException("Target column selections contain an unknown column.");
        return target;
    }
}
