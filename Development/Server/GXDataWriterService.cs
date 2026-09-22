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
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Gurux.Service.Orm.Settings;
using System.Reflection;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared.Server;

namespace Gurux.Data.Relay.Server;

public sealed class GXDataWriterService : IDataWriterService
{
    private readonly IGXConfigurationService _configurationService;
    private readonly IGXDatabaseConnectionFactory _connectionFactory;

    public GXDataWriterService(
        IGXConfigurationService configurationService,
        IGXDatabaseConnectionFactory connectionFactory)
    {
        _configurationService = configurationService;
        _connectionFactory = connectionFactory;
    }

    public async Task<int> WriteAsync(string destinationTable, GXDataMessage message, CancellationToken cancellationToken)
    {
        GXServerConfiguration configuration = await _configurationService.LoadServerAsync(cancellationToken)
            ?? throw new InvalidOperationException("Server configuration was not found.");

        int processed = 0;
        foreach (GXDatabaseConfiguration database in ResolveTargetDatabases(configuration))
        {
            processed = await WriteAsync(database, destinationTable, message, configuration, cancellationToken);
        }

        return processed;
    }

    private static IReadOnlyList<GXDatabaseConfiguration> ResolveTargetDatabases(GXServerConfiguration configuration)
    {
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetTargetDatabases();
        if (GXServerDatabaseContext.DatabaseIndex is not int index)
        {
            return databases;
        }

        if (index < 0 || index >= databases.Count)
        {
            throw new InvalidOperationException($"Server database index {index} is outside configured Databases.");
        }

        return [databases[index]];
    }

    public async Task<int> WriteAsync(
        GXDatabaseConfiguration database,
        string destinationTable,
        GXDataMessage message,
        GXServerConfiguration serverConfiguration,
        CancellationToken cancellationToken)
    {
        GXRecordSource.Prepare(message);
        await using DbConnection connection = _connectionFactory.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        GXSchemaManager schemaManager = new(guruxConnection);
        GXTableSchema tableSchema = schemaManager.Describe(destinationTable);
        if (!string.IsNullOrWhiteSpace(message.RecordSource) &&
            !tableSchema.Columns.Any(c => string.Equals(c.Name, GXRecordSource.ColumnName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Destination table '{destinationTable}' needs a nullable RECORD_SOURCE string column (255 characters) to receive the configured record source.");
        GXDBSettings settings = GetSettings(schemaManager);
        GXStageLoadDate.Apply(message, tableSchema, DateTimeOffset.Now);

        using IDbTransaction transaction = connection.BeginTransaction();
        try
        {
            int processed = 0;
            foreach (GXDataChange change in message.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (change.Operation == DataOperation.Insert)
                {
                    if (change.Values is null || change.Values.Count == 0)
                    {
                        throw new InvalidOperationException("Insert change must contain values.");
                    }

                    await InsertRowAsync(guruxConnection, transaction, tableSchema, change.Values, database.Type, cancellationToken);
                }
                else if (change.Operation == DataOperation.Update)
                {
                    if (change.Values is null || change.Values.Count == 0)
                    {
                        throw new InvalidOperationException("Update change must contain values.");
                    }

                    await UpdateRowAsync(connection, transaction, settings, tableSchema, message.Keys, change, database.Type, cancellationToken);
                }
                else if (change.Operation == DataOperation.Delete)
                {
                    await DeleteRowAsync(connection, transaction, settings, tableSchema, message.Keys, change, database, cancellationToken);
                }
                else
                {
                    throw new NotSupportedException($"{change.Operation} handling is not implemented in this milestone.");
                }

                ++processed;
            }

            transaction.Commit();
            return processed;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static object? GetValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l)
                ? l
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element
        };
    }

    private static async Task InsertRowAsync(
        GXDbConnection guruxConnection,
        IDbTransaction transaction,
        GXTableSchema tableSchema,
        Dictionary<string, JsonElement> values,
        DatabaseType databaseType,
        CancellationToken cancellationToken)
    {
        List<GXColumnSchema> columns = [];
        List<object?> values2 = [];
        foreach ((string name, JsonElement it) in values)
        {
            GXColumnSchema? column = tableSchema.Columns.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (column is null)
            {
                continue;
            }
            object? value = GetValue(it);
            if (value is null)
            {
                continue;
            }
            value = ConvertColumnValue(it, column, tableSchema.Name, databaseType);
            columns.Add(column);
            values2.Add(value);
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Insert change does not contain values for any destination columns in table '{tableSchema.Name}'.");
        }
        await guruxConnection.InsertAsync(transaction, GXInsertArgs.Insert(values2, columns), cancellationToken);
    }

    private static object? ConvertColumnValue(JsonElement value, GXColumnSchema column, string tableName, DatabaseType databaseType)
    {
        try
        {
            return GXServerValueConversionHelper.ConvertJsonValue(value, column.Type, databaseType);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or JsonException or ArgumentException)
        {
            throw new InvalidOperationException($"Could not convert column '{tableName}.{column.Name}' to {column.Type.Name}: {ex.Message}", ex);
        }
    }

    private static async Task UpdateRowAsync(
        DbConnection connection,
        IDbTransaction transaction,
        GXDBSettings settings,
        GXTableSchema tableSchema,
        List<string> messageKeys,
        GXDataChange change,
        DatabaseType databaseType,
        CancellationToken cancellationToken)
    {
        Dictionary<string, JsonElement> keyValues = ResolveKeyValues(messageKeys, change, operationName: "Update");
        List<GXColumnSchema> keyColumns = ResolveColumns(tableSchema, keyValues.Keys);
        List<GXColumnSchema> updatedColumns = ResolveExistingColumns(tableSchema, change.Values!.Keys);

        HashSet<string> keyNames = new(keyColumns.Select(static column => column.Name), StringComparer.OrdinalIgnoreCase);
        List<GXColumnSchema> setColumns = updatedColumns
            .Where(column => !keyNames.Contains(column.Name))
            .ToList();

        if (setColumns.Count == 0)
        {
            throw new InvalidOperationException("Update change must include at least one non-key value.");
        }

        string[] setClauses = setColumns
            .Select((column, index) => $"{QuoteIdentifier(settings, column.Name, isTable: false)} = {GXSqlParameterHelper.GetPlaceholder(databaseType, $"set{index}")}")
            .ToArray();
        string[] whereClauses = keyColumns
            .Select((column, index) => $"{QuoteIdentifier(settings, column.Name, isTable: false)} = {GXSqlParameterHelper.GetPlaceholder(databaseType, $"key{index}")}")
            .ToArray();

        string sql = $"UPDATE {QuoteIdentifier(settings, tableSchema.ToString(), isTable: true)} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = (DbTransaction?)transaction;
        command.CommandType = CommandType.Text;
        command.CommandText = sql;

        for (int index = 0; index < setColumns.Count; ++index)
        {
            GXColumnSchema column = setColumns[index];
            AddParameter(command, GXSqlParameterHelper.GetPlaceholder(databaseType, $"set{index}"), ConvertColumnValue(GetJsonValue(change.Values!, column.Name), column, tableSchema.Name, databaseType), databaseType);
        }

        for (int index = 0; index < keyColumns.Count; ++index)
        {
            GXColumnSchema column = keyColumns[index];
            AddParameter(command, GXSqlParameterHelper.GetPlaceholder(databaseType, $"key{index}"), ConvertColumnValue(GetJsonValue(keyValues, column.Name), column, tableSchema.Name, databaseType), databaseType);
        }

        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException("Update change did not match any destination row.");
        }
    }

    private static async Task DeleteRowAsync(
        DbConnection connection,
        IDbTransaction transaction,
        GXDBSettings settings,
        GXTableSchema tableSchema,
        List<string> messageKeys,
        GXDataChange change,
        GXDatabaseConfiguration database,
        CancellationToken cancellationToken)
    {
        Dictionary<string, JsonElement> keyValues = ResolveKeyValues(messageKeys, change, operationName: "Delete");
        List<GXColumnSchema> keyColumns = ResolveColumns(tableSchema, keyValues.Keys);

        if (database.DeleteMode == DeleteMode.SoftDelete)
        {
            await SoftDeleteRowAsync(connection, transaction, settings, tableSchema, keyColumns, keyValues, database, cancellationToken);
            return;
        }

        string[] whereClauses = keyColumns
            .Select((column, index) => $"{QuoteIdentifier(settings, column.Name, isTable: false)} = {GXSqlParameterHelper.GetPlaceholder(database.Type, $"key{index}")}")
            .ToArray();

        string sql = $"DELETE FROM {QuoteIdentifier(settings, tableSchema.ToString(), isTable: true)} WHERE {string.Join(" AND ", whereClauses)}";

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = (DbTransaction?)transaction;
        command.CommandType = CommandType.Text;
        command.CommandText = sql;

        for (int index = 0; index < keyColumns.Count; ++index)
        {
            GXColumnSchema column = keyColumns[index];
            AddParameter(command, GXSqlParameterHelper.GetPlaceholder(database.Type, $"key{index}"), ConvertColumnValue(GetJsonValue(keyValues, column.Name), column, tableSchema.Name, database.Type), database.Type);
        }

        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException("Delete change did not match any destination row.");
        }
    }

    private static async Task SoftDeleteRowAsync(
        DbConnection connection,
        IDbTransaction transaction,
        GXDBSettings settings,
        GXTableSchema tableSchema,
        List<GXColumnSchema> keyColumns,
        Dictionary<string, JsonElement> keyValues,
        GXDatabaseConfiguration database,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(database.DeletedColumn))
        {
            throw new InvalidOperationException("Soft delete mode requires a configured deleted column.");
        }

        GXColumnSchema? deletedColumn = tableSchema.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, database.DeletedColumn, StringComparison.OrdinalIgnoreCase));
        if (deletedColumn is null)
        {
            throw new InvalidOperationException($"Deleted column '{database.DeletedColumn}' is missing from table '{tableSchema.Name}'.");
        }

        object markerValue = GXDeleteMarkerHelper.GetSoftDeleteMarkerValue(deletedColumn.Type);
        string[] whereClauses = keyColumns
            .Select((column, index) => $"{QuoteIdentifier(settings, column.Name, isTable: false)} = {GXSqlParameterHelper.GetPlaceholder(database.Type, $"key{index}")}")
            .ToArray();
        string sql = $"UPDATE {QuoteIdentifier(settings, tableSchema.ToString(), isTable: true)} SET {QuoteIdentifier(settings, deletedColumn.Name, isTable: false)} = {GXSqlParameterHelper.GetPlaceholder(database.Type, "deletedMarker")} WHERE {string.Join(" AND ", whereClauses)}";

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = (DbTransaction?)transaction;
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        AddParameter(command, GXSqlParameterHelper.GetPlaceholder(database.Type, "deletedMarker"), markerValue, database.Type);
        for (int index = 0; index < keyColumns.Count; ++index)
        {
            GXColumnSchema column = keyColumns[index];
            AddParameter(command, GXSqlParameterHelper.GetPlaceholder(database.Type, $"key{index}"), ConvertColumnValue(GetJsonValue(keyValues, column.Name), column, tableSchema.Name, database.Type), database.Type);
        }

        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException("Delete change did not match any destination row.");
        }
    }

    private static Dictionary<string, JsonElement> ResolveKeyValues(List<string> messageKeys, GXDataChange change, string operationName)
    {
        if (messageKeys.Count == 0)
        {
            throw new InvalidOperationException($"{operationName} change requires configured key columns.");
        }

        Dictionary<string, JsonElement> resolved = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in messageKeys)
        {
            if (change.Keys is not null && TryGetJsonValue(change.Keys, key, out JsonElement explicitKey))
            {
                resolved[key] = explicitKey;
                continue;
            }

            if (change.Values is not null && TryGetJsonValue(change.Values, key, out JsonElement implicitKey))
            {
                resolved[key] = implicitKey;
                continue;
            }

            throw new InvalidOperationException($"{operationName} change is missing key column '{key}'.");
        }

        return resolved;
    }

    private static List<GXColumnSchema> ResolveExistingColumns(GXTableSchema tableSchema, IEnumerable<string> names)
    {
        List<GXColumnSchema> columns = [];
        foreach (string name in names)
        {
            GXColumnSchema? column = tableSchema.Columns.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (column is not null)
            {
                columns.Add(column);
            }
        }

        return columns;
    }

    private static List<GXColumnSchema> ResolveColumns(GXTableSchema tableSchema, IEnumerable<string> names)
    {
        List<GXColumnSchema> columns = [];
        foreach (string name in names)
        {
            GXColumnSchema? column = tableSchema.Columns.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (column is null)
            {
                throw new InvalidOperationException($"Destination column '{name}' is missing from table '{tableSchema.Name}'.");
            }

            columns.Add(column);
        }

        return columns;
    }

    private static JsonElement GetJsonValue(IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        if (TryGetJsonValue(values, name, out JsonElement value))
        {
            return value;
        }

        throw new KeyNotFoundException($"The given key '{name}' was not present in the dictionary.");
    }

    private static bool TryGetJsonValue(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        out JsonElement value)
    {
        if (values.TryGetValue(name, out value))
        {
            return true;
        }

        foreach ((string key, JsonElement candidate) in values)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void AddParameter(DbCommand command, string name, object? value, DatabaseType databaseType)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = GXSqlParameterHelper.GetParameterName(name);
        parameter.Value = GXSqlParameterHelper.NormalizeParameterValue(databaseType, value) ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static GXDBSettings GetSettings(GXSchemaManager schemaManager)
    {
        FieldInfo? builderField = typeof(GXSchemaManager).GetField("Builder", BindingFlags.Instance | BindingFlags.NonPublic);
        object builder = builderField?.GetValue(schemaManager)
            ?? throw new InvalidOperationException("Failed to access Gurux SQL builder.");
        PropertyInfo? settingsProperty = builder.GetType().GetProperty("Settings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return (GXDBSettings?)settingsProperty?.GetValue(builder)
            ?? throw new InvalidOperationException("Failed to access Gurux database settings.");
    }

    private static string QuoteIdentifier(GXDBSettings settings, string identifier, bool isTable)
    {
        return GXSqlIdentifierHelper.QuoteIdentifier(settings, identifier, isTable);
    }
}


