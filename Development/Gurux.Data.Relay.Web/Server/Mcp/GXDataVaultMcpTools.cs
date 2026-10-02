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
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared.Mcp;
using Gurux.Data.Relay.Web.Server.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Gurux.Data.Relay.Web.Server.Mcp;

/// <summary>Data Vault discovery and explicit configuration-write MCP tools.</summary>
[McpServerToolType]
public sealed class GXDataVaultMcpTools(IGXDatabaseCatalogService catalog, GXDataVaultDiscoveryService discovery, GXDataVaultWriteService writer, IGXRelayAdministrationService administration)
{
    [McpServerTool(Name = "create_data_vault_mapping")]
    [Description("Create one Data Vault mapping using explicit source/target columns and roles. Set createIfMissing to create a missing target from the provider-neutral schema. Validates before saving and rejects duplicates. This modifies the database and configuration.")]
    public Task<GXDataVaultMcpApplyResult> CreateDataVaultMappingAsync(GXDataVaultMcpMappingRequest request, CancellationToken cancellationToken)
        => writer.CreateAsync(request, cancellationToken);

    [McpServerTool(Name = "apply_model")]
    [Description("Apply an explicitly approved Hub/Link/Hub-parent Satellite/Reference model. Set model.createIfMissing to create missing target tables from generated schemas. Information Mart definitions are accepted only when they are backed by an explicitly selected Hub and existing mapped vault objects. Validates the whole model then saves all mappings in one configuration transaction. Supports single-column Business Keys and same-Stage relationships. Rejects existing mappings and unsupported definitions without saving. This modifies the database and configuration.")]
    public Task<GXDataVaultMcpApplyResult> ApplyModelAsync(GXDataVaultModel model, CancellationToken cancellationToken)
        => writer.ApplyAsync(model, cancellationToken);

    [McpServerTool(Name = "create_information_mart")]
    [Description("Create an Information Mart from an explicitly selected source Hub, Link, or Reference grain. Set definition.grain to Hub, Link, or Reference and provide sourceLink or sourceReference for those grains. The target table is created when missing; columns may be restricted or renamed. This modifies the database and configuration.")]
    public Task<GXDataVaultMcpApplyResult> CreateInformationMartAsync(Guid database, GXDataVaultMartDefinition definition, CancellationToken cancellationToken)
        => writer.CreateMartAsync(database, definition, cancellationToken);

    [McpServerTool(Name = "list_server_transports")]
    [Description("List configured Server transports and their source-to-destination routes. Connection secrets are never returned.")]
    public async Task<IReadOnlyList<GXMcpTransportInfo>> ListServerTransportsAsync(CancellationToken cancellationToken)
    {
        var settings = await administration.GetServerSettingsAsync(cancellationToken);
        return settings.Transports.Select(t => new GXMcpTransportInfo { Id = t.Id, Type = t.Type.ToString(), Host = t.Host, Port = t.Port, Database = t.Database, Routes = t.Tables.Select(r => $"{r.Source ?? r.Table} -> {r.Table}").ToArray() }).ToArray();
    }

    [McpServerTool(Name = "create_server_transport")]
    [Description("Create a TCP Server listener. Omit database and table names for a listener without routes. For a table route, supply a database ID from list_transport_databases, sourceTable and destinationTable. The selected catalog database is added to Server settings automatically, as in the UI. This modifies Server configuration.")]
    public async Task<GXMcpTransportInfo> CreateServerTransportAsync(GXMcpServerTransportRequest request, CancellationToken cancellationToken)
    {
        if (request.Port is < 1 or > 65535) throw new ArgumentException("Port must be between 1 and 65535.");
        bool hasRoute = !string.IsNullOrWhiteSpace(request.SourceTable) || !string.IsNullOrWhiteSpace(request.DestinationTable);
        if (hasRoute && (string.IsNullOrWhiteSpace(request.SourceTable) || string.IsNullOrWhiteSpace(request.DestinationTable) || request.Database is null || request.Database == Guid.Empty))
            throw new ArgumentException("A table route requires a catalog database, SourceTable and DestinationTable. Omit both table names to create a listener without routes.");
        var settings = await administration.GetServerSettingsAsync(cancellationToken);
        if (settings.Transports.Any(t => t.Type == TransportType.Tcp && t.Port == request.Port))
            throw new ArgumentException("A TCP transport already uses this port.");
        if (request.Database is Guid databaseId && databaseId != Guid.Empty)
        {
            var selected = (await catalog.GetDatabasesAsync(cancellationToken)).SingleOrDefault(d => d.Id == databaseId)
                ?? throw new ArgumentException("Select an available database from list_transport_databases.");
            if (!settings.Databases.Any(d => d.Id == databaseId))
                settings.Databases.AddRange(GXConfigurationMapper.FromEntities<GXServerConfiguration>(new Gurux.Data.Relay.Shared.GXSettings
                { Mode = ApplicationMode.Server, Databases = [selected] }).Databases);
        }
        var transport = new GXTransportConfiguration { Type = TransportType.Tcp, Host = request.Host, Port = request.Port, Database = request.Database == Guid.Empty ? null : request.Database, Description = $"MCP TCP {request.Port}", Tables = hasRoute ? [new() { DatabaseId = request.Database!.Value, Table = request.DestinationTable.Trim(), Source = request.SourceTable.Trim() }] : [] };
        settings.Transports.Add(transport);
        await administration.UpdateServerSettingsAsync(settings, cancellationToken);
        return new() { Id = transport.Id, Type = transport.Type.ToString(), Host = transport.Host, Port = transport.Port, Database = transport.Database, Routes = transport.Tables.Select(r => $"{r.Source} -> {r.Table}").ToArray() };
    }

    [McpServerTool(Name = "list_transport_databases")]
    [Description("List databases from the shared catalog used by the Server transport editor. Returns IDs, descriptions and providers without connection secrets.")]
    public async Task<IReadOnlyList<GXDataVaultMcpDatabase>> ListTransportDatabasesAsync(CancellationToken cancellationToken)
        => (await catalog.GetDatabasesAsync(cancellationToken)).Select(d => new GXDataVaultMcpDatabase
        { Id = d.Id, Description = d.Description, Provider = d.Type.ToString() }).ToArray();

    /// <summary>Lists configured databases without their connection settings.</summary>
    [McpServerTool(Name = "list_databases")]
    [Description("List configured databases that are available for Data Vault discovery. Connection strings and credentials are never returned.")]
    public async Task<IReadOnlyList<GXDataVaultMcpDatabase>> ListDatabasesAsync(CancellationToken cancellationToken)
    {
        var dataVault = await administration.GetDataVaultSettingsAsync(cancellationToken);
        return dataVault.Databases.Select(database => new GXDataVaultMcpDatabase
        {
            Id = database.Id,
            Description = database.Description,
            Provider = database.Type.ToString()
        }).ToArray();
    }

    /// <summary>Lists tables in a configured database.</summary>
    [McpServerTool(Name = "list_tables")]
    [Description("List table names in a configured source database. Use this before requesting schema, statistics, or sample rows for a table.")]
    public Task<IReadOnlyList<GXDataVaultMcpTable>> ListTablesAsync(Guid database, CancellationToken cancellationToken)
        => discovery.ListTablesAsync(database, cancellationToken);

    /// <summary>Returns safe schema metadata for one table.</summary>
    [McpServerTool(Name = "get_table_schema")]
    [Description("Return column definitions and primary-key metadata for one source table. No table data or connection settings are returned.")]
    public Task<GXDataVaultMcpSchema> GetTableSchemaAsync(Guid database, string table, CancellationToken cancellationToken)
        => discovery.GetTableSchemaAsync(database, table, cancellationToken);

    /// <summary>Returns at most one hundred masked sample rows.</summary>
    [McpServerTool(Name = "get_sample_rows")]
    [Description("Return a bounded sample of up to 100 rows from one source table for planning. Values in columns with sensitive names are masked.")]
    public Task<GXDataVaultMcpSample> GetSampleRowsAsync(Guid database, string table, int limit, CancellationToken cancellationToken)
        => discovery.GetSampleRowsAsync(database, table, limit, cancellationToken);

    /// <summary>Returns inexpensive row and column counts for one table.</summary>
    [McpServerTool(Name = "get_table_statistics")]
    [Description("Return the total row count and schema column count for one source table to help assess its Data Vault modelling scope.")]
    public Task<GXDataVaultMcpTableStatistics> GetTableStatisticsAsync(Guid database, string table, CancellationToken cancellationToken)
        => discovery.GetTableStatisticsAsync(database, table, cancellationToken);
}
