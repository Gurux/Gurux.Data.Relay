using Gurux.Service.Orm.Common.Model;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Input;
using Gurux.Data.Relay.Shared.Protocol;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Server;

public sealed class GXTableMappingService : ITableMappingService
{
    private readonly IGXConfigurationService _configurationService;
    private readonly ILogger<GXTableMappingService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GXTableMappingService(
        IGXConfigurationService configurationService,
        ILogger<GXTableMappingService> logger)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    public async Task<string> GetOrCreateDestinationTableAsync(GXDataMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            GXServerConfiguration configuration = await _configurationService.LoadServerAsync(cancellationToken)
                ?? throw new InvalidOperationException("Server configuration was not found.");
            string? configuredDestination = ResolveConfiguredDestinationTable(configuration, message.Table.Name);
            if (configuredDestination is not null)
            {
                return configuredDestination;
            }

            GXServerState state = await _configurationService.LoadServerStateAsync(cancellationToken) ?? new GXServerState();
            GXTableMapping? existing = state.TableMappings.FirstOrDefault(mapping =>
                string.Equals(mapping.Source, message.Table.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing.Destination;
            }

            string destination = PromptForDestination(message);
            state.TableMappings.Add(new GXTableMapping
            {
                Source = message.Table.Name,
                Destination = destination,
            });
            await _configurationService.SaveServerStateAsync(state, cancellationToken);
            _logger.LogInformation("Saved table mapping {Source} -> {Destination} to {Path}.", message.Table.Name, destination, _configurationService.GetStatePath(ApplicationMode.Server));
            return destination;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? ResolveConfiguredDestinationTable(GXServerConfiguration configuration, string sourceTable)
    {
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetTargetDatabases();
        if (GXServerDatabaseContext.DatabaseIndex is int index)
        {
            if (index < 0 || index >= databases.Count)
            {
                throw new InvalidOperationException($"Server database index {index} is outside configured Databases.");
            }

            return FindTable(databases[index], sourceTable);
        }

        foreach (GXDatabaseConfiguration database in databases)
        {
            string? table = FindTable(database, sourceTable);
            if (table is not null)
            {
                return table;
            }
        }

        return null;
    }

    private static string? FindTable(GXDatabaseConfiguration database, string sourceTable)
    {
        return database.Tables?
            .FirstOrDefault(table => string.Equals(table.Name, sourceTable, StringComparison.OrdinalIgnoreCase))
            ?.Name;
    }

    private static string PromptForDestination(GXDataMessage message)
    {
        Console.WriteLine();
        Console.WriteLine($"New source table received: {message.Table.Name}");
        Console.WriteLine();
        foreach (GXColumnSchema column in message.Table.Columns)
        {
            Console.WriteLine($"{column.Name,-20} {column.Type}");
        }

        while (true)
        {
            string? value = GXConsoleInput.ReadLine($"Destination table [{message.Table.Name}]: ");
            if (string.IsNullOrWhiteSpace(value))
            {
                return message.Table.Name;
            }

            value = value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
    }
}

