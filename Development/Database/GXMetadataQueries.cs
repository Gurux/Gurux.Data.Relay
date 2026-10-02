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

using Gurux.Service.Orm;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Database;

/// <summary>Builds metadata selections while retaining the CLR result mapping.</summary>
public static class GXMetadataQueries
{
    public static System.Linq.Expressions.Expression<Func<GXColumnSchema, object>> JoinColumn(
        GXColumnSchema column, string alias)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(typeof(GXColumnSchema), "column");
        return System.Linq.Expressions.Expression.Lambda<Func<GXColumnSchema, object>>(
            System.Linq.Expressions.Expression.Convert(Column(column, alias), typeof(object)), parameter);
    }

    public static System.Linq.Expressions.Expression Column(GXColumnSchema column, string alias)
    {
        var table = column.Parent ?? throw new ArgumentException("Column requires a parent table.", nameof(column));
        int index = table.Columns.IndexOf(column);
        if (index < 0) throw new ArgumentException("Column is not in its parent table.", nameof(column));
        System.Linq.Expressions.Expression<Func<GXColumnSchema>> expression = () => GXSql.As(table, alias).Columns[index];
        return expression.Body;
    }

    public static GXSelectArgs Select(GXTableSchema schema, string alias)
    {
        var source = GXSelectArgs.Select(schema.Columns);
        return GXSelectArgs.From(() => GXSql.As(GXSql.Subquery<object>(source), alias));
    }

    public static GXSelectArgs Select<T>(GXDbConnection connection,
        params (string Column, object? Value)[] filters)
    {
        // Query construction must not execute schema commands on a connection
        // whose caller may already own a local transaction.
        var schema = new GXTableSchema { Name = connection.GetTableName<T>() };
        foreach (var name in GXSqlBuilder.GetFields<T>())
            schema.Columns.Add(new GXColumnSchema { Name = name, Parent = schema });
        var query = GXSelectArgs.SelectAll<T>();
        var values = filters.Select(filter => (
            Column: schema.Columns.Single(column => string.Equals(column.Name, filter.Column, StringComparison.OrdinalIgnoreCase)),
            Value: filter.Value)).ToArray();
        query.Where.FilterBy(values.AsEnumerable());
        foreach (var filter in values.Where(filter => filter.Value == null))
        {
            var column = filter.Column;
            query.Where.And<GXColumnSchema>(_ => column == null);
        }
        return query;
    }
}
