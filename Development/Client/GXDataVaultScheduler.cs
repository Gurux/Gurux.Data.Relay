using Gurux.Service.Orm.Common.Model;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Gurux.Scheduling;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Enums;
using Gurux.Service.Orm.Model;
using Gurux.Service.Orm.Settings;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Database;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Log;

namespace Gurux.Data.Relay.Client;

public sealed class GXDataVaultScheduler
{
    private static GXTable Source(GXDataVaultTableMapping mapping) => mapping.SourceTable
        ?? throw new InvalidOperationException($"Data Vault mapping '{mapping.Id}' requires a source table.");
    private static GXTable Target(GXDataVaultTableMapping mapping) => mapping.TargetTable
        ?? throw new InvalidOperationException($"Data Vault mapping '{mapping.Id}' requires a target table.");
    private sealed record GXDataVaultMartRun(
        GXDatabaseConfiguration Database,
        GXDataVaultTableMapping Mart);

    private readonly IGXDatabaseConnectionFactory _connectionFactory;
    private readonly IDatabaseChangeNotifierFactory _databaseChangeNotifierFactory;
    private readonly ILogger<GXDataVaultScheduler> _logger;
    private readonly Dictionary<GXDatabaseConfiguration, int> _databaseIndexes = new();
    private HashAlgorithmType _hashAlgorithm = HashAlgorithmType.SHA256;
    public IGXDataVaultRuntimeStateStore? RuntimeStateStore { get; set; }

    public GXDataVaultScheduler(
        IGXDatabaseConnectionFactory connectionFactory,
        IDatabaseChangeNotifierFactory databaseChangeNotifierFactory,
        ILogger<GXDataVaultScheduler> logger)
    {
        _connectionFactory = connectionFactory;
        _databaseChangeNotifierFactory = databaseChangeNotifierFactory;
        _logger = logger;
    }

    public async Task<int> RunAsync(
        GXDataVaultConfiguration configuration,
        CancellationToken cancellationToken)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = runCancellation.Token;
        _hashAlgorithm = configuration.HashAlgorithm;
        for (int index = 0; index < configuration.Databases.Count; ++index)
            _databaseIndexes[configuration.Databases[index]] = index;
        List<GXDataVaultMartRun> scheduledMarts = GetScheduledInformationMarts(configuration);
        if (scheduledMarts.Count == 0)
        {
            return 0;
        }

        List<GXDataVaultMartRun> databaseChangeMarts = scheduledMarts
            .Where(mapping => mapping.Mart.Schedule.Type == ScheduleType.DatabaseChange)
            .ToList();
        List<GXDataVaultMartRun> timedMarts = scheduledMarts
            .Where(mapping => mapping.Mart.Schedule.Type != ScheduleType.DatabaseChange)
            .ToList();

        Task<int>? databaseChangeTask = databaseChangeMarts.Count == 0
            ? null
            : RunDatabaseChangeAsync(databaseChangeMarts, cancellationToken);

        int copiedRows = 0;
        Dictionary<GXDataVaultMartRun, DateTimeOffset> nextRuns = timedMarts.ToDictionary(
            mapping => mapping,
            mapping => GetInitialRun(mapping.Mart.Schedule, DateTimeOffset.UtcNow));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset now = DateTimeOffset.Now;
                foreach (GXDataVaultMartRun mart in timedMarts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (nextRuns[mart] > now)
                    {
                        continue;
                    }

                    copiedRows += await RunScheduledMappingAsync(mart, cancellationToken);
                    nextRuns[mart] = GetNextRun(mart.Mart.Schedule, now);
                }

                if (timedMarts.Count == 0)
                {
                    return await databaseChangeTask!;
                }

                DateTimeOffset nextDue = nextRuns.Values.Min();
                TimeSpan delay = nextDue - DateTimeOffset.Now;
                if (delay < TimeSpan.FromSeconds(1))
                {
                    delay = TimeSpan.FromSeconds(1);
                }

                var delayTask = Task.Delay(delay, cancellationToken);
                if (databaseChangeTask is not null && await Task.WhenAny(delayTask, databaseChangeTask) == databaseChangeTask)
                    await databaseChangeTask;
                await delayTask;
            }
        }
        finally
        {
            await runCancellation.CancelAsync();
            if (databaseChangeTask is not null)
            {
                try
                {
                    copiedRows += await databaseChangeTask;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            }
        }

        return copiedRows;
    }

    public async Task<int> RefreshInformationMartsAsync(
        GXDataVaultConfiguration configuration,
        CancellationToken cancellationToken)
    {
        _hashAlgorithm = configuration.HashAlgorithm;
        for (int index = 0; index < configuration.Databases.Count; ++index)
            _databaseIndexes[configuration.Databases[index]] = index;
        int copiedRows = 0;
        foreach (GXDatabaseConfiguration database in configuration.GetDatabases())
        {
            foreach (GXDataVaultTableMapping mart in (database.Mappings ?? [])
                .Where(mapping => mapping.ObjectType == DataVaultObjectType.InformationMart))
            {
                copiedRows += await RefreshInformationMartAsync(database, mart, cancellationToken);
            }
        }

        return copiedRows;
    }

    public async Task<int> RefreshInformationMartsAsync(
        GXDatabaseConfiguration database,
        string sourceTable,
        CancellationToken cancellationToken,
        HashAlgorithmType hashAlgorithm = HashAlgorithmType.SHA256)
    {
        _hashAlgorithm = hashAlgorithm;
        int copiedRows = 0;
        foreach (GXDataVaultTableMapping mart in (database.Mappings ?? [])
            .Where(mapping => mapping.ObjectType == DataVaultObjectType.InformationMart &&
                string.Equals(Source(mapping).Name, sourceTable, StringComparison.OrdinalIgnoreCase)))
        {
            copiedRows += await RefreshInformationMartAsync(database, mart, cancellationToken);
        }

        return copiedRows;
    }

    public Task<int> RefreshInformationMartAsync(GXDatabaseConfiguration database, Guid mappingId, CancellationToken cancellationToken)
    {
        var mapping = (database.Mappings ?? []).SingleOrDefault(m => m.Id == mappingId && m.ObjectType == DataVaultObjectType.InformationMart)
            ?? throw new ArgumentException("The Information Mart mapping was not found.", nameof(mappingId));
        return RefreshInformationMartAsync(database, mapping, cancellationToken);
    }

    private Task<int> RunScheduledMappingAsync(GXDataVaultMartRun run, CancellationToken token)
        => run.Mart.ObjectType == DataVaultObjectType.Staging
            ? StartMappingAsync(run.Database, run.Mart.Id, _hashAlgorithm, token)
            : RefreshInformationMartAsync(run.Database, run.Mart, token);

    private async Task<int> RunDatabaseChangeAsync(
        IReadOnlyList<GXDataVaultMartRun> marts,
        CancellationToken cancellationToken)
    {
        Channel<GXDataVaultMartRun> changes = Channel.CreateUnbounded<GXDataVaultMartRun>();
        List<IDatabaseChangeSubscription> subscriptions = [];

        try
        {
            foreach (GXDataVaultMartRun mart in marts)
            {
                foreach (GXDataVaultTableMapping staging in mart.Mart.ObjectType == DataVaultObjectType.Staging
                    ? new[] { mart.Mart } : ResolveStagingMappings(mart.Database, mart.Mart).ToArray())
                {
                    IDatabaseChangeSubscription subscription = await _databaseChangeNotifierFactory.CreateAsync(
                        mart.Database,
                        CreateStagingTableConfiguration(staging, mart.Mart.Schedule),
                        cancellationToken);
                    subscription.Changed += (_, args) =>
                    {
                        if (string.IsNullOrWhiteSpace(args.Table) ||
                            string.Equals(args.Table, Target(staging).Name, StringComparison.OrdinalIgnoreCase))
                        {
                            changes.Writer.TryWrite(mart);
                        }
                    };
                    subscriptions.Add(subscription);
                    await subscription.StartAsync(cancellationToken);
                    _logger.LogInformation("Started Data Vault database change monitoring for {StagingTable}.", Target(staging).Name);
                }
            }

            int copiedRows = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                GXDataVaultMartRun mart = await changes.Reader.ReadAsync(cancellationToken);
                copiedRows += await RunScheduledMappingAsync(mart, cancellationToken);
            }

            return copiedRows;
        }
        finally
        {
            foreach (IDatabaseChangeSubscription subscription in subscriptions)
            {
                try
                {
                    await subscription.StopAsync(CancellationToken.None);
                }
                finally
                {
                    await subscription.DisposeAsync();
                }
            }
        }
    }

    private async Task<int> RefreshInformationMartAsync(
        GXDatabaseConfiguration database,
        GXDataVaultTableMapping mart,
        CancellationToken cancellationToken)
        => await TrackMappingRunAsync(mart, () => ExecuteInformationMartAsync(database, mart, cancellationToken), cancellationToken);

    private async Task<int> TrackMappingRunAsync(GXDataVaultTableMapping mart, Func<Task<int>> execute, CancellationToken cancellationToken)
    {
        Guid runId = Guid.NewGuid();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await RecordAsync("Running", null, null, cancellationToken);
        try
        {
            int rows = await execute();
            await RecordAsync("Succeeded", rows, null, CancellationToken.None);
            return rows;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RecordAsync("Cancelled", null, null, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await RecordAsync("Failed", null, ex.Message, CancellationToken.None);
            throw;
        }

        async Task RecordAsync(string status, int? rows, string? error, CancellationToken token)
        {
            if (RuntimeStateStore == null) return;
            try
            {
                await RuntimeStateStore.RecordDataVaultRunAsync(mart.Id, runId, status, rows,
                    status == "Running" ? null : timer.ElapsedMilliseconds, error, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not persist runtime state for Data Vault mapping {MappingId}.", mart.Id);
            }
        }
    }

    public async Task<int> StartMappingAsync(GXDatabaseConfiguration database, Guid mappingId,
        HashAlgorithmType hashAlgorithm, CancellationToken cancellationToken)
    {
        _hashAlgorithm = hashAlgorithm;
        var mappings = database.Mappings ?? [];
        var selected = mappings.SingleOrDefault(m => m.Id == mappingId)
            ?? throw new ArgumentException("Mapping not found.");
        if (selected.ObjectType != DataVaultObjectType.Staging)
            return await RunOneAsync(selected);

        return await TrackMappingRunAsync(selected, async () =>
        {
            var completed = new HashSet<Guid> { selected.Id };
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { selected.SourceTable!.Name, selected.TargetTable!.Name };
            var included = new HashSet<Guid> { selected.Id };
            var reachableTables = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
            bool changed;
            do
            {
                changed = false;
                foreach (var mapping in mappings.Where(m => m.ObjectType != DataVaultObjectType.Staging && !included.Contains(m.Id)))
                    if (reachableTables.Contains(mapping.SourceTable!.Name) || mapping.Columns.Any(c => c.SourceMappingId is Guid parent && included.Contains(parent)))
                    {
                        included.Add(mapping.Id);
                        reachableTables.Add(mapping.TargetTable!.Name);
                        changed = true;
                    }
            } while (changed);
            var pending = mappings.Where(m => m.Id != selected.Id && included.Contains(m.Id)).ToList();
            int rows = 0;
            while (true)
            {
                var ready = pending.Where(m => m.ObjectType == DataVaultObjectType.InformationMart && m.Columns.Any(c => c.SourceMappingId.HasValue)
                        ? m.Columns.Where(c => c.SourceMappingId.HasValue).All(c => !included.Contains(c.SourceMappingId!.Value) || completed.Contains(c.SourceMappingId.Value))
                        : available.Contains(m.SourceTable!.Name))
                    .OrderBy(m => m.ObjectType switch
                    {
                        DataVaultObjectType.Hub => 0,
                        DataVaultObjectType.Reference => 1,
                        DataVaultObjectType.Link => 2,
                        DataVaultObjectType.Satellite => 3,
                        _ => 4
                    }).FirstOrDefault();
                if (ready == null)
                {
                    if (pending.Count != 0) throw new InvalidOperationException("The mapping chain has unresolved or cyclic dependencies.");
                    break;
                }
                cancellationToken.ThrowIfCancellationRequested();
                rows += await RunOneAsync(ready);
                completed.Add(ready.Id);
                available.Add(ready.TargetTable!.Name);
                pending.Remove(ready);
            }
            return rows;
        }, cancellationToken);

        Task<int> RunOneAsync(GXDataVaultTableMapping mapping) => mapping.ObjectType == DataVaultObjectType.InformationMart
            ? RefreshInformationMartAsync(database, mapping, cancellationToken)
            : TrackMappingRunAsync(mapping, () => ExecuteRawMappingAsync(database, mapping, cancellationToken), cancellationToken);
    }

    private async Task<int> ExecuteRawMappingAsync(GXDatabaseConfiguration database, GXDataVaultTableMapping mapping,
        CancellationToken cancellationToken)
    {
        if (mapping.Columns.Count == 0) throw new InvalidOperationException("Configure columns before starting this mapping.");
        var stage = (database.Mappings ?? []).FirstOrDefault(m => m.ObjectType == DataVaultObjectType.Staging &&
            (string.Equals(m.TargetTable!.Name, mapping.SourceTable!.Name, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(m.SourceTable!.Name, mapping.SourceTable!.Name, StringComparison.OrdinalIgnoreCase)));
        await using var connection = _connectionFactory.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        var schema = new GXSchemaManager(connection);
        var source = schema.Describe(stage?.TargetTable!.Name ?? mapping.SourceTable!.Name);
        var target = schema.Describe(mapping.TargetTable!.Name);
        if (string.Equals(source.ToString(), target.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and target must differ for this mapping.");
        var sourceMapping = new GXDataVaultTableMapping
        {
            Columns = stage?.Columns.Count > 0 ? stage.Columns : source.Columns.Select(c => new GXDataVaultColumnMapping
            { SourceColumn = c.Name, TargetColumn = c.Name, Role = DataVaultColumnRole.Attribute }).ToList()
        };
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        int rows = await CopyStagingToRawVaultAsync(connection, transaction, database.Type, true,
            sourceMapping, source, mapping, target, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    private async Task<int> ExecuteInformationMartAsync(
        GXDatabaseConfiguration database,
        GXDataVaultTableMapping mart,
        CancellationToken cancellationToken)
    {
        if (GXInformationMartBuilder.HasColumnMappings(mart))
        {
            await using var connection = _connectionFactory.CreateConnection(database);
            await connection.OpenAsync(cancellationToken);
            var schema = new GXSchemaManager(connection);
            var plan = new GXInformationMartBuilder(database.Mappings ?? [], schema.Describe, database.Type).Build(mart);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            int rows = await GXInformationMartBuilder.PopulateAsync(connection, transaction, plan, cancellationToken, replace: true);
            await transaction.CommitAsync(cancellationToken);
            return rows;
        }

        using var databaseContext = GXEventLogContext.BeginDatabase(
            _databaseIndexes.TryGetValue(database, out int index) ? index : GXEventLogContext.DatabaseIndex);
        List<GXDataVaultTableMapping> stagingMappings = ResolveStagingMappings(database, mart);
        int copiedRows = await RefreshInformationMartAsync(database, stagingMappings, mart, cancellationToken);

        _logger.LogInformation(
            "Data Vault Information Mart {TargetTable} refreshed from {StagingTables}. Copied {RowCount} rows.",
            Target(mart).Name,
            string.Join(", ", stagingMappings.Select(mapping => Target(mapping).Name)),
            copiedRows);
        return copiedRows;
    }

    /// <summary>
    /// Refresh information mart by first loading staging rows into raw Data Vault tables and then loading the mart from those tables.
    /// </summary>
    /// <remarks>
    /// Database is doing the copying with select. 
    /// It's faster than reading the data into memory and then writing it back to the database.
    /// </remarks>
    /// <param name="database">The database configuration.</param>
    /// <param name="staging">The staging table mapping.</param>
    /// <param name="mart">The mart table mapping.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows copied.</returns>
    private async Task<int> RefreshInformationMartAsync(
        GXDatabaseConfiguration database,
        IReadOnlyList<GXDataVaultTableMapping> stagingMappings,
        GXDataVaultTableMapping mart,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = _connectionFactory.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        GXSchemaManager schemaManager = new(guruxConnection);
        Dictionary<string, GXTableSchema> stagingSchemas = stagingMappings.ToDictionary(
            mapping => Target(mapping).Name,
            mapping => schemaManager.Describe(Target(mapping).Name),
            StringComparer.OrdinalIgnoreCase);
        GXTableSchema martSchema = schemaManager.Describe(Target(mart).Name);
        List<GXDataVaultTableMapping> rawVaultMappings = ResolveRawVaultMappings(database, mart);
        bool historyEnabled = UsesRawVaultHistory(rawVaultMappings);
        Dictionary<string, GXTableSchema> rawVaultSchemas = rawVaultMappings.ToDictionary(
            mapping => Target(mapping).Name,
            mapping => schemaManager.Describe(Target(mapping).Name),
            StringComparer.OrdinalIgnoreCase);

        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (GXDataVaultTableMapping rawVaultMapping in rawVaultMappings)
            {
                GXTableSchema rawVaultSchema = rawVaultSchemas[Target(rawVaultMapping).Name];
                if (!historyEnabled)
                {
                    guruxConnection.Truncate(transaction, rawVaultSchema.Name);
                }
                foreach (GXDataVaultTableMapping staging in stagingMappings)
                {
                    await CopyStagingToRawVaultAsync(
                        connection,
                        transaction,
                        database.Type,
                        historyEnabled,
                        staging,
                        stagingSchemas[Target(staging).Name],
                        rawVaultMapping,
                        rawVaultSchema,
                        cancellationToken);
                }
            }

            guruxConnection.Truncate(transaction, martSchema.Name);
            await CopyRawVaultToInformationMartAsync(
                connection,
                transaction,
                rawVaultMappings,
                rawVaultSchemas,
                mart,
                martSchema,
                historyEnabled,
                cancellationToken);
            int rows = await CountRowsAsync(connection, transaction, martSchema.Name, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return rows;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<int> CountRowsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        GXDbConnection guruxConnection = new(connection);
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}";
        object? count = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }

    private static List<GXDataVaultTableMapping> ResolveRawVaultMappings(
        GXDatabaseConfiguration database,
        GXDataVaultTableMapping mart)
    {
        List<GXDataVaultTableMapping> mappings = (database.Mappings ?? [])
            .Where(mapping =>
                mapping.ObjectType != DataVaultObjectType.Staging &&
                mapping.ObjectType != DataVaultObjectType.InformationMart &&
                string.Equals(Source(mapping).Name, Source(mart).Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (mappings.Count == 0)
        {
            throw new InvalidOperationException(
                $"Data Vault raw mappings for Information Mart '{Target(mart).Name}' and source '{Source(mart).Name}' were not found.");
        }

        return mappings;
    }

    private static bool UsesRawVaultHistory(IReadOnlyList<GXDataVaultTableMapping> rawVaultMappings)
    {
        return rawVaultMappings.Any(mapping =>
            mapping.ObjectType == DataVaultObjectType.Satellite &&
            mapping.Columns.Any(column => column.Role == DataVaultColumnRole.HashDiff)) ||
            rawVaultMappings.Any(mapping =>
                mapping.ObjectType == DataVaultObjectType.Link &&
                mapping.Columns.Any(column => column.Role == DataVaultColumnRole.RecordSource));
    }

    private async Task<int> CopyStagingToRawVaultAsync(
        DbConnection connection,
        DbTransaction transaction,
        DatabaseType databaseType,
        bool historyEnabled,
        GXDataVaultTableMapping staging,
        GXTableSchema stagingSchema,
        GXDataVaultTableMapping rawVault,
        GXTableSchema rawVaultSchema,
        CancellationToken cancellationToken)
    {
        List<(string SourceColumn, string StagingColumn)> sourceColumns = [];
        var generatedMetadata = new List<GXDataVaultColumnMapping>();
        foreach (GXDataVaultColumnMapping rawVaultColumn in rawVault.Columns)
        {
            if (rawVaultColumn.Role is DataVaultColumnRole.LoadDate or DataVaultColumnRole.RecordSource)
            {
                var mapped = staging.Columns.FirstOrDefault(c => string.Equals(c.SourceColumn, rawVaultColumn.SourceColumn, StringComparison.OrdinalIgnoreCase));
                if (mapped == null || !stagingSchema.Columns.Any(c => string.Equals(c.Name, mapped.TargetColumn, StringComparison.OrdinalIgnoreCase)))
                {
                    if (rawVaultColumn.Role == DataVaultColumnRole.LoadDate &&
                        stagingSchema.Columns.FirstOrDefault(c => string.Equals(c.Name, "LOAD_DATE", StringComparison.OrdinalIgnoreCase)) is { } receivedLoadDateColumn)
                    {
                        if (!sourceColumns.Any(c => string.Equals(c.SourceColumn, rawVaultColumn.SourceColumn, StringComparison.OrdinalIgnoreCase)))
                            sourceColumns.Add((rawVaultColumn.SourceColumn, receivedLoadDateColumn.Name));
                        continue;
                    }
                    if (rawVaultColumn.Role == DataVaultColumnRole.RecordSource &&
                        stagingSchema.Columns.FirstOrDefault(c => string.Equals(c.Name, "RECORD_SOURCE", StringComparison.OrdinalIgnoreCase)) is { } recordSourceColumn)
                    {
                        if (!sourceColumns.Any(c => string.Equals(c.SourceColumn, rawVaultColumn.SourceColumn, StringComparison.OrdinalIgnoreCase)))
                            sourceColumns.Add((rawVaultColumn.SourceColumn, recordSourceColumn.Name));
                        continue;
                    }
                    generatedMetadata.Add(rawVaultColumn);
                    continue;
                }
            }
            AddSourceColumn(sourceColumns, staging, stagingSchema, rawVaultColumn.SourceColumn);
        }
        foreach (GXDataVaultColumnMapping attributeColumn in rawVault.Columns.Where(column => column.Role == DataVaultColumnRole.Attribute))
        {
            AddSourceColumn(sourceColumns, staging, stagingSchema, attributeColumn.SourceColumn);
        }

        if (sourceColumns.Count == 0 || rawVault.Columns.Count == 0)
        {
            return 0;
        }

        StringBuilder sql = new();
        sql.Append("SELECT DISTINCT ");
        sql.AppendLine(string.Join(", ", sourceColumns.Select(column => QuoteIdentifier(column.StagingColumn))));
        sql.Append("FROM ");
        sql.Append(QuoteIdentifier(stagingSchema.Name));
        string? loadDateColumn = ResolveOptionalLoadDateColumn(rawVault, staging, stagingSchema);
        if (loadDateColumn is not null)
        {
            sql.Append(" ORDER BY ");
            sql.Append(QuoteIdentifier(loadDateColumn));
        }

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql.ToString();
        List<Dictionary<string, object?>> rows = [];
        DateTimeOffset loadDate = DateTimeOffset.Now;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Dictionary<string, object?> valuesBySourceColumn = new(StringComparer.OrdinalIgnoreCase);
            for (int pos = 0; pos != sourceColumns.Count; ++pos)
            {
                object value = reader.GetValue(pos);
                valuesBySourceColumn[sourceColumns[pos].SourceColumn] = value == DBNull.Value ? null : value;
            }
            foreach (var metadata in generatedMetadata)
                valuesBySourceColumn[metadata.SourceColumn] = metadata.Role == DataVaultColumnRole.LoadDate ? loadDate : stagingSchema.ToString();
            rows.Add(valuesBySourceColumn);
        }

        await reader.CloseAsync();
        int insertedRows = 0;
        Dictionary<string, RawVaultRow> latestRowsByKey = new(StringComparer.Ordinal);
        foreach (Dictionary<string, object?> valuesBySourceColumn in rows)
        {
            RawVaultRow row = CreateRawVaultRow(rawVault, rawVaultSchema, valuesBySourceColumn);
            latestRowsByKey[string.Join("||", row.KeyValues.Select(NormalizeHashComponent))] = row;
        }

        foreach (RawVaultRow row in latestRowsByKey.Values)
        {
            if (historyEnabled && rawVault.ObjectType == DataVaultObjectType.Hub &&
                await RawVaultRowExistsAsync(connection, transaction, databaseType, rawVaultSchema.Name, row.KeyColumns, row.KeyValues, cancellationToken))
            {
                continue;
            }

            if (historyEnabled && rawVault.ObjectType == DataVaultObjectType.Link &&
                await RawVaultRowExistsAsync(connection, transaction, databaseType, rawVaultSchema.Name, row.KeyColumns, row.KeyValues, cancellationToken))
            {
                continue;
            }

            if (historyEnabled && rawVault.ObjectType == DataVaultObjectType.Satellite &&
                rawVault.Columns.Any(column => column.Role == DataVaultColumnRole.HashDiff) &&
                !await ShouldInsertSatelliteHistoryRowAsync(connection, transaction, databaseType, rawVault, rawVaultSchema, row, cancellationToken))
            {
                continue;
            }

            if (!historyEnabled || rawVault.ObjectType is not (DataVaultObjectType.Hub or DataVaultObjectType.Link or DataVaultObjectType.Satellite))
            {
                await DeleteRawVaultRowAsync(
                connection,
                transaction,
                databaseType,
                rawVaultSchema.Name,
                    row.KeyColumns,
                    row.KeyValues,
                cancellationToken);
            }
            await InsertRawVaultRowAsync(
                connection,
                transaction,
                databaseType,
                rawVaultSchema.Name,
                row.TargetColumns,
                row.Values,
                cancellationToken);
            ++insertedRows;
        }
        return insertedRows;
    }

    private sealed record RawVaultRow(
        IReadOnlyList<string> TargetColumns,
        IReadOnlyList<object?> Values,
        IReadOnlyList<string> KeyColumns,
        IReadOnlyList<object?> KeyValues);

    private static void AddSourceColumn(
        List<(string SourceColumn, string StagingColumn)> sourceColumns,
        GXDataVaultTableMapping staging,
        GXTableSchema stagingSchema,
        string sourceColumn)
    {
        if (sourceColumns.Any(column =>
            string.Equals(column.SourceColumn, sourceColumn, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        GXDataVaultColumnMapping stagingColumn = staging.Columns.FirstOrDefault(column =>
            string.Equals(column.SourceColumn, sourceColumn, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Staging column for source column '{sourceColumn}' was not found.");
        sourceColumns.Add((
            sourceColumn,
            ResolveColumnName(stagingSchema, stagingColumn.TargetColumn)));
    }

    private RawVaultRow CreateRawVaultRow(
        GXDataVaultTableMapping rawVault,
        GXTableSchema rawVaultSchema,
        IReadOnlyDictionary<string, object?> valuesBySourceColumn)
    {
        List<string> targetColumns = [];
        List<object?> values = [];
        List<string> keyColumns = [];
        List<object?> keyValues = [];
        foreach (GXDataVaultColumnMapping rawVaultColumn in rawVault.Columns)
        {
            string targetColumn = ResolveColumnName(rawVaultSchema, rawVaultColumn.TargetColumn);
            targetColumns.Add(targetColumn);
            object? rawVaultValue = GetRawVaultValue(rawVault, rawVaultColumn, valuesBySourceColumn);
            var physicalColumn = rawVaultSchema.Columns.Single(c => string.Equals(c.Name, targetColumn, StringComparison.OrdinalIgnoreCase));
            if (GXDataVaultHashStorage.IsHash(rawVaultColumn.Role))
                rawVaultValue = GXDataVaultHashStorage.Encode((string)rawVaultValue!, physicalColumn, _hashAlgorithm);
            if (rawVaultColumn.Role == DataVaultColumnRole.LoadDate && physicalColumn.Type == typeof(DateTime) && rawVaultValue is DateTimeOffset timestamp)
                rawVaultValue = timestamp.DateTime;
            if (rawVaultColumn.Role == DataVaultColumnRole.RecordSource && rawVaultValue is string recordSource &&
                physicalColumn.MaxLength > 0 && recordSource.Length > physicalColumn.MaxLength)
                throw new InvalidOperationException($"Record source value exceeds the {physicalColumn.MaxLength}-character limit of '{targetColumn}'.");
            values.Add(rawVaultValue);
            if (IsRawVaultSnapshotKeyColumn(rawVault.ObjectType, rawVaultColumn.Role))
            {
                keyColumns.Add(targetColumn);
                keyValues.Add(rawVaultValue);
            }
        }

        return new RawVaultRow(targetColumns, values, keyColumns, keyValues);
    }

    private object? GetRawVaultValue(
        GXDataVaultTableMapping rawVault,
        GXDataVaultColumnMapping rawVaultColumn,
        IReadOnlyDictionary<string, object?> valuesBySourceColumn)
    {
        if (rawVaultColumn.Role == DataVaultColumnRole.HashDiff)
        {
            return ComputeDataVaultHash(rawVault.Columns
                .Where(column => column.Role == DataVaultColumnRole.Attribute)
                .Select(column => valuesBySourceColumn[column.SourceColumn])
                .ToArray());
        }
        if (rawVaultColumn.Role == DataVaultColumnRole.LinkHashKey)
        {
            return ComputeDataVaultHash(rawVault.Columns
                .Where(column => column.Role == DataVaultColumnRole.ParentHashKey)
                .Select(column => ComputeDataVaultHash(valuesBySourceColumn[column.SourceColumn]))
                .ToArray());
        }

        object? value = valuesBySourceColumn[rawVaultColumn.SourceColumn];
        return ShouldHashDataVaultColumn(rawVaultColumn.Role) ? ComputeDataVaultHash(value) : value;
    }

    private static string? ResolveOptionalLoadDateColumn(
        GXDataVaultTableMapping rawVault,
        GXDataVaultTableMapping staging,
        GXTableSchema stagingSchema)
    {
        GXDataVaultColumnMapping? rawLoadDateColumn = rawVault.Columns.FirstOrDefault(column =>
            column.Role == DataVaultColumnRole.LoadDate);
        if (rawLoadDateColumn is null)
        {
            return null;
        }

        GXDataVaultColumnMapping? stagingColumn = staging.Columns.FirstOrDefault(column =>
            string.Equals(column.SourceColumn, rawLoadDateColumn.SourceColumn, StringComparison.OrdinalIgnoreCase));
        return stagingColumn is null ? null : stagingSchema.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, stagingColumn.TargetColumn, StringComparison.OrdinalIgnoreCase))?.Name;
    }

    private static async Task DeleteRawVaultRowAsync(
        DbConnection connection,
        DbTransaction transaction,
        DatabaseType databaseType,
        string tableName,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<object?> keyValues,
        CancellationToken cancellationToken)
    {
        if (keyColumns.Count == 0)
        {
            return;
        }

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;

        List<string> conditions = [];
        for (int pos = 0; pos != keyColumns.Count; ++pos)
        {
            string placeholder = GXSqlParameterHelper.GetPlaceholder(databaseType, $"k{pos}");
            conditions.Add($"{QuoteIdentifier(keyColumns[pos])} = {placeholder}");

            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = GXSqlParameterHelper.GetParameterName(placeholder);
            parameter.Value = GXSqlParameterHelper.NormalizeParameterValue(databaseType, keyValues[pos]) ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        command.CommandText = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE {string.Join(" AND ", conditions)}";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> RawVaultRowExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        DatabaseType databaseType,
        string tableName,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<object?> keyValues,
        CancellationToken cancellationToken)
    {
        if (keyColumns.Count == 0)
        {
            return false;
        }

        await using DbCommand command = CreateKeyLookupCommand(
            connection,
            transaction,
            databaseType,
            tableName,
            keyColumns,
            keyValues,
            "SELECT COUNT(*)");
        object? count = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(count, CultureInfo.InvariantCulture) != 0;
    }

    private static async Task<bool> ShouldInsertSatelliteHistoryRowAsync(
        DbConnection connection,
        DbTransaction transaction,
        DatabaseType databaseType,
        GXDataVaultTableMapping rawVault,
        GXTableSchema rawVaultSchema,
        RawVaultRow row,
        CancellationToken cancellationToken)
    {
        string hashDiffColumn = ResolveColumnName(
            rawVaultSchema,
            rawVault.Columns.Single(column => column.Role == DataVaultColumnRole.HashDiff).TargetColumn);
        string loadDateColumn = ResolveColumnName(
            rawVaultSchema,
            rawVault.Columns.Single(column => column.Role == DataVaultColumnRole.LoadDate).TargetColumn);

        await using DbCommand command = CreateKeyLookupCommand(
            connection,
            transaction,
            databaseType,
            rawVaultSchema.Name,
            row.KeyColumns,
            row.KeyValues,
            $"SELECT {QuoteIdentifier(hashDiffColumn)}");
        command.CommandText += $" ORDER BY {QuoteIdentifier(loadDateColumn)} DESC";

        int hashDiffIndex = row.TargetColumns
            .Select((column, index) => new { column, index })
            .Single(item => string.Equals(item.column, hashDiffColumn, StringComparison.OrdinalIgnoreCase))
            .index;
        object? incomingHashDiff = row.Values[hashDiffIndex];
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return true;
        }

        object? latestHashDiff = reader.GetValue(0);
        if (latestHashDiff == DBNull.Value)
        {
            latestHashDiff = null;
        }
        return !string.Equals(
            NormalizeHashComponent(incomingHashDiff),
            NormalizeHashComponent(latestHashDiff),
            StringComparison.Ordinal);
    }

    private static DbCommand CreateKeyLookupCommand(
        DbConnection connection,
        DbTransaction transaction,
        DatabaseType databaseType,
        string tableName,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<object?> keyValues,
        string selectClause)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;

        List<string> conditions = [];
        for (int pos = 0; pos != keyColumns.Count; ++pos)
        {
            string placeholder = GXSqlParameterHelper.GetPlaceholder(databaseType, $"lk{pos}");
            conditions.Add($"{QuoteIdentifier(keyColumns[pos])} = {placeholder}");

            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = GXSqlParameterHelper.GetParameterName(placeholder);
            parameter.Value = GXSqlParameterHelper.NormalizeParameterValue(databaseType, keyValues[pos]) ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        command.CommandText = $"{selectClause} FROM {QuoteIdentifier(tableName)} WHERE {string.Join(" AND ", conditions)}";
        return command;
    }

    private static async Task InsertRawVaultRowAsync(
        DbConnection connection,
        DbTransaction transaction,
        DatabaseType databaseType,
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyList<object?> values,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;

        List<string> placeholders = [];
        for (int pos = 0; pos != values.Count; ++pos)
        {
            string placeholder = GXSqlParameterHelper.GetPlaceholder(databaseType, $"p{pos}");
            placeholders.Add(placeholder);

            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = GXSqlParameterHelper.GetParameterName(placeholder);
            parameter.Value = GXSqlParameterHelper.NormalizeParameterValue(databaseType, values[pos]) ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        StringBuilder sql = new();
        sql.Append("INSERT ");
        sql.Append("INTO ");
        sql.Append(QuoteIdentifier(tableName));
        sql.Append(" (");
        sql.Append(string.Join(", ", columns.Select(QuoteIdentifier)));
        sql.Append(") VALUES (");
        sql.Append(string.Join(", ", placeholders));
        sql.Append(')');
        command.CommandText = sql.ToString();

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool ShouldHashDataVaultColumn(DataVaultColumnRole? role)
    {
        return role is DataVaultColumnRole.HashKey or DataVaultColumnRole.ParentHashKey;
    }

    private static bool IsRawVaultSnapshotKeyColumn(
        DataVaultObjectType? objectType, DataVaultColumnRole? role)
    {
        return objectType switch
        {
            DataVaultObjectType.Hub => role == DataVaultColumnRole.HashKey,
            DataVaultObjectType.Link => role == DataVaultColumnRole.LinkHashKey,
            DataVaultObjectType.Satellite => role == DataVaultColumnRole.ParentHashKey,
            DataVaultObjectType.Reference => role == DataVaultColumnRole.BusinessKey,
            _ => false,
        };
    }

    private string ComputeDataVaultHash(params object?[] values)
    {
        string input = string.Join("||", values.Select(value =>
            NormalizeHashComponent(value)));
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        return _hashAlgorithm switch
        {
            HashAlgorithmType.SHA256 => Convert.ToHexString(SHA256.HashData(bytes)),
            HashAlgorithmType.SHA512 => Convert.ToHexString(SHA512.HashData(bytes)),
            HashAlgorithmType.MD5 => Convert.ToHexString(MD5.HashData(bytes)),
            _ => throw new NotSupportedException($"Hash algorithm '{_hashAlgorithm}' is not supported."),
        };
    }

    private static string NormalizeHashComponent(object? value)
    {
        if (value is byte[] bytes) return Convert.ToHexString(bytes);
        return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    private static async Task CopyRawVaultToInformationMartAsync(
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyList<GXDataVaultTableMapping> rawVaultMappings,
        IReadOnlyDictionary<string, GXTableSchema> rawVaultSchemas,
        GXDataVaultTableMapping mart,
        GXTableSchema martSchema,
        bool historyEnabled,
        CancellationToken cancellationToken)
    {
        GXDataVaultTableMapping baseMapping = rawVaultMappings.FirstOrDefault(mapping => mapping.ObjectType == DataVaultObjectType.Hub)
            ?? rawVaultMappings[0];
        List<GXDataVaultTableMapping> joinedMappings = [baseMapping];
        joinedMappings.AddRange(rawVaultMappings.Where(mapping => !ReferenceEquals(mapping, baseMapping)));
        Dictionary<GXDataVaultTableMapping, string> aliases = joinedMappings
            .Select((mapping, index) => new { mapping, alias = $"t{index}" })
            .ToDictionary(item => item.mapping, item => item.alias);

        List<string> targetColumns = [];
        List<string> sourceColumns = [];
        foreach (GXDataVaultColumnMapping martColumn in mart.Columns)
        {
            (GXDataVaultTableMapping mapping, GXDataVaultColumnMapping column) = ResolveRawVaultColumn(rawVaultMappings, martColumn);
            GXTableSchema sourceSchema = rawVaultSchemas[Target(mapping).Name];
            string sourceColumn = ResolveColumnName(sourceSchema, column.TargetColumn);
            string targetColumn = ResolveColumnName(martSchema, martColumn.TargetColumn);
            targetColumns.Add(QuoteIdentifier(targetColumn));
            sourceColumns.Add($"{aliases[mapping]}.{QuoteIdentifier(sourceColumn)}");
        }

        StringBuilder sql = new();
        sql.Append("INSERT ");
        sql.Append("INTO ");
        sql.Append(QuoteIdentifier(martSchema.Name));
        sql.Append(" (");
        sql.Append(string.Join(", ", targetColumns));
        sql.AppendLine(")");
        sql.Append("SELECT DISTINCT ");
        sql.AppendLine(string.Join(", ", sourceColumns));
        sql.Append("FROM ");
        sql.Append(QuoteIdentifier(rawVaultSchemas[Target(baseMapping).Name].Name));
        sql.Append(' ');
        sql.AppendLine(aliases[baseMapping]);

        foreach (GXDataVaultTableMapping mapping in joinedMappings.Skip(1))
        {
            string joinCondition = CreateJoinCondition(baseMapping, mapping, rawVaultSchemas, aliases);
            string latestSatelliteCondition = historyEnabled
                ? CreateLatestSatelliteCondition(mapping, rawVaultSchemas, aliases)
                : string.Empty;
            if (joinCondition.Length != 0 && latestSatelliteCondition.Length != 0)
            {
                joinCondition += " AND " + latestSatelliteCondition;
            }
            else if (latestSatelliteCondition.Length != 0)
            {
                joinCondition = latestSatelliteCondition;
            }
            if (joinCondition.Length == 0)
            {
                sql.Append("CROSS JOIN ");
            }
            else
            {
                sql.Append("INNER JOIN ");
            }
            sql.Append(QuoteIdentifier(rawVaultSchemas[Target(mapping).Name].Name));
            sql.Append(' ');
            sql.Append(aliases[mapping]);
            if (joinCondition.Length != 0)
            {
                sql.Append(" ON ");
                sql.Append(joinCondition);
            }
            sql.AppendLine();
        }

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql.ToString();
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static (GXDataVaultTableMapping Mapping, GXDataVaultColumnMapping Column) ResolveRawVaultColumn(
        IReadOnlyList<GXDataVaultTableMapping> rawVaultMappings,
        GXDataVaultColumnMapping martColumn)
    {
        return rawVaultMappings
            .SelectMany(mapping => mapping.Columns
                .Where(column => string.Equals(column.SourceColumn, martColumn.SourceColumn, StringComparison.OrdinalIgnoreCase))
                .Select(column => (Mapping: mapping, Column: column)))
            .OrderBy(candidate => GetRawVaultColumnPriority(candidate.Mapping, candidate.Column, martColumn))
            .FirstOrDefault() is var match && match.Mapping is not null
                ? match
                : throw new InvalidOperationException(
                    $"Raw Data Vault column for Information Mart source column '{martColumn.SourceColumn}' was not found.");
    }

    private static int GetRawVaultColumnPriority(
        GXDataVaultTableMapping mapping,
        GXDataVaultColumnMapping column,
        GXDataVaultColumnMapping martColumn)
    {
        if (martColumn.Role == DataVaultColumnRole.LoadDate && column.Role == DataVaultColumnRole.LoadDate)
        {
            return 0;
        }
        if (martColumn.Role == DataVaultColumnRole.BusinessKey && mapping.ObjectType == DataVaultObjectType.Hub)
        {
            return column.Role == DataVaultColumnRole.BusinessKey ? 0 : 1;
        }
        if (martColumn.Role == DataVaultColumnRole.Attribute && column.Role == DataVaultColumnRole.Attribute)
        {
            return 0;
        }
        if (mapping.ObjectType == DataVaultObjectType.Hub)
        {
            return 1;
        }

        return 2;
    }

    private static string CreateJoinCondition(
        GXDataVaultTableMapping baseMapping,
        GXDataVaultTableMapping mapping,
        IReadOnlyDictionary<string, GXTableSchema> rawVaultSchemas,
        IReadOnlyDictionary<GXDataVaultTableMapping, string> aliases)
    {
        GXTableSchema baseSchema = rawVaultSchemas[Target(baseMapping).Name];
        GXTableSchema schema = rawVaultSchemas[Target(mapping).Name];
        List<string> conditions = [];
        foreach (GXDataVaultColumnMapping baseColumn in baseMapping.Columns)
        {
            GXDataVaultColumnMapping? matchingColumn = mapping.Columns.FirstOrDefault(column =>
                CanJoinRawVaultColumns(baseColumn, column));
            if (matchingColumn is null)
            {
                continue;
            }

            string baseColumnName = ResolveColumnName(baseSchema, baseColumn.TargetColumn);
            string columnName = ResolveColumnName(schema, matchingColumn.TargetColumn);
            conditions.Add($"{aliases[baseMapping]}.{QuoteIdentifier(baseColumnName)} = {aliases[mapping]}.{QuoteIdentifier(columnName)}");
        }

        return string.Join(" AND ", conditions);
    }

    private static bool CanJoinRawVaultColumns(
        GXDataVaultColumnMapping baseColumn,
        GXDataVaultColumnMapping column)
    {
        if (!string.Equals(baseColumn.SourceColumn, column.SourceColumn, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsHashJoinColumn(baseColumn.Role) && IsHashJoinColumn(column.Role);
    }

    private static bool IsHashJoinColumn(DataVaultColumnRole? role)
    {
        return role is DataVaultColumnRole.HashKey or DataVaultColumnRole.ParentHashKey;
    }

    private static string CreateLatestSatelliteCondition(
        GXDataVaultTableMapping mapping,
        IReadOnlyDictionary<string, GXTableSchema> rawVaultSchemas,
        IReadOnlyDictionary<GXDataVaultTableMapping, string> aliases)
    {
        if (mapping.ObjectType != DataVaultObjectType.Satellite ||
            !mapping.Columns.Any(column => column.Role == DataVaultColumnRole.HashDiff))
        {
            return string.Empty;
        }

        GXDataVaultColumnMapping loadDate = mapping.Columns.Single(column => column.Role == DataVaultColumnRole.LoadDate);
        List<GXDataVaultColumnMapping> parentKeys = mapping.Columns
            .Where(column => column.Role == DataVaultColumnRole.ParentHashKey)
            .ToList();
        if (parentKeys.Count == 0)
        {
            return string.Empty;
        }

        GXTableSchema schema = rawVaultSchemas[Target(mapping).Name];
        string alias = aliases[mapping];
        string subAlias = alias + "_latest";
        string loadDateColumn = ResolveColumnName(schema, loadDate.TargetColumn);
        List<string> keyConditions = parentKeys
            .Select(column =>
            {
                string keyColumn = ResolveColumnName(schema, column.TargetColumn);
                return $"{subAlias}.{QuoteIdentifier(keyColumn)} = {alias}.{QuoteIdentifier(keyColumn)}";
            })
            .ToList();

        return $"{alias}.{QuoteIdentifier(loadDateColumn)} = (SELECT MAX({subAlias}.{QuoteIdentifier(loadDateColumn)}) FROM {QuoteIdentifier(schema.Name)} {subAlias} WHERE {string.Join(" AND ", keyConditions)})";
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static List<GXDataVaultTableMapping> ResolveStagingMappings(
        GXDatabaseConfiguration database,
        GXDataVaultTableMapping mart)
    {
        List<GXDataVaultTableMapping> mappings = (database.Mappings ?? []).Where(mapping =>
            mapping.ObjectType == DataVaultObjectType.Staging &&
            string.Equals(Source(mapping).Name, Source(mart).Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (mappings.Count == 0)
        {
            throw new InvalidOperationException(
                $"Staging mapping for Information Mart '{Target(mart).Name}' and source '{Source(mart).Name}' was not found.");
        }

        return mappings;
    }

    private static List<(string StagingColumn, string MartColumn)> ResolveColumnPairs(
        GXDataVaultTableMapping staging,
        GXDataVaultTableMapping mart,
        GXTableSchema stagingSchema,
        GXTableSchema martSchema)
    {
        List<(string StagingColumn, string MartColumn)> columns = [];
        foreach (GXDataVaultColumnMapping martColumn in mart.Columns)
        {
            GXDataVaultColumnMapping stagingColumn = staging.Columns.FirstOrDefault(column =>
                string.Equals(column.SourceColumn, martColumn.SourceColumn, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Staging column for source column '{martColumn.SourceColumn}' was not found.");

            string actualStagingColumn = ResolveColumnName(stagingSchema, stagingColumn.TargetColumn);
            string actualMartColumn = ResolveColumnName(martSchema, martColumn.TargetColumn);
            columns.Add((actualStagingColumn, actualMartColumn));
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Information Mart mapping '{Target(mart).Name}' does not contain columns.");
        }

        return columns;
    }

    private static string ResolveColumnName(GXTableSchema table, string name)
    {
        return table.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase))?.Name
            ?? throw new InvalidOperationException($"Column '{name}' was not found in table '{table.Name}'.");
    }

    private static List<GXDataVaultMartRun> GetScheduledInformationMarts(GXDataVaultConfiguration configuration)
    {
        return configuration.GetDatabases()
            .SelectMany(database => (database.Mappings ?? [])
                .Where(mapping => (mapping.ObjectType is DataVaultObjectType.InformationMart or DataVaultObjectType.Staging) &&
                    mapping.Schedule != null && mapping.Schedule.Type != ScheduleType.Manual)
                .Select(mapping => new GXDataVaultMartRun(database, mapping)))
            .ToList();
    }

    private static GXTableConfiguration CreateStagingTableConfiguration(
        GXDataVaultTableMapping staging,
        GXSchedule schedule)
    {
        return new GXTableConfiguration
        {
            Name = Target(staging).Name,
            // Empty column selection makes the notifier inspect all physical Stage columns.
            Columns = [],
            Keys = staging.Columns
                .Where(column => column.Role == DataVaultColumnRole.BusinessKey)
                .Select(column => column.TargetColumn)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Schedule = schedule,
        };
    }

    private static DateTimeOffset GetInitialRun(GXSchedule schedule, DateTimeOffset now)
    {
        return schedule.Type switch
        {
            ScheduleType.Interval => now,
            ScheduleType.Daily => GetNextDailyRun(schedule, now, allowNow: true),
            ScheduleType.Cron => GetNextCronRun(schedule, now, includeCurrent: true),
            ScheduleType.Continuous => now,
            _ => now,
        };
    }

    private static DateTimeOffset GetNextRun(
        GXSchedule schedule,
        DateTimeOffset now)
    {
        return schedule.Type switch
        {
            ScheduleType.Interval => now.AddSeconds(schedule.IntervalSeconds ?? throw new InvalidOperationException("IntervalSeconds is required for interval schedules.")),
            ScheduleType.Daily => GetNextDailyRun(schedule, now, allowNow: false),
            ScheduleType.Cron => GetNextCronRun(schedule, now, includeCurrent: false),
            ScheduleType.Continuous => now.AddSeconds(1),
            _ => now,
        };
    }

    private static DateTimeOffset GetNextDailyRun(GXSchedule schedule,
        DateTimeOffset now,
        bool allowNow)
    {
        if (!TimeOnly.TryParse(schedule.Time, out TimeOnly time))
        {
            throw new InvalidOperationException("Daily schedule requires a valid Time value.");
        }

        DateTimeOffset candidate = new(now.Year, now.Month, now.Day, time.Hour, time.Minute, time.Second, now.Offset);
        if (candidate < now || (!allowNow && candidate == now))
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }

    private static DateTimeOffset GetNextCronRun(
        GXSchedule schedule,
        DateTimeOffset now,
        bool includeCurrent)
    {
        if (string.IsNullOrWhiteSpace(schedule.Expression))
        {
            throw new InvalidOperationException("Cron schedule requires an Expression value.");
        }

        DateTime[] dates = GXDateTime.GetNextScheduledDates(now.UtcDateTime,
            new GXDateTime(schedule.Expression, CultureInfo.GetCultureInfo("en-US")), includeCurrent ? 1 : 2);
        DateTime next = includeCurrent ? dates.FirstOrDefault() : dates.Skip(1).FirstOrDefault();
        return next == default
            ? throw new InvalidOperationException("Cron schedule did not produce a next occurrence.")
            : new DateTimeOffset(DateTime.SpecifyKind(next, DateTimeKind.Utc));
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
}




