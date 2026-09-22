using Gurux.Service.Orm.Common.Model;
using Gurux.Data.Relay.Configuration;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Microsoft.Extensions.Logging;

namespace Gurux.Data.Relay.Database;

public sealed class GXDatabaseMetadataService : IGXDatabaseMetadataService
{
    private readonly IGXDatabaseConnectionFactory _connectionFactory;
    private readonly ILogger<GXDatabaseMetadataService> _logger;

    public GXDatabaseMetadataService(
        IGXDatabaseConnectionFactory connectionFactory,
        ILogger<GXDatabaseMetadataService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GXTableSchema>> GetTablesAsync(
        GXDatabaseConfiguration configuration,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> tableNames = await GetTableNamesAsync(configuration, cancellationToken);
        List<GXTableSchema> tables = [];
        foreach (string tableName in tableNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GXTableSchema? table = await TryDescribeTableAsync(configuration, tableName, cancellationToken);
            if (table is not null)
            {
                tables.Add(table);
            }
        }

        return tables;
    }

    public async Task<IReadOnlyList<string>> GetTableNamesAsync(
        GXDatabaseConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection(configuration);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        GXSchemaManager schemaManager = new(guruxConnection);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (IReadOnlyList<string>)schemaManager.GetTables()
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    public async Task<GXTableSchema> DescribeTableAsync(
        GXDatabaseConfiguration configuration,
        string tableName,
        CancellationToken cancellationToken)
    {
        GXTableSchema? table = await TryDescribeTableAsync(configuration, tableName, cancellationToken, throwOnError: true);
        if (table is null)
        {
            throw new InvalidOperationException($"Table '{tableName}' schema could not be read.");
        }
        return table;
    }

    private async Task<GXTableSchema?> TryDescribeTableAsync(
        GXDatabaseConfiguration configuration,
        string tableName,
        CancellationToken cancellationToken,
        bool throwOnError = false)
    {
        await using var connection = _connectionFactory.CreateConnection(configuration);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        GXSchemaManager schemaManager = new(guruxConnection);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            GXTableSchema table;
            try
            {
                table = schemaManager.Describe(tableName);
            }
            catch (Exception ex)
            {
                if (throwOnError)
                {
                    throw new InvalidOperationException($"Table '{tableName}' schema could not be read: {ex.Message}", ex);
                }
                _logger.LogWarning("Skipping table {TableName} because its schema could not be read: {Message}", tableName, ex.Message);
                return null;
            }

            if (table.Columns.Count == 0)
            {
                _logger.LogWarning("Skipping table {TableName} because no columns were found.", tableName);
                return null;
            }
            return table;
        }, cancellationToken);
    }
}

