using E = System.Linq.Expressions.Expression;
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
using Gurux.Data.Relay.Transport;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared.Protocol;

namespace Gurux.Data.Relay.Client;

public sealed class GXClientBatchSender : IClientBatchSender
{
    private sealed record GXPreparedBatch(GXDataMessage Message, string? CheckpointValue);

    private readonly IGXConfigurationService _configurationService;
    private readonly IGXDatabaseConnectionFactory _connectionFactory;
    private readonly IGXTransportFactory _transportFactory;
    private readonly ILogger<GXClientBatchSender> _logger;

    public GXClientBatchSender(
        IGXConfigurationService configurationService,
        IGXDatabaseConnectionFactory connectionFactory,
        IGXTransportFactory transportFactory,
        ILogger<GXClientBatchSender> logger)
    {
        _configurationService = configurationService;
        _connectionFactory = connectionFactory;
        _transportFactory = transportFactory;
        _logger = logger;
    }

    public async Task<int> SendAsync(GXClientConfiguration configuration, CancellationToken cancellationToken)
    {
        int sentMessages = 0;
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetSourceDatabases();
        for (int databaseIndex = 0; databaseIndex < databases.Count; ++databaseIndex)
        {
            IReadOnlyList<GXTableConfiguration> tables = GetConfiguredTables(databases[databaseIndex]);
            foreach (GXTableConfiguration tableConfiguration in tables)
            {
                if (configuration.GetTransports(databaseIndex, tableConfiguration.Name).Count == 0) continue;
                string stateTableName = GXClientTableStateNames.Get(tableConfiguration.Name, databaseIndex, databases.Count);
                sentMessages += await SendTableAsync(configuration, databases[databaseIndex], tableConfiguration, stateTableName, databaseIndex, databases.Count, cancellationToken);
            }
        }

        if (sentMessages == 0 && !databases.Any(database => database.Tables is { Count: > 0 }))
        {
            _logger.LogInformation("No client tables are configured for transfer.");
        }

        return sentMessages;
    }

    public async Task<int> SendTableAsync(
        GXClientConfiguration configuration,
        int databaseIndex,
        GXTableConfiguration tableConfiguration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(tableConfiguration);

        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetSourceDatabases();
        if (databaseIndex < 0 || databaseIndex >= databases.Count)
        {
            throw new InvalidOperationException($"Client source database index {databaseIndex + 1} is out of range.");
        }

        string stateTableName = GXClientTableStateNames.Get(tableConfiguration.Name, databaseIndex, databases.Count);
        return await SendTableAsync(configuration, databases[databaseIndex], tableConfiguration, stateTableName, databaseIndex, databases.Count, cancellationToken);
    }

    private async Task<int> SendTableAsync(
        GXClientConfiguration configuration,
        GXDatabaseConfiguration database,
        GXTableConfiguration tableConfiguration,
        string stateTableName,
        int databaseIndex,
        int databaseCount,
        CancellationToken cancellationToken)
    {
        if (configuration.GetTransports(databaseIndex, tableConfiguration.Name).Count == 0)
            throw new InvalidOperationException($"Table '{tableConfiguration.Name}' is not selected by any transport.");
        await using DbConnection connection = _connectionFactory.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        GXSchemaManager schemaManager = new(guruxConnection);

        GXClientState state = await _configurationService.LoadClientStateAsync(cancellationToken) ?? new GXClientState();

        GXClientTableState? tableState = state.Tables.FirstOrDefault(table =>
            string.Equals(table.Name, stateTableName, StringComparison.OrdinalIgnoreCase));
        GXTransferConfiguration transfer = GXClientTransferSettings.GetEffective(configuration.GetTransports(databaseIndex, tableConfiguration.Name));

        GXPreparedBatch? batch = await ReadBatchAsync(
            connection,
            schemaManager,
            database.Type,
            tableConfiguration,
            tableConfiguration.DeleteSourceRowsAfterTransfer ? null : tableState,
            transfer.BatchSize,
            cancellationToken);
        if (batch is null)
        {
            if (ShouldSendPing(transfer, tableState))
            {
                await SendPingAsync(configuration, database, tableConfiguration, state, stateTableName, databaseIndex, databaseCount, cancellationToken);
                return 1;
            }

            string? name = configuration.Databases[databaseIndex].Description;
            if (string.IsNullOrEmpty(name))
            {
                name = $"{databaseIndex + 1}/{databaseCount}";
            }
            _logger.LogInformation(
                "No rows available for table {TableName} in source database {name}.",
                tableConfiguration.Name,
                name);
            return 0;
        }

        batch.Message.RecordSource = GXRecordSource.Resolve(database, tableConfiguration);
        GXDataAcknowledgement acknowledgement = await SendToConfiguredTransportsAsync(configuration.GetTransports(databaseIndex, tableConfiguration.Name), batch.Message, cancellationToken);

        if (tableConfiguration.DeleteSourceRowsAfterTransfer)
            await DeleteSourceRowsAsync(connection, schemaManager, database.Type, tableConfiguration, batch.Message, cancellationToken);

        UpdateState(state, stateTableName, databaseIndex, acknowledgement.MessageId, batch.Message.Changes.Count, batch.CheckpointValue);
        await _configurationService.SaveClientStateAsync(state, cancellationToken);

        _logger.LogInformation("Message {MessageId} acknowledged by server.", acknowledgement.MessageId);
        return 1;
    }

    private async Task SendPingAsync(
        GXClientConfiguration configuration,
        GXDatabaseConfiguration database,
        GXTableConfiguration tableConfiguration,
        GXClientState state,
        string stateTableName,
        int databaseIndex,
        int databaseCount,
        CancellationToken cancellationToken)
    {
        GXDataMessage ping = new()
        {
            MessageType = MessageType.Ping,
            Keys = [.. tableConfiguration.Keys],
            Table = new GXTableSchema
            {
                Name = tableConfiguration.Name,
            },
        };

        _logger.LogInformation(
            "No rows available for table {TableName} in source database {DatabaseIndex}/{DatabaseCount}. Sending ping message {MessageId}.",
            tableConfiguration.Name,
            databaseIndex + 1,
            databaseCount,
            ping.MessageId);
        GXDataAcknowledgement acknowledgement = await SendToConfiguredTransportsAsync(configuration.GetTransports(databaseIndex, tableConfiguration.Name), ping, cancellationToken);

        await SavePingStateAsync(stateTableName, databaseIndex, acknowledgement.MessageId, cancellationToken);
        _logger.LogInformation("Ping message {MessageId} acknowledged by server.", acknowledgement.MessageId);
    }

    private static async Task DeleteSourceRowsAsync(DbConnection connection, GXSchemaManager schemaManager,
        DatabaseType databaseType, GXTableConfiguration table,
        GXDataMessage message, CancellationToken cancellationToken)
    {
        var schema = schemaManager.Describe(table.Name);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var change in message.Changes)
        {
            var values = change.Values;
            if (values is null || table.Keys.Any(key => !values.Keys.Contains(key, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The acknowledged row does not contain its source keys.");
            List<E> predicates = [];
            foreach (var pair in values)
            {
                var column = schema.Columns.Single(column => string.Equals(column.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
                object? value = pair.Value.ValueKind == JsonValueKind.Null ? null : Gurux.Data.Relay.Server.GXServerValueConversionHelper.ConvertJsonValue(
                    pair.Value, column.Type ?? typeof(string), databaseType);
                predicates.Add(GXSqlExpressions.Equal(E.Constant(column), GXSqlExpressions.Value(value)));
            }
            if (predicates.Count == 0) throw new InvalidOperationException("Source row has no values.");
            int deleted = await new GXDbConnection(connection).DeleteAsync(transaction, GXDeleteArgs.Delete(schema, GXSqlExpressions.And(predicates.ToArray())), cancellationToken);
            if (deleted > 1) throw new InvalidOperationException("Source keys matched more than one row.");
            // A row changed during transmission is retained for the next transfer.
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SavePingStateAsync(string tableName, int databaseIndex, Guid messageId,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; ++attempt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GXClientState latest = await _configurationService.LoadClientStateAsync(cancellationToken) ?? new();
            UpdatePingState(latest, tableName, databaseIndex, messageId);
            try
            {
                await _configurationService.SaveClientStateAsync(latest, cancellationToken);
                return;
            }
            catch (System.Data.DBConcurrencyException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), cancellationToken);
            }
        }
    }

    private async Task<GXDataAcknowledgement> SendToConfiguredTransportsAsync(
        IReadOnlyList<GXTransportConfiguration> transports,
        GXDataMessage message,
        CancellationToken cancellationToken)
    {
        return await new GXMessageDelivery(_transportFactory, _logger).DeliverAsync(transports, message, cancellationToken);
    }

    private static IReadOnlyList<GXTableConfiguration> GetConfiguredTables(GXDatabaseConfiguration database)
    {
        return database.Tables ?? [];
    }

    private static void UpdateState(GXClientState state, string tableName, int databaseIndex, Guid messageId, int rowCount, string? checkpointValue)
    {
        GXClientTableState? existing = state.Tables.FirstOrDefault(table =>
            string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new GXClientTableState
            {
                Name = tableName,
            };
            state.Tables.Add(existing);
        }

        existing.DatabaseIndex = databaseIndex;
        existing.LastMessageId = messageId;
        existing.LastSuccessfulTransfer = DateTimeOffset.Now;
        existing.LastSuccessfulNotification = existing.LastSuccessfulTransfer;
        existing.LastRowCount = rowCount;
        existing.LastCheckpointValue = checkpointValue;
    }

    private static void UpdatePingState(GXClientState state, string tableName, int databaseIndex, Guid messageId)
    {
        GXClientTableState? existing = state.Tables.FirstOrDefault(table =>
            string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new GXClientTableState
            {
                Name = tableName,
            };
            state.Tables.Add(existing);
        }

        DateTimeOffset now = DateTimeOffset.Now;
        existing.DatabaseIndex = databaseIndex;
        existing.LastPingMessageId = messageId;
        existing.LastPingAt = now;
        existing.LastSuccessfulNotification = now;
    }

    private static bool ShouldSendPing(GXTransferConfiguration transfer, GXClientTableState? tableState)
    {
        if (transfer.PingAfterSeconds <= 0)
        {
            return false;
        }

        DateTimeOffset? lastNotification = tableState?.LastSuccessfulNotification ?? tableState?.LastSuccessfulTransfer;
        if (lastNotification is null)
        {
            return true;
        }

        return DateTimeOffset.Now - lastNotification.Value >= TimeSpan.FromSeconds(transfer.PingAfterSeconds);
    }

    private static async Task<GXPreparedBatch?> ReadBatchAsync(
        DbConnection connection,
        GXSchemaManager schemaManager,
        DatabaseType databaseType,
        GXTableConfiguration tableConfiguration,
        GXClientTableState? tableState,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new InvalidOperationException("Transfer batch size must be positive.");
        }

        GXTableSchema tableSchema = schemaManager.Describe(tableConfiguration.Name);
        List<GXColumnSchema> columns = ResolveColumns(tableSchema, tableConfiguration.Columns);
        if (tableConfiguration.ChangeTracking.Type == ChangeTrackingType.ContentHash)
        {
            if (tableConfiguration.DeleteSourceRowsAfterTransfer || !string.IsNullOrWhiteSpace(tableConfiguration.IncrementalColumn))
                throw new InvalidOperationException("ContentHash cannot be combined with an incremental column or deleting source rows after transfer.");
            var tracker = new GXContentHashTracker(columns.Select(c => c.Name), tableConfiguration.Keys,
                tableConfiguration.ChangeTracking.HashColumns, tableState?.LastCheckpointValue, batchSize);
            var hashQuery = BuildSelectQuery(tableSchema.ToString(), columns, null, null);
            var hashRows = await new GXDbConnection(connection).SelectAsync<object[]>(null, hashQuery, cancellationToken);
            // Scan every row, including after the batch fills, to reject duplicate keys before sending.
            foreach (var hashRow in hashRows)
            {
                Dictionary<string, JsonElement> values = new(StringComparer.OrdinalIgnoreCase);
                foreach (var column in columns)
                {
                    var value = hashRow[columns.IndexOf(column)];
                    values[column.Name] = value is DBNull ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(value);
                }
                tracker.Add(values);
            }
            return tracker.Changes.Count == 0 ? null : new GXPreparedBatch(BuildMessage(tableConfiguration, columns, tracker.Changes), tracker.Checkpoint);
        }
        if (tableConfiguration.DeleteSourceRowsAfterTransfer)
        {
            var primaryKeys = tableSchema.Columns.Where(column => column.IsPrimaryKey).Select(column => column.Name).ToList();
            if (primaryKeys.Count == 0 || primaryKeys.Any(key =>
                !tableConfiguration.Keys.Contains(key, StringComparer.OrdinalIgnoreCase) ||
                !tableConfiguration.Columns.Contains(key, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Deleting source rows requires all source primary-key columns to be selected as columns and keys.");
        }
        if (tableConfiguration.ChangeTracking.Type == ChangeTrackingType.LastRow)
        {
            return await ReadLastRowBatchAsync(connection, databaseType, tableSchema, columns, tableConfiguration, tableState, batchSize, cancellationToken);
        }
        if (tableConfiguration.ChangeTracking.Type == ChangeTrackingType.Timestamp)
        {
            return await ReadTimestampBatchAsync(connection, databaseType, tableSchema, columns, tableConfiguration, tableState, batchSize, cancellationToken);
        }
        if (tableConfiguration.ChangeTracking.Type == ChangeTrackingType.Version)
        {
            return await ReadVersionBatchAsync(connection, databaseType, tableSchema, columns, tableConfiguration, tableState, batchSize, cancellationToken);
        }

        GXColumnSchema? incrementalColumn = ResolveIncrementalColumn(tableSchema, tableConfiguration);
        var query = BuildSelectQuery(tableSchema.ToString(), columns, incrementalColumn, tableState?.LastCheckpointValue);

        var selectedRows = await new GXDbConnection(connection).SelectAsync<object[]>(null, query, cancellationToken);
        List<GXDataChange> changes = [];
        foreach (var selectedRow in selectedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (changes.Count >= batchSize) break;
            Dictionary<string, JsonElement> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (GXColumnSchema column in columns)
            {
                object? value = selectedRow[columns.IndexOf(column)];
                values[column.Name] = value is DBNull ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(value);
            }

            changes.Add(new GXDataChange
            {
                Operation = DataOperation.Insert,
                Values = values,
            });
        }

        if (changes.Count == 0)
        {
            return null;
        }

        GXDataMessage message = BuildMessage(tableConfiguration, columns, changes);
        return new GXPreparedBatch(message, GetCheckpointValue(message, tableConfiguration));
    }

    private static async Task<GXPreparedBatch?> ReadLastRowBatchAsync(
        DbConnection connection,
        DatabaseType databaseType,
        GXTableSchema tableSchema,
        List<GXColumnSchema> columns,
        GXTableConfiguration tableConfiguration,
        GXClientTableState? tableState,
        int batchSize,
        CancellationToken cancellationToken)
    {
        GXColumnSchema lastRowColumn = ResolveLastRowColumn(tableSchema, tableConfiguration);
        var query = BuildSelectQuery(tableSchema.ToString(), columns, lastRowColumn, tableState?.LastCheckpointValue);

        var selectedRows = await new GXDbConnection(connection).SelectAsync<object[]>(null, query, cancellationToken);
        List<GXDataChange> changes = [];
        string? checkpoint = tableState?.LastCheckpointValue;
        foreach (var selectedRow in selectedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (changes.Count >= batchSize) break;
            Dictionary<string, JsonElement> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (GXColumnSchema column in columns)
            {
                object? value = selectedRow[columns.IndexOf(column)];
                values[column.Name] = value is DBNull ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(value);
            }

            changes.Add(new GXDataChange
            {
                Operation = DataOperation.Insert,
                Values = values,
            });
            checkpoint = GXChangeTrackingHelper.GetCheckpointValue(values, lastRowColumn.Name) ?? checkpoint;
        }

        if (changes.Count == 0)
        {
            return null;
        }

        GXDataMessage message = BuildMessage(tableConfiguration, columns, changes);
        return new GXPreparedBatch(message, checkpoint);
    }

    private static async Task<GXPreparedBatch?> ReadTimestampBatchAsync(
        DbConnection connection,
        DatabaseType databaseType,
        GXTableSchema tableSchema,
        List<GXColumnSchema> columns,
        GXTableConfiguration tableConfiguration,
        GXClientTableState? tableState,
        int batchSize,
        CancellationToken cancellationToken)
    {
        GXColumnSchema? createdColumn = ResolveTrackedColumn(tableSchema, tableConfiguration.ChangeTracking.CreatedColumn);
        GXColumnSchema? updatedColumn = ResolveTrackedColumn(tableSchema, tableConfiguration.ChangeTracking.UpdatedColumn);
        GXColumnSchema? deletedColumn = ResolveTrackedColumn(tableSchema, tableConfiguration.ChangeTracking.DeletedColumn);
        var query = BuildTimestampSelectQuery(tableSchema.ToString(), columns, createdColumn, updatedColumn, deletedColumn, tableState?.LastCheckpointValue);
        DateTimeOffset? checkpoint = string.IsNullOrWhiteSpace(tableState?.LastCheckpointValue) ? null : DateTimeOffset.Parse(tableState.LastCheckpointValue!);

        var selectedRows = await new GXDbConnection(connection).SelectAsync<object[]>(null, query, cancellationToken);
        List<GXDataChange> changes = [];
        DateTimeOffset? maxCheckpoint = checkpoint;
        foreach (var selectedRow in selectedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (changes.Count >= batchSize) break;
            Dictionary<string, JsonElement> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (GXColumnSchema column in columns)
            {
                object? value = selectedRow[columns.IndexOf(column)];
                values[column.Name] = value is DBNull ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(value);
            }

            if (checkpoint is null)
            {
                changes.Add(new GXDataChange
                {
                    Operation = DataOperation.Insert,
                    Values = values,
                });
                maxCheckpoint = Max(maxCheckpoint, GXChangeTrackingHelper.GetMaximumTrackedTimestamp(values, createdColumn?.Name, updatedColumn?.Name, deletedColumn?.Name));
                continue;
            }

            DataOperation? operation = GXChangeTrackingHelper.DetectTimestampOperation(values, checkpoint.Value, createdColumn?.Name, updatedColumn?.Name, deletedColumn?.Name);
            if (operation is null)
            {
                continue;
            }

            GXDataChange change = new()
            {
                Operation = operation.Value,
            };
            if (operation == DataOperation.Delete)
            {
                change.Keys = GXChangeTrackingHelper.BuildKeyValues(values, tableConfiguration.Keys);
            }
            else
            {
                change.Values = values;
                if (operation == DataOperation.Update)
                {
                    change.Keys = GXChangeTrackingHelper.BuildKeyValues(values, tableConfiguration.Keys);
                }
            }

            changes.Add(change);
            maxCheckpoint = Max(maxCheckpoint, GXChangeTrackingHelper.GetMaximumTrackedTimestamp(values, createdColumn?.Name, updatedColumn?.Name, deletedColumn?.Name));
        }

        if (changes.Count == 0)
        {
            return null;
        }

        GXDataMessage message = BuildMessage(tableConfiguration, columns, changes);
        return new GXPreparedBatch(message, maxCheckpoint?.ToUniversalTime().ToString("O"));
    }

    private static async Task<GXPreparedBatch?> ReadVersionBatchAsync(
        DbConnection connection,
        DatabaseType databaseType,
        GXTableSchema tableSchema,
        List<GXColumnSchema> columns,
        GXTableConfiguration tableConfiguration,
        GXClientTableState? tableState,
        int batchSize,
        CancellationToken cancellationToken)
    {
        GXColumnSchema versionColumn = ResolveVersionColumn(tableSchema, tableConfiguration);
        bool hasCheckpoint = !string.IsNullOrWhiteSpace(tableState?.LastCheckpointValue);
        var query = BuildSelectQuery(tableSchema.ToString(), columns, versionColumn, tableState?.LastCheckpointValue);
        if (!hasCheckpoint) query.OrderBy.Clear();

        var selectedRows = await new GXDbConnection(connection).SelectAsync<object[]>(null, query, cancellationToken);
        List<GXDataChange> changes = [];
        string? maxCheckpoint = tableState?.LastCheckpointValue;
        foreach (var selectedRow in selectedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (changes.Count >= batchSize) break;
            Dictionary<string, JsonElement> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (GXColumnSchema column in columns)
            {
                object? value = selectedRow[columns.IndexOf(column)];
                values[column.Name] = value is DBNull ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(value);
            }

            GXDataChange change = new()
            {
                Operation = GXChangeTrackingHelper.GetVersionOperation(hasCheckpoint),
                Values = values,
            };
            if (hasCheckpoint)
            {
                change.Keys = GXChangeTrackingHelper.BuildKeyValues(values, tableConfiguration.Keys);
            }

            changes.Add(change);
            maxCheckpoint = GXChangeTrackingHelper.GetCheckpointValue(values, versionColumn.Name) ?? maxCheckpoint;
        }

        if (changes.Count == 0)
        {
            return null;
        }

        GXDataMessage message = BuildMessage(tableConfiguration, columns, changes);
        return new GXPreparedBatch(message, maxCheckpoint);
    }

    private static GXDataMessage BuildMessage(GXTableConfiguration tableConfiguration, List<GXColumnSchema> columns, List<GXDataChange> changes)
    {
        GXTableSchema table = new() { Name = tableConfiguration.Name };
        foreach (GXColumnSchema column in columns)
        {
            table.Columns.Add(new GXColumnSchema
            {
                Name = column.Name,
                Type = column.Type is null ? typeof(string) : Nullable.GetUnderlyingType(column.Type) ?? column.Type,
                IsNullable = column.IsNullable,
                IsPrimaryKey = column.IsPrimaryKey,
                Ordinal = column.Ordinal,
                MaxLength = column.MaxLength,
                Precision = column.Precision,
                Scale = column.Scale,
                Parent = table,
            });
        }
        return new GXDataMessage
        {
            Keys = [.. tableConfiguration.Keys],
            Table = table,
            Changes = changes,
        };
    }

    private static List<GXColumnSchema> ResolveColumns(GXTableSchema tableSchema, IEnumerable<string> selectedColumns)
    {
        List<GXColumnSchema> columns = [];
        foreach (string name in selectedColumns)
        {
            GXColumnSchema? column = tableSchema.Columns.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (column is null)
            {
                string availableColumns = tableSchema.Columns.Count == 0
                    ? "none"
                    : string.Join(", ", tableSchema.Columns.Select(item => item.Name));
                throw new InvalidOperationException(
                    $"Configured source column '{name}' is missing from table '{tableSchema.Name}'. Available columns: {availableColumns}.");
            }

            columns.Add(column);
        }

        return columns;
    }

    private static GXSelectArgs BuildSelectQuery(string tableName, List<GXColumnSchema> columns,
        GXColumnSchema? incrementalColumn, string? checkpointValue)
    {
        var query = GXSelectArgs.Select(columns);
        if (incrementalColumn != null)
        {
            query.OrderBy.Add(E.Constant(incrementalColumn));
            if (!string.IsNullOrWhiteSpace(checkpointValue))
                query.Where.Set(GXSqlExpressions.Greater(E.Constant(incrementalColumn), GXSqlExpressions.Value(ParseCheckpointValue(checkpointValue, incrementalColumn.Type))));
        }
        return query;
    }

    private static GXColumnSchema? ResolveIncrementalColumn(GXTableSchema tableSchema, GXTableConfiguration tableConfiguration)
    {
        if (string.IsNullOrWhiteSpace(tableConfiguration.IncrementalColumn))
        {
            return null;
        }

        GXColumnSchema? column = tableSchema.Columns.FirstOrDefault(item =>
            string.Equals(item.Name, tableConfiguration.IncrementalColumn, StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            throw new InvalidOperationException($"Configured incremental column '{tableConfiguration.IncrementalColumn}' is missing from table '{tableSchema.Name}'.");
        }

        return column;
    }

    private static GXColumnSchema ResolveLastRowColumn(GXTableSchema tableSchema, GXTableConfiguration tableConfiguration)
    {
        if (string.IsNullOrWhiteSpace(tableConfiguration.ChangeTracking.Column))
        {
            throw new InvalidOperationException($"LastRow change tracking for table '{tableConfiguration.Name}' requires a configured Column value.");
        }

        GXColumnSchema? column = tableSchema.Columns.FirstOrDefault(item =>
            string.Equals(item.Name, tableConfiguration.ChangeTracking.Column, StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            throw new InvalidOperationException($"Configured last row column '{tableConfiguration.ChangeTracking.Column}' is missing from table '{tableSchema.Name}'.");
        }

        if (!tableConfiguration.Columns.Any(name => string.Equals(name, column.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Configured last row column '{column.Name}' must be included in selected source columns for table '{tableConfiguration.Name}'.");
        }

        return column;
    }

    private static GXColumnSchema? ResolveTrackedColumn(GXTableSchema tableSchema, string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return null;
        }

        GXColumnSchema? column = tableSchema.Columns.FirstOrDefault(item =>
            string.Equals(item.Name, columnName, StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            throw new InvalidOperationException($"Configured tracking column '{columnName}' is missing from table '{tableSchema.Name}'.");
        }

        return column;
    }

    private static GXColumnSchema ResolveVersionColumn(GXTableSchema tableSchema, GXTableConfiguration tableConfiguration)
    {
        if (string.IsNullOrWhiteSpace(tableConfiguration.ChangeTracking.Column))
        {
            throw new InvalidOperationException($"Version change tracking for table '{tableConfiguration.Name}' requires a configured Column value.");
        }

        GXColumnSchema? column = tableSchema.Columns.FirstOrDefault(item =>
            string.Equals(item.Name, tableConfiguration.ChangeTracking.Column, StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            throw new InvalidOperationException($"Configured version column '{tableConfiguration.ChangeTracking.Column}' is missing from table '{tableSchema.Name}'.");
        }

        return column;
    }

    private static string? GetCheckpointValue(GXDataMessage message, GXTableConfiguration tableConfiguration)
    {
        if (string.IsNullOrWhiteSpace(tableConfiguration.IncrementalColumn) || message.Changes.Count == 0)
        {
            return null;
        }

        JsonElement? last = null;
        foreach (GXDataChange change in message.Changes)
        {
            if (change.Values is not null && change.Values.TryGetValue(tableConfiguration.IncrementalColumn, out JsonElement value))
            {
                last = value;
            }
        }

        return last?.ToString();
    }

    private static GXSelectArgs BuildTimestampSelectQuery(string tableName, List<GXColumnSchema> columns,
        GXColumnSchema? createdColumn, GXColumnSchema? updatedColumn, GXColumnSchema? deletedColumn, string? checkpointValue)
    {
        var query = BuildSelectQuery(tableName, columns, null, null);
        if (!string.IsNullOrWhiteSpace(checkpointValue))
        {
            var tracked = new[] { deletedColumn, updatedColumn, createdColumn }.OfType<GXColumnSchema>().ToArray();
            if (tracked.Length == 0) throw new InvalidOperationException("Timestamp change tracking requires at least one CreatedColumn, UpdatedColumn or DeletedColumn value.");
            var checkpoint = DateTimeOffset.Parse(checkpointValue);
            query.Where.Set(GXSqlExpressions.Or(tracked.Select(c => GXSqlExpressions.Greater(E.Constant(c), GXSqlExpressions.Value(checkpoint))).ToArray()));
            var order = tracked.Select(c => E.Constant(c)).ToArray();
            query.OrderBy.Add(order.Skip(1).Aggregate((E)order[0], (left, right) => E.Coalesce(left, right)));
        }
        return query;
    }

    private static DateTimeOffset? Max(DateTimeOffset? current, DateTimeOffset? candidate)
    {
        if (candidate is null)
        {
            return current;
        }
        if (current is null || candidate > current)
        {
            return candidate;
        }

        return current;
    }

    private static object ParseCheckpointValue(string checkpointValue, Type? type)
    {
        Type actualType = Nullable.GetUnderlyingType(type ?? typeof(string)) ?? type ?? typeof(string);
        if (actualType == typeof(DateTimeOffset)) return DateTimeOffset.Parse(checkpointValue);
        if (actualType == typeof(DateTime)) return DateTime.Parse(checkpointValue);
        if (actualType == typeof(DateOnly)) return DateOnly.Parse(checkpointValue);
        if (actualType == typeof(TimeOnly)) return TimeOnly.Parse(checkpointValue);
        if (actualType == typeof(Guid)) return Guid.Parse(checkpointValue);
        if (actualType == typeof(long)) return long.Parse(checkpointValue);
        if (actualType == typeof(ulong)) return ulong.Parse(checkpointValue);
        if (actualType == typeof(int)) return int.Parse(checkpointValue);
        if (actualType == typeof(uint)) return uint.Parse(checkpointValue);
        if (actualType == typeof(short)) return short.Parse(checkpointValue);
        if (actualType == typeof(ushort)) return ushort.Parse(checkpointValue);
        if (actualType == typeof(decimal)) return decimal.Parse(checkpointValue);
        if (actualType == typeof(double)) return double.Parse(checkpointValue);
        if (actualType == typeof(float)) return float.Parse(checkpointValue);
        return checkpointValue;
    }

}
