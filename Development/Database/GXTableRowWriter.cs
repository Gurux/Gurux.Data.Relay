using System.Data.Common;
using System.Text.Json;
using Gurux.Data.Relay.Configuration;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Service.Orm.Common.Model;
using Gurux.Service.Orm.Enums;

namespace Gurux.Data.Relay.Database;

/// <summary>Writes individual rows in existing tables using schema validation and parameterized commands.</summary>
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
        string Quote(string name) => database.Type switch
        {
            DatabaseType.MSSQL => "[" + name.Replace("]", "]]") + "]",
            DatabaseType.MySQL or DatabaseType.MariaDB => "`" + name.Replace("`", "``") + "`",
            _ => "\"" + name.Replace("\"", "\"\"") + "\""
        };
        string table = string.Join(".", tableName.Split('.').Select(Quote));
        await using var connection = connections.CreateConnection(database);
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        string Parameter(string name, JsonElement value, bool key)
        {
            var column = schema.Columns.SingleOrDefault(column => column.Name == name)
                ?? throw new ArgumentException($"Unknown column '{name}'.");
            if (!key && (column.IsGenerated || column.IsComputed || column.IsIdentity || column.IsAutoIncrement || (keys != null && column.IsPrimaryKey)))
                throw new ArgumentException($"Column '{name}' is generated or is an immutable primary key.");
            if (value.ValueKind == JsonValueKind.Null && (key || !column.IsNullable))
                throw new ArgumentException($"Column '{name}' cannot be null.");
            string placeholder = database.Type is DatabaseType.DB2 or DatabaseType.SapHana ? "?"
                : GXSqlParameterHelper.GetPlaceholder(database.Type, $"p{command.Parameters.Count}");
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"p{command.Parameters.Count}";
            parameter.Value = GXSqlParameterHelper.NormalizeParameterValue(database.Type, ConvertValue(column, value)) ?? DBNull.Value;
            command.Parameters.Add(parameter);
            return placeholder;
        }

        var assignments = values?.Select(pair => (Column: Quote(pair.Key), Value: Parameter(pair.Key, pair.Value, false))).ToArray();
        string where = keys == null ? "" : " WHERE " + string.Join(" AND ", keys.Select(pair => $"{Quote(pair.Key)} = {Parameter(pair.Key, pair.Value, true)}"));
        command.CommandText = keys == null
            ? $"INSERT INTO {table} ({string.Join(", ", assignments!.Select(item => item.Column))}) VALUES ({string.Join(", ", assignments.Select(item => item.Value))})"
            : values == null ? $"DELETE FROM {table}{where}"
            : $"UPDATE {table} SET {string.Join(", ", assignments!.Select(item => $"{item.Column} = {item.Value}"))}{where}";
        int affected = await command.ExecuteNonQueryAsync(token);
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
