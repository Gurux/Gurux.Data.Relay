using Gurux.Service.Orm.Common.Model;
using System.Diagnostics;
using Gurux.Data.Relay.Client;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Transport;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Log;
using Microsoft.Extensions.Logging;
using GXClientTableState = Gurux.Data.Relay.Configuration.GXClientTableState;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared.Protocol;

namespace Gurux.Data.Relay.Web.Server.Services;

public sealed class GXClientTableActions(
    IGXConfigurationService configurationService,
    IClientSchemaSender schemaSender,
    IGXTransportFactory transportFactory,
    IGXEventLogService? eventLog = null)
{
    public async Task TestTransportAsync(Guid transportId, CancellationToken cancellationToken)
    {
        var configuration = await configurationService.LoadClientAsync(cancellationToken)
            ?? throw new InvalidOperationException("Client configuration was not found.");
        var settings = configuration.Transports.SingleOrDefault(transport => transport.Id == transportId)
            ?? throw new InvalidOperationException("The selected transport was not found. Refresh the transport list.");
        GXDataMessage message = new() { MessageType = MessageType.Ping };
        var tables = settings.Tables.Select(route =>
            (Index: configuration.Databases.FindIndex(database => database.Id == route.DatabaseId), Name: route.Table))
            .Where(table => table.Index >= 0 && !string.IsNullOrWhiteSpace(table.Name)).Distinct().ToList();
        await SaveTestAttemptAsync(message.MessageId, configuration.Databases.Count, tables, cancellationToken);
        await using IDataTransport transport = transportFactory.Create(settings);
        await transport.ConnectAsync(cancellationToken);
        await transport.SendAsync(message, cancellationToken);
        GXDataAcknowledgement acknowledgement = await transport.ReceiveAcknowledgementAsync(cancellationToken);
        if (acknowledgement.MessageId != message.MessageId)
            throw new InvalidOperationException("The server acknowledged a different message.");
        if (acknowledgement.Status != AcknowledgementStatus.Ok)
            throw new InvalidOperationException($"Server connection test failed: {acknowledgement.Error}");
    }

    public async Task RunTransportAsync(Guid transportId, GXTransferScheduler scheduler, CancellationToken cancellationToken)
    {
        using var mode = GXEventLogContext.BeginMode(ApplicationMode.Client);
        int? databaseIndex = null;
        string? tableName = null;
        try
        {
            var loaded = await configurationService.LoadClientAsync(cancellationToken);
            if (eventLog != null)
                await eventLog.InitializeAsync(loaded ?? new GXClientConfiguration(), cancellationToken);
            if (loaded == null)
                throw new InvalidOperationException("Client configuration was not found.");
            var configuration = System.Text.Json.JsonSerializer.Deserialize<GXClientConfiguration>(
                System.Text.Json.JsonSerializer.Serialize(loaded))!;
            var transport = configuration.Transports.SingleOrDefault(t => t.Id == transportId)
                ?? throw new InvalidOperationException("The selected transport was not found. Refresh the transport list.");
            if (transport.Tables.Count == 0) throw new InvalidOperationException("The selected transport has no tables.");
            var tables = transport.Tables.Select(route =>
            {
                int index = configuration.Databases.FindIndex(d => d.Id == route.DatabaseId);
                if (index < 0 || !(configuration.Databases[index].Tables ?? []).Any(t => t.Name == route.Table))
                    throw new InvalidOperationException($"Table '{route.Table}' is no longer configured.");
                return (Index: index, Name: route.Table);
            }).Distinct().ToList();
            configuration.Transports = [transport];
            foreach (var table in tables)
            {
                databaseIndex = table.Index;
                tableName = table.Name;
                await scheduler.RunTableNowAsync(configuration, table.Index, table.Name, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            eventLog?.Write(LogLevel.Error, nameof(GXClientTableActions), new EventId(0, "SendNowFailed"),
                $"Send Now failed for transport '{transportId}': {ex.Message}", ex,
                new Dictionary<string, object?>
                {
                    ["TransportId"] = transportId,
                    ["DatabaseIndex"] = databaseIndex,
                    ["TableName"] = tableName
                });
            throw;
        }
    }

    public async Task RunAsync(GXClientTableRequest request, GXTransferScheduler scheduler, CancellationToken cancellationToken)
    {
        GXClientConfiguration configuration = await configurationService.LoadClientAsync(cancellationToken)
            ?? throw new InvalidOperationException("Client configuration was not found.");
        await scheduler.RunTableNowAsync(configuration, request.DatabaseIndex, request.TableName, cancellationToken);
    }

    public async Task SendSchemaAsync(GXClientTableRequest request, CancellationToken cancellationToken)
    {
        var (database, table, transports, _) = await GetTableAsync(request, cancellationToken);
        // Use the CLI sender with a separate configuration restricted to the selected table.
        GXDatabaseConfiguration source = new()
        {
            Id = database.Id,
            Type = database.Type,
            ConnectionString = database.ConnectionString,
            Description = database.Description,
            RecordSource = database.RecordSource,
            Tables = [table]
        };
        await schemaSender.SendAsync(new GXClientConfiguration { Databases = [source], Transports = transports.ToList() }, cancellationToken);
    }

    public async Task PingAsync(GXClientTableRequest request, CancellationToken cancellationToken)
    {
        var (_, table, transports, databaseCount) = await GetTableAsync(request, cancellationToken);
        if (eventLog != null)
        {
            GXClientConfiguration configuration = await configurationService.LoadClientAsync(cancellationToken)
                ?? throw new InvalidOperationException("Client configuration was not found.");
            await eventLog.InitializeAsync(configuration, cancellationToken);
        }
        GXDataMessage message = new()
        {
            MessageType = MessageType.Ping,
            Keys = [.. table.Keys],
            Table = new GXTableSchema { Name = table.Name },
        };
        await SaveTestAttemptAsync(message.MessageId, databaseCount, [(request.DatabaseIndex, table.Name)], cancellationToken);
        foreach (GXTransportConfiguration settings in transports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using IDataTransport transport = transportFactory.Create(settings);
            await transport.ConnectAsync(cancellationToken);
            await transport.SendAsync(message, cancellationToken);
            GXDataAcknowledgement acknowledgement = await transport.ReceiveAcknowledgementAsync(cancellationToken);
            if (acknowledgement.MessageId != message.MessageId)
                throw new InvalidOperationException("The server acknowledged a different message.");
            if (acknowledgement.Status != AcknowledgementStatus.Ok)
                throw new InvalidOperationException($"Server rejected ping for table '{table.Name}': {acknowledgement.Error}");
            eventLog?.Write(LogLevel.Information, nameof(GXClientTableActions), new EventId(0, "Ping"),
                $"Ping for table '{table.Name}' acknowledged by server '{settings.Description ?? settings.Host ?? settings.Broker}'.",
                null, new Dictionary<string, object?>
                {
                    ["MessageId"] = message.MessageId,
                    ["DatabaseIndex"] = request.DatabaseIndex,
                    ["TableName"] = table.Name,
                    ["TransportType"] = settings.Type.ToString()
                });
        }

        GXClientState state = await configurationService.LoadClientStateAsync(cancellationToken) ?? new();
        string stateTableName = databaseCount == 1 ? table.Name : $"source-{request.DatabaseIndex + 1}:{table.Name}";
        GXClientTableState? tableState = state.Tables.FirstOrDefault(item =>
            string.Equals(item.Name, stateTableName, StringComparison.OrdinalIgnoreCase));
        if (tableState is null)
        {
            tableState = new GXClientTableState { Name = stateTableName };
            state.Tables.Add(tableState);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        tableState.DatabaseIndex = request.DatabaseIndex;
        tableState.LastPingMessageId = message.MessageId;
        tableState.LastPingAt = now;
        tableState.LastSuccessfulNotification = now;
        await configurationService.SaveClientStateAsync(state, cancellationToken);
    }

    private async Task SaveTestAttemptAsync(Guid messageId, int databaseCount,
        IReadOnlyList<(int Index, string Name)> tables, CancellationToken cancellationToken)
    {
        if (tables.Count == 0) return;
        GXClientState state = await configurationService.LoadClientStateAsync(cancellationToken) ?? new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var table in tables)
        {
            string name = databaseCount == 1 ? table.Name : $"source-{table.Index + 1}:{table.Name}";
            GXClientTableState? entry = state.Tables.FirstOrDefault(item =>
                (item.DatabaseIndex == null || item.DatabaseIndex == table.Index) &&
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                entry = new GXClientTableState { Name = name, DatabaseIndex = table.Index };
                state.Tables.Add(entry);
            }
            entry.DatabaseIndex = table.Index;
            entry.LastPingMessageId = messageId;
            entry.LastPingAt = now;
        }
        await configurationService.SaveClientStateAsync(state, cancellationToken);
    }

    private async Task<(GXDatabaseConfiguration Database, GXTableConfiguration Table, IReadOnlyList<GXTransportConfiguration> Transports, int DatabaseCount)> GetTableAsync(
        GXClientTableRequest request, CancellationToken cancellationToken)
    {
        GXClientConfiguration configuration = await configurationService.LoadClientAsync(cancellationToken)
            ?? throw new InvalidOperationException("Client configuration was not found.");
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetSourceDatabases();
        if (request.DatabaseIndex < 0 || request.DatabaseIndex >= databases.Count)
            throw new InvalidOperationException("The selected database was not found. Refresh the table list.");
        GXDatabaseConfiguration database = databases[request.DatabaseIndex];
        GXTableConfiguration table = database.Tables?.SingleOrDefault(item => item.Name == request.TableName)
            ?? throw new InvalidOperationException("The selected table was not found. Save the table or refresh the list.");
        IReadOnlyList<GXTransportConfiguration> transports = configuration.GetTransports(request.DatabaseIndex, table.Name);
        if (transports.Count == 0)
            throw new InvalidOperationException($"Table '{table.Name}' has no configured transports.");
        return (database, table, transports, databases.Count);
    }
}


