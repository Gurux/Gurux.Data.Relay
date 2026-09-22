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

using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Database;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Channels;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Log;
using Gurux.Data.Relay.Shared.Scheduling;

namespace Gurux.Data.Relay.Client;

public sealed class GXTransferScheduler : ITransferScheduler
{
    private static GXSchedule Schedule(GXTableConfiguration table) => table.Schedule
        ?? throw new InvalidOperationException($"Table '{table.Name}' requires a schedule.");
    private sealed record GXClientTableRun(
        GXDatabaseConfiguration Database,
        int DatabaseIndex,
        int DatabaseCount,
        GXTableConfiguration Table,
        string StateTableName);

    private readonly IClientBatchSender _clientBatchSender;
    private readonly IGXConfigurationService _configurationService;
    private readonly IDatabaseChangeNotifierFactory _databaseChangeNotifierFactory;
    private readonly ILogger<GXTransferScheduler> _logger;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _runGates = new(StringComparer.OrdinalIgnoreCase);

    public Task<int> RunTableNowAsync(GXClientConfiguration configuration, int databaseIndex, string tableName, CancellationToken cancellationToken)
    {
        GXClientTableRun table = GetTableRuns(configuration).SingleOrDefault(item =>
            item.DatabaseIndex == databaseIndex && item.Table.Name == tableName)
            ?? throw new InvalidOperationException("The selected table was not found. Refresh the table list.");
        if (configuration.GetTransports(table.DatabaseIndex, table.Table.Name).Count == 0)
            throw new InvalidOperationException("The selected table has no configured transports.");
        return RunWithRetryAsync(configuration, table, cancellationToken, true);
    }

    public GXTransferScheduler(
        IClientBatchSender clientBatchSender,
        IGXConfigurationService configurationService,
        ILogger<GXTransferScheduler> logger)
        : this(
            clientBatchSender,
            configurationService,
            logger,
            new GXDatabaseChangeNotifierFactory(new GXDatabaseConnectionFactory(), logger))
    {
    }

    public GXTransferScheduler(
        IClientBatchSender clientBatchSender,
        IGXConfigurationService configurationService,
        ILogger<GXTransferScheduler> logger,
        IDatabaseChangeNotifierFactory databaseChangeNotifierFactory)
    {
        _clientBatchSender = clientBatchSender;
        _configurationService = configurationService;
        _databaseChangeNotifierFactory = databaseChangeNotifierFactory;
        _logger = logger;
    }

    public async Task<int> RunAsync(GXClientConfiguration configuration, CancellationToken cancellationToken)
    {
        List<GXClientTableRun> tableRuns = GetTableRuns(configuration);
        List<GXClientTableRun> manualTables = tableRuns
            .Where(item => Schedule(item.Table).Type == ScheduleType.Manual)
            .ToList();
        List<GXClientTableRun> databaseChangeTables = tableRuns
            .Where(item => Schedule(item.Table).Type == ScheduleType.DatabaseChange)
            .ToList();
        List<GXClientTableRun> scheduledTables = tableRuns
            .Where(item => Schedule(item.Table).Type != ScheduleType.Manual &&
                Schedule(item.Table).Type != ScheduleType.DatabaseChange)
            .ToList();

        int sentMessages = 0;
        foreach (GXClientTableRun tableRun in manualTables)
        {
            sentMessages += await RunWithRetryAsync(configuration, tableRun, cancellationToken);
        }

        if (scheduledTables.Count == 0)
        {
            if (databaseChangeTables.Count == 0)
            {
                return sentMessages;
            }

            return sentMessages + await RunDatabaseChangeAsync(configuration, databaseChangeTables, cancellationToken);
        }

        Task<int>? databaseChangeTask = databaseChangeTables.Count == 0
            ? null
            : RunDatabaseChangeAsync(configuration, databaseChangeTables, cancellationToken);

        Dictionary<string, DateTimeOffset> nextRuns = scheduledTables.ToDictionary(
            item => item.StateTableName,
            item => GetInitialRun(Schedule(item.Table), DateTimeOffset.UtcNow),
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Task<int>> running = new(StringComparer.OrdinalIgnoreCase);

        foreach (var tableRun in scheduledTables)
            await UpdateTableStateAsync(tableRun.StateTableName, tableRun.DatabaseIndex,
                state => state.NextTransferTime = nextRuns[tableRun.StateTableName], cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (KeyValuePair<string, Task<int>> item in running.ToArray())
                {
                    if (!item.Value.IsCompleted)
                    {
                        continue;
                    }

                    sentMessages += await item.Value;
                    running.Remove(item.Key);
                }

                DateTimeOffset now = DateTimeOffset.UtcNow;
                foreach (GXClientTableRun tableRun in scheduledTables)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (nextRuns[tableRun.StateTableName] > now)
                    {
                        continue;
                    }

                    if (running.TryGetValue(tableRun.StateTableName, out Task<int>? active) && !active.IsCompleted)
                    {
                        GXTransferConfiguration transfer = GXClientTransferSettings.GetEffective(configuration.GetTransports(tableRun.DatabaseIndex, tableRun.Table.Name));
                        if (!transfer.AllowConcurrentRuns)
                        {
                            _logger.LogWarning("Skipping scheduled run for table {TableName} because the previous run is still in progress.", tableRun.StateTableName);
                            await UpdateTableStateAsync(
                                tableRun.StateTableName, tableRun.DatabaseIndex,
                                state =>
                                {
                                    state.LastAttemptedTransfer = now;
                                    state.LastRunStatus = TransferRunStatus.SkippedOverlap;
                                    state.SkippedRunCount += 1;
                                    state.NextTransferTime = GetNextRun(Schedule(tableRun.Table), now);
                                },
                                cancellationToken);
                            nextRuns[tableRun.StateTableName] = GetNextRun(Schedule(tableRun.Table), now);
                            continue;
                        }
                    }

                    nextRuns[tableRun.StateTableName] = GetNextRun(Schedule(tableRun.Table), now);
                    await UpdateTableStateAsync(tableRun.StateTableName, tableRun.DatabaseIndex,
                        state => state.NextTransferTime = nextRuns[tableRun.StateTableName], cancellationToken);
                    running[tableRun.StateTableName] = RunWithRetryAsync(configuration, tableRun, cancellationToken);
                }

                DateTimeOffset nextDue = nextRuns.Values.Min();
                TimeSpan delay = nextDue - DateTimeOffset.UtcNow;
                if (delay < TimeSpan.FromSeconds(1))
                {
                    delay = TimeSpan.FromSeconds(1);
                }

                _logger.LogDebug("Next scheduled transfer in {Delay}.", delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
        finally
        {
            // A settings reload must wait for in-flight transfers to stop before starting a new scheduler.
            try { await Task.WhenAll(running.Values); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            if (databaseChangeTask is not null)
            {
                try
                {
                    await databaseChangeTask;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            }
        }

        return sentMessages;
    }

    private async Task<int> RunDatabaseChangeAsync(
        GXClientConfiguration configuration,
        IReadOnlyList<GXClientTableRun> tableRuns,
        CancellationToken cancellationToken)
    {
        Channel<GXClientTableRun> changes = Channel.CreateUnbounded<GXClientTableRun>();
        List<IDatabaseChangeSubscription> subscriptions = [];

        try
        {
            foreach (GXClientTableRun tableRun in tableRuns)
            {
                IDatabaseChangeSubscription subscription = await _databaseChangeNotifierFactory.CreateAsync(
                    tableRun.Database,
                    tableRun.Table,
                    cancellationToken);
                subscription.Changed += (_, args) =>
                {
                    if (string.IsNullOrWhiteSpace(args.Table) ||
                        string.Equals(args.Table, tableRun.Table.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        changes.Writer.TryWrite(tableRun);
                    }
                };
                subscriptions.Add(subscription);
                await subscription.StartAsync(cancellationToken);
                _logger.LogInformation("Started database change monitoring for table {TableName}.", tableRun.StateTableName);
            }

            int sentMessages = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                GXClientTableRun tableRun = await changes.Reader.ReadAsync(cancellationToken);
                _logger.LogInformation("Database change detected for table {TableName}.", tableRun.StateTableName);
                sentMessages += await RunWithRetryAsync(configuration, tableRun, cancellationToken);
            }

            return sentMessages;
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

    private async Task<int> RunWithRetryAsync(
        GXClientConfiguration configuration,
        GXClientTableRun tableRun,
        CancellationToken cancellationToken,
        bool reportFailure = false)
    {
        using var databaseContext = GXEventLogContext.BeginDatabase(tableRun.DatabaseIndex);
        SemaphoreSlim? gate = null;
        if (!GXClientTransferSettings.GetEffective(configuration.GetTransports(tableRun.DatabaseIndex, tableRun.Table.Name)).AllowConcurrentRuns)
        {
            gate = _runGates.GetOrAdd(tableRun.StateTableName, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0, cancellationToken))
            {
                if (reportFailure) throw new InvalidOperationException("This table is already running.");
                return 0;
            }
        }
        try { return await RunWithRetryCoreAsync(configuration, tableRun, cancellationToken, reportFailure); }
        finally { gate?.Release(); }
    }

    private async Task<int> RunWithRetryCoreAsync(
        GXClientConfiguration configuration,
        GXClientTableRun tableRun,
        CancellationToken cancellationToken,
        bool reportFailure)
    {
        GXTransferConfiguration transfer = GXClientTransferSettings.GetEffective(configuration.GetTransports(tableRun.DatabaseIndex, tableRun.Table.Name));
        int attempts = Math.Max(1, transfer.RetryCount);
        TimeSpan retryDelay = TimeSpan.FromSeconds(Math.Max(1, transfer.RetryDelaySeconds));

        for (int attempt = 1; attempt <= attempts; ++attempt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UpdateTableStateAsync(
                tableRun.StateTableName, tableRun.DatabaseIndex,
                state =>
                {
                    state.LastAttemptedTransfer = DateTimeOffset.UtcNow;
                },
                cancellationToken);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                int sent = await _clientBatchSender.SendTableAsync(configuration, tableRun.DatabaseIndex, tableRun.Table, cancellationToken);
                stopwatch.Stop();
                await UpdateTableStateAsync(
                    tableRun.StateTableName, tableRun.DatabaseIndex,
                    state =>
                    {
                        state.LastRunStatus = TransferRunStatus.Succeeded;
                        state.LastFailure = null;
                        state.ConsecutiveFailures = 0;
                        state.LastSentMessageCount = sent;
                        state.LastRunDurationMs = stopwatch.ElapsedMilliseconds;
                    },
                    cancellationToken);
                return sent;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                stopwatch.Stop();
                await UpdateTableStateAsync(
                    tableRun.StateTableName, tableRun.DatabaseIndex,
                    state =>
                    {
                        state.LastRunStatus = TransferRunStatus.Failed;
                        state.LastFailure = ex.Message;
                        state.ConsecutiveFailures += 1;
                        state.LastSentMessageCount = 0;
                        state.LastRunDurationMs = stopwatch.ElapsedMilliseconds;
                    },
                    cancellationToken);
                _logger.LogWarning(ex, "Table {TableName} transfer attempt {Attempt}/{Attempts} failed. Retrying in {Delay}.", tableRun.StateTableName, attempt, attempts, retryDelay);
                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await UpdateTableStateAsync(
                    tableRun.StateTableName, tableRun.DatabaseIndex,
                    state =>
                    {
                        state.LastRunStatus = TransferRunStatus.Failed;
                        state.LastFailure = ex.Message;
                        state.ConsecutiveFailures += 1;
                        state.LastSentMessageCount = 0;
                        state.LastRunDurationMs = stopwatch.ElapsedMilliseconds;
                    },
                    cancellationToken);
                _logger.LogError(ex, "Table {TableName} transfer failed after {Attempts} attempts.", tableRun.StateTableName, attempts);
                if (reportFailure) throw new InvalidOperationException($"Table '{tableRun.Table.Name}' transfer failed: {ex.Message}", ex);
                return 0;
            }
        }

        return 0;
    }

    private static List<GXClientTableRun> GetTableRuns(GXClientConfiguration configuration)
    {
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetSourceDatabases();
        List<GXClientTableRun> runs = [];
        for (int databaseIndex = 0; databaseIndex < databases.Count; ++databaseIndex)
        {
            GXDatabaseConfiguration database = databases[databaseIndex];
            foreach (GXTableConfiguration table in database.Tables ?? [])
            {
                if (configuration.GetTransports(databaseIndex, table.Name).Count == 0) continue;
                runs.Add(new GXClientTableRun(
                    database,
                    databaseIndex,
                    databases.Count,
                    table,
                    GXClientTableStateNames.Get(table.Name, databaseIndex, databases.Count)));
            }
        }

        return runs;
    }

    private async Task UpdateTableStateAsync(
        string tableName,
        int databaseIndex,
        Action<GXClientTableState> update,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            GXClientState state = await _configurationService.LoadClientStateAsync(cancellationToken) ?? new GXClientState();
            GXClientTableState? tableState = state.Tables.FirstOrDefault(table =>
                string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase));
            if (tableState is null)
            {
                tableState = new GXClientTableState
                {
                    Name = tableName,
                };
                state.Tables.Add(tableState);
            }

            tableState.DatabaseIndex = databaseIndex;
            update(tableState);
            await _configurationService.SaveClientStateAsync(state, cancellationToken);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private static DateTimeOffset GetInitialRun(GXSchedule schedule, DateTimeOffset now)
        => GXScheduleTiming.Initial(schedule, now) ?? now;

    private static DateTimeOffset GetNextRun(GXSchedule schedule, DateTimeOffset now)
        => GXScheduleTiming.Next(schedule, now) ?? now;

}



