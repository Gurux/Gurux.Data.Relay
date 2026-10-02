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
using Gurux.Data.Relay.Shared;
using Mapping = Gurux.Data.Relay.Configuration.GXDataVaultTableMapping;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Web.Server.Services;

public static class GXVaultTableMappingBuilder
{
    public static Mapping Build(GXCreateVaultTableRequest request, GXTableSchema target, Mapping? staging)
    {
        var mapping = new Mapping { SourceTable = staging?.SourceTable ?? new() { Id = Guid.Empty, Name = request.SourceTable }, TargetTable = new() { Id = Guid.Empty, Name = target.ToString() }, ObjectType = request.ObjectType };
        string Resolve(string source) => staging?.Columns.FirstOrDefault(c => string.Equals(c.TargetColumn, source, StringComparison.OrdinalIgnoreCase))?.SourceColumn ?? source;
        void Add(string source, string destination, DataVaultColumnRole role, bool resolved = false) => mapping.Columns.Add(new()
        { SourceColumn = Resolve(source), TargetColumn = resolved ? destination : request.ResolveTargetColumn(destination), Role = role });
        switch (request.ObjectType)
        {
            case DataVaultObjectType.Hub:
                Add(request.BusinessKeyColumn!, target.Columns.Single(c => c.IsPrimaryKey).Name, DataVaultColumnRole.HashKey, resolved: true);
                string businessKey = string.IsNullOrWhiteSpace(request.TargetBusinessKeyColumn)
                    ? request.BusinessKeyColumn + "_BK" : request.TargetBusinessKeyColumn;
                Add(request.BusinessKeyColumn!, businessKey, DataVaultColumnRole.BusinessKey);
                break;
            case DataVaultObjectType.Link:
                Add(request.Parents[0].SourceColumn, target.Columns.Single(c => c.IsPrimaryKey).Name, 
                    DataVaultColumnRole.LinkHashKey, resolved: true);
                goto case DataVaultObjectType.Satellite;
            case DataVaultObjectType.Satellite:
                foreach (var parent in request.Parents)
                {
                    Add(parent.SourceColumn, parent.Column, DataVaultColumnRole.ParentHashKey);
                }
                if (request.ObjectType == DataVaultObjectType.Satellite) Add(request.Columns[0], "HASH_DIFF", DataVaultColumnRole.HashDiff);
                break;
            case DataVaultObjectType.Reference:
                Add(request.BusinessKeyColumn!, request.BusinessKeyColumn!, DataVaultColumnRole.BusinessKey);
                break;
        }
        if (request.ObjectType is DataVaultObjectType.Satellite or DataVaultObjectType.Reference)
            foreach (var column in request.Columns) Add(column, column, DataVaultColumnRole.Attribute);
        foreach (var role in new[] { DataVaultColumnRole.LoadDate, DataVaultColumnRole.RecordSource })
        {
            string name = role == DataVaultColumnRole.LoadDate ? "LOAD_DATE" : "RECORD_SOURCE";
            var metadata = staging?.Columns.FirstOrDefault(c => c.Role == role);
            mapping.Columns.Add(new() { SourceColumn = metadata?.SourceColumn ?? name, TargetColumn = request.ResolveTargetColumn(name), Role = role });
        }
        return mapping;
    }
}
