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
using System.Data.Common;
using System.Text.Json;
using Gurux.Data.Relay.Configuration;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Service.Orm.Common.Model;
using Gurux.Service.Orm.Enums;

namespace Gurux.Data.Relay.Database;

/// <summary>Writes individual rows in existing tables using schema validation and Gurux.Service execution methods.</summary>
public sealed class GXTableRowWriter(IGXDatabaseConnectionFactory connections, IGXDatabaseMetadataService metadata)
{
    /// <summary>Inserts a row. Omit generated columns and columns whose database defaults should apply.</summary>
    public Task<int> InsertAsync(GXDatabaseConfiguration database, string table, IReadOnlyDictionary<string, JsonElement> values, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(values);
        return WriteAsync(database, table, null, values, token);
    }

    /// <summary>Updates supplied non-key columns of the row identified by its complete primary key.</summary>
    public Task<int> UpdateAsync(GXDatabaseConfiguration database, string table, IReadOnlyDictionary<string, JsonElement> keys,
        IReadOnlyDictionary<string, JsonElement> values, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(values);
        return WriteAsync(database, table, keys, values, token);
    }

    /// <summary>Deletes the row identified by its complete primary key.</summary>
    public Task<int> DeleteAsync(GXDatabaseConfiguration database, string table, IReadOnlyDictionary<string, JsonElement> keys, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return WriteAsync(database, table, keys, null, token);
    }

    private async Task<int> WriteAsync(GXDatabaseConfiguration database, string tableName, IReadOnlyDictionary<string, JsonElement>? keys,
        IReadOnlyDictionary<string, JsonElement>? values, CancellationToken token)
    {
        if (!(await metadata.GetTableNamesAsync(database, token)).Contains(tableName, StringComparer.Ordinal))
            throw new ArgumentException("The selected table does not exist.");
        var schema = await metadata.DescribeTableAsync(database, tableName, token);
        if (values is { Count: 0 }) throw new ArgumentException("Supply at least one column value.");
        if (keys != null)
        {
            var primaryKey = schema.Columns.Where(column => column.IsPrimaryKey).Select(column => column.Name).ToHashSet(StringComparer.Ordinal);
            if (primaryKey.Count == 0 || !primaryKey.SetEquals(keys.Keys))
                throw new ArgumentException("Supply every primary-key column and no other columns in keys.");
        }
        await using var connection = connections.CreateConnection(database);
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        object? Parameter(string name, JsonElement value, bool key)
        {
            var column = schema.Columns.SingleOrDefault(column => column.Name == name)
                ?? throw new ArgumentException($"Unknown column '{name}'.");
            if (!key && (column.IsGenerated || column.IsComputed || column.IsIdentity || column.IsAutoIncrement || (keys != null && column.IsPrimaryKey)))
                throw new ArgumentException($"Column '{name}' is generated or is an immutable primary key.");
            if (value.ValueKind == JsonValueKind.Null && (key || !column.IsNullable))
                throw new ArgumentException($"Column '{name}' cannot be null.");
            return ConvertValue(column, value);
        }
        var assignments = values?.Select(pair => (
            Column: schema.Columns.Single(column => column.Name == pair.Key),
            Value: Parameter(pair.Key, pair.Value, false))).ToArray();
        E? predicate = null;
        if (keys != null)
        {
            foreach (var pair in keys)
            {
                var column = schema.Columns.Single(column => column.Name == pair.Key);
                var value = Parameter(pair.Key, pair.Value, true);
                System.Linq.Expressions.Expression<Func<GXColumnSchema, bool>> condition = _ => column == value;
                predicate = predicate == null ? condition.Body : E.AndAlso(predicate, condition.Body);
            }
        }
        GXUpdateArgs? update = null;
        if (keys != null && assignments != null)
        {
            update = GXUpdateArgs.Update(assignments);
            update.Where.Set(predicate);
        }
        var databaseConnection = new GXDbConnection(connection);
        int affected = keys == null
            ? await databaseConnection.InsertAsync(transaction, GXInsertArgs.Insert(assignments!), token)
            : values == null
                ? await databaseConnection.DeleteAsync(transaction, GXDeleteArgs.Delete(schema, predicate!), token)
                : await databaseConnection.UpdateAsync(transaction, update!, token);
        if (affected is < 0 or > 1) throw new InvalidOperationException("The operation did not affect exactly one row; changes were rolled back.");
        await transaction.CommitAsync(token);
        return affected;
    }

    private static object? ConvertValue(GXColumnSchema column, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Undefined)
            throw new ArgumentException($"Column '{column.Name}' requires a scalar value; use Base64 for binary data.");
        try
        {
            if (column.Type == typeof(object) || column.Type == null)
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            return JsonSerializer.Deserialize(value.GetRawText(), column.Type, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException or OverflowException)
        {
            throw new ArgumentException($"Invalid value for column '{column.Name}' ({column.Type?.Name}).", ex);
        }
    }
}
