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
using Gurux.Data.Relay.Shared.Mcp;
using System.Text.Json;

namespace Gurux.Data.Relay.Web.Server.Mcp;

/// <summary>Shared input limits for read-only Data Vault discovery operations.</summary>
public sealed class GXDataVaultDiscoveryService(IGXDatabaseCatalogService catalog, IGXDatabaseMetadataService metadata, GXTableDataReader dataReader)
{
    /// <summary>Lists physical tables for a configured database.</summary>
    public async Task<IReadOnlyList<GXDataVaultMcpTable>> ListTablesAsync(Guid databaseId, CancellationToken cancellationToken)
    {
        GXDatabaseConfiguration database = await GetDatabaseAsync(databaseId, cancellationToken);
        return (await metadata.GetTableNamesAsync(database, cancellationToken))
            .Select(name => new GXDataVaultMcpTable { Name = name }).ToArray();
    }

    /// <summary>Gets safe physical schema metadata for one configured table.</summary>
    public async Task<GXDataVaultMcpSchema> GetTableSchemaAsync(Guid databaseId, string table, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(table))
        {
            throw new ArgumentException("A table name is required.", nameof(table));
        }
        GXDatabaseConfiguration database = await GetDatabaseAsync(databaseId, cancellationToken);
        var schema = await metadata.DescribeTableAsync(database, table, cancellationToken);
        GXDataVaultMcpSchema result = new() { Table = schema.ToString() };
        foreach (var column in schema.Columns)
        {
            result.Columns.Add(new GXDataVaultMcpColumn
            {
                Name = column.Name,
                DataType = column.Type?.Name ?? column.DbType?.ToString() ?? "Unknown",
                Nullable = column.IsNullable,
                Identity = column.IsIdentity || column.IsAutoIncrement || column.IsGenerated,
                MaxLength = column.MaxLength,
                Precision = column.Precision,
                Scale = column.Scale,
                DefaultValue = column.DefaultValue?.ToString()
            });
            if (column.IsPrimaryKey)
            {
                result.PrimaryKey.Add(column.Name);
            }
        }
        return result;
    }

    /// <summary>Returns a small masked source-data sample.</summary>
    public async Task<GXDataVaultMcpSample> GetSampleRowsAsync(Guid databaseId, string table, int limit, CancellationToken cancellationToken)
    {
        ValidateSampleLimit(limit);
        GXDatabaseConfiguration database = await GetDatabaseAsync(databaseId, cancellationToken);
        var data = await dataReader.ReadAsync(database, table, 0, limit, cancellationToken);
        GXDataVaultMcpSample result = new();
        result.Columns.AddRange(data.Columns);
        foreach (var source in data.Rows)
        {
            Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < data.Columns.Count; index++)
            {
                string column = data.Columns[index];
                row[column] = IsSensitive(column) ? "***" : source.Values[index].Deserialize<object>();
            }
            result.Rows.Add(row);
        }
        return result;
    }

    /// <summary>Returns table row and column counts without exporting table data.</summary>
    public async Task<GXDataVaultMcpTableStatistics> GetTableStatisticsAsync(Guid databaseId, string table, CancellationToken cancellationToken)
    {
        GXDatabaseConfiguration database = await GetDatabaseAsync(databaseId, cancellationToken);
        var schema = await metadata.DescribeTableAsync(database, table, cancellationToken);
        var page = await dataReader.ReadAsync(database, table, 0, 1, cancellationToken);
        return new GXDataVaultMcpTableStatistics
        {
            Table = schema.ToString(),
            RowCount = page.TotalCount,
            ColumnCount = schema.Columns.Count
        };
    }

    private static bool IsSensitive(string name) => name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) || name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("apikey", StringComparison.OrdinalIgnoreCase) || name.Contains("api_key", StringComparison.OrdinalIgnoreCase);

    private async Task<GXDatabaseConfiguration> GetDatabaseAsync(Guid databaseId, CancellationToken cancellationToken)
    {
        var database = (await catalog.GetDatabasesAsync(cancellationToken)).SingleOrDefault(item => item.Id == databaseId)
            ?? throw new ArgumentException("Database was not found.", nameof(databaseId));
        return new GXDatabaseConfiguration
        {
            Id = database.Id,
            Type = database.Type.Value,
            ConnectionString = database.ConnectionString,
            Description = database.Description
        };
    }

    /// <summary>Validates the bounded sample row count accepted by MCP discovery tools.</summary>
    public static void ValidateSampleLimit(int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Use a sample limit from 1 to 100.");
        }
    }
}
