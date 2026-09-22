using System.Globalization;
using System.Text.Json;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Service.Orm.Enums;

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
        string Quote(string name) => database.Type switch
        {
            DatabaseType.MSSQL => "[" + name.Replace("]", "]]") + "]",
            DatabaseType.MySQL or DatabaseType.MariaDB => "`" + name.Replace("`", "``") + "`",
            _ => "\"" + name.Replace("\"", "\"\"") + "\""
        };
        string table = string.Join(".", tableName.Split('.').Select(Quote));
        var keys = schema.Columns.Where(c => c.IsPrimaryKey).Select(c => Quote(c.Name)).ToArray();
        string order = keys.Length == 0 ? "1" : string.Join(", ", keys);
        string offset = startIndex.ToString(CultureInfo.InvariantCulture);
        string limit = count.ToString(CultureInfo.InvariantCulture);
        await using var connection = connections.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var conditions = new List<string>();
        foreach (var filter in filters ?? new Dictionary<string, string>())
        {
            var column = schema.Columns.FirstOrDefault(c => string.Equals(c.Name, filter.Key, StringComparison.Ordinal));
            if (column == null) throw new ArgumentException($"Unknown filter column '{filter.Key}'.");
            if (string.IsNullOrWhiteSpace(filter.Value)) continue;
            string expression = database.Type switch
            {
                DatabaseType.MSSQL => $"CAST({Quote(column.Name)} AS NVARCHAR(MAX))",
                DatabaseType.MySQL or DatabaseType.MariaDB => $"CAST({Quote(column.Name)} AS CHAR)",
                DatabaseType.Oracle => $"TO_CHAR({Quote(column.Name)})",
                DatabaseType.DB2 => $"CAST({Quote(column.Name)} AS VARCHAR(32672))",
                DatabaseType.SapHana => $"TO_NVARCHAR({Quote(column.Name)})",
                _ => $"CAST({Quote(column.Name)} AS TEXT)"
            };
            string placeholder = GXSqlParameterHelper.GetPlaceholder(database.Type, $"filter{conditions.Count}");
            var parameter = command.CreateParameter();
            parameter.ParameterName = GXSqlParameterHelper.GetParameterName(placeholder);
            parameter.Value = "%" + filter.Value.ToUpperInvariant().Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![") + "%";
            command.Parameters.Add(parameter);
            conditions.Add($"UPPER({expression}) LIKE {placeholder} ESCAPE '!'");
        }
        string where = conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions);
        command.CommandText = $"SELECT COUNT(*) FROM {table}{where}";
        var result = new GXTableData
        {
            TotalCount = checked(Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture))
        };
        string paging = database.Type switch
        {
            DatabaseType.MSSQL or DatabaseType.Oracle or DatabaseType.DB2 => $"OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY",
            _ => $"LIMIT {limit} OFFSET {offset}"
        };
        // SQL Server requires an expression rather than a column ordinal in ORDER BY.
        if (keys.Length == 0 && database.Type == DatabaseType.MSSQL) order = "(SELECT NULL)";
        command.CommandText = $"SELECT * FROM {table}{where} ORDER BY {order} {paging}";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        for (int i = 0; i < reader.FieldCount; ++i) result.Columns.Add(reader.GetName(i));
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new GXTableDataRow { Id = startIndex + result.Rows.Count };
            for (int i = 0; i < reader.FieldCount; ++i)
                row.Values.Add(JsonSerializer.SerializeToElement(reader.IsDBNull(i) ? null : reader.GetValue(i)));
            result.Rows.Add(row);
        }
        return result;
    }
}

