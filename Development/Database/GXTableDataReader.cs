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

using E = System.Linq.Expressions.Expression;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Common.Model;
using System.Text.Json;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Database;

/// <summary>Reads a bounded page from an existing database table.</summary>
public sealed class GXTableDataReader(IGXDatabaseConnectionFactory connections, IGXDatabaseMetadataService metadata)
{
    public async Task<GXTableData> ReadAsync(GXDatabaseConfiguration database, string tableName,
        int startIndex, int count, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? filters = null)
    {       
        if (startIndex < 0 || count < 1 || count > 1000)
            throw new ArgumentOutOfRangeException(nameof(count), "Use a nonnegative offset and 1–1000 rows per page.");
        var tables = await metadata.GetTableNamesAsync(database, cancellationToken);
        if (!tables.Contains(tableName, StringComparer.Ordinal))
            throw new ArgumentException("The selected table does not exist.", nameof(tableName));
        var schema = await metadata.DescribeTableAsync(database, tableName, cancellationToken);
        var query = GXSelectArgs.Select(schema.Columns);
        await using var connection = connections.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        var columnFilters = new List<(GXColumnSchema Column, object? value)>();
        foreach (var filter in filters ?? new Dictionary<string, string>())
        {
            var column = schema.Columns.FirstOrDefault(c => string.Equals(c.Name, filter.Key, StringComparison.Ordinal));
            if (column == null) throw new ArgumentException($"Unknown filter column '{filter.Key}'.");
            if (string.IsNullOrWhiteSpace(filter.Value)) continue;
            columnFilters.Add((column, filter.Value));
        }
        query.Where.FilterBy(columnFilters.AsEnumerable());
        var countQuery = GXSelectArgs.From(() => GXSql.As(GXSql.Subquery<object>(query), "rows"));
        countQuery.Columns.Add(GXSqlExpressions.CountExpression());
        var result = new GXTableData();
        var databaseConnection = new GXDbConnection(connection);
        result.TotalCount = checked((int)(await databaseConnection.SelectAsync<long>(null, countQuery, cancellationToken)).Single());
        query.Index = checked((uint)startIndex);
        query.Count = checked((uint)count);
        var orderColumns = schema.Columns.Where(c => c.IsPrimaryKey).ToArray();
        if (orderColumns.Length == 0)
            orderColumns = schema.Columns.Where(c => c.Type != typeof(byte[])).Take(1).ToArray();
        if (orderColumns.Length == 0)
            throw new NotSupportedException("Paging requires a sortable column.");
        query.OrderBy.AddRange(orderColumns.Select(c => ((E)E.Constant(c), false)));
        var selectedRows = await databaseConnection.SelectAsync<object[]>(null, query, cancellationToken);
        foreach (var column in schema.Columns) result.Columns.Add(column.Name);
        foreach (var values in selectedRows)
        {
            var row = new GXTableDataRow { Id = startIndex + result.Rows.Count };
            foreach (var value in values)
                row.Values.Add(JsonSerializer.SerializeToElement(value is DBNull ? null : value));
            result.Rows.Add(row);
        }
        return result;
    }
}
