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

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Web.Server.Models;
using Gurux.Data.Relay.Web.Server.Services;
using Gurux.Data.Relay.Enums;
using Microsoft.AspNetCore.Mvc;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Log;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/datavault")]
public sealed class DataVaultController : ControllerBase
{
    private readonly IGXRelayAdministrationService _administrationService;

    /// <summary>Read the persisted runtime state.</summary>
    [HttpGet("state")]
    public async Task<Shared.GXState> GetState([FromServices] IGXDataVaultRuntimeStateStore store,
        CancellationToken cancellationToken)
        => new() { DataVaultMappings = await store.LoadDataVaultStateAsync(cancellationToken) };

    /// <summary>Start Mapping.</summary>
    [HttpPost("mappings/{id:guid}/start")]
    public async Task<IActionResult> StartMapping(Guid id,
        [FromServices] Database.IGXDatabaseConnectionFactory connections,
        [FromServices] IGXDataVaultRuntimeStateStore runtimeState, CancellationToken cancellationToken)
    {
        var configuration = await _administrationService.GetDataVaultSettingsAsync(cancellationToken);
        await RecoverLegacyMappingsAsync(configuration, cancellationToken);
        var database = configuration.Databases.FirstOrDefault(d => d.Mappings?.Any(m => m.Id == id) == true);
        if (database == null) return NotFound("Mapping not found.");
        var scheduler = new Gurux.Data.Relay.Client.GXDataVaultScheduler(connections, new Gurux.Data.Relay.Client.GXDatabaseChangeNotifierFactory(connections),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Gurux.Data.Relay.Client.GXDataVaultScheduler>.Instance)
        { RuntimeStateStore = runtimeState };
        try { return Ok(await scheduler.StartMappingAsync(database, id, configuration.HashAlgorithm, cancellationToken)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Problem(detail: ex.Message, statusCode: 400); }
    }

    private readonly IGXDatabaseCatalogService? _catalog;
    public DataVaultController(IGXRelayAdministrationService administrationService, IGXDatabaseCatalogService? catalog = null)
    {
        _administrationService = administrationService;
        _catalog = catalog;
    }

    /// <summary>Create Table.</summary>
    [HttpPost("databases/{databaseId:guid}/tables")]
    public async Task<IActionResult> CreateTable(Guid databaseId,
        [FromBody] Shared.GXCreateVaultTableRequest request,
        [FromServices] Database.IGXDatabaseConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        if (request.Overwrite && (request.UseExistingTable || !request.CreateIfMissing))
            return BadRequest("Overwrite requires CreateIfMissing and cannot be combined with UseExistingTable.");
        var databases = await _administrationService.GetDatabasesAsync(ApplicationMode.DataVault, cancellationToken);
        var database = databases.FirstOrDefault(item => item.Id == databaseId);
        if (database == null) return NotFound("Database not found.");
        if (string.IsNullOrWhiteSpace(request.SourceTable) || string.IsNullOrWhiteSpace(request.TargetTable))
            return BadRequest("Select a source table, target name and columns.");
        if (!request.UseExistingTable && !System.Text.RegularExpressions.Regex.IsMatch(request.TargetTable, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return BadRequest("Use letters, digits and underscores for the target table name.");
        await using var connection = connections.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        var manager = new Gurux.Service.Orm.Model.GXSchemaManager(connection);
        try
        {
            var source = manager.Describe(request.SourceTable);
            if (source.Columns.Count == 0) return BadRequest("The source table has no columns.");
            var mappings = await _administrationService.ListDataVaultMappingsAsync(cancellationToken);
            int databaseIndex = databases.ToList().FindIndex(item => item.Id == databaseId);
            var target = GXVaultTableSchemaBuilder.Build(request, source, name => manager.Describe(name),
                name => mappings.FirstOrDefault(m => m.DatabaseIndex == databaseIndex &&
                    string.Equals(m.Mapping.TargetTable.Name, name, StringComparison.OrdinalIgnoreCase)).Mapping?.ObjectType);
            Database.GXDataVaultSchemaCompatibility.Normalize(target, database.Type);
            var existingTableName = manager.GetTables().FirstOrDefault(name => string.Equals(name, target.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, target.ToString(), StringComparison.OrdinalIgnoreCase));
            bool exists = existingTableName is not null;
            if (request.UseExistingTable && !exists)
                return NotFound("The selected target table no longer exists. Select an existing table or choose Create new table.");
            if (exists && !request.UseExistingTable && !request.CreateIfMissing)
                return Conflict("The target table already exists.");
            if (request.UseExistingTable && !request.AddMapping)
                return BadRequest("Select Add mapping when using an existing table.");
            if (string.Equals(source.ToString(), target.ToString(), StringComparison.OrdinalIgnoreCase))
                return BadRequest("Source and target tables must be different.");
            if (!exists && !request.CreateIfMissing)
                return Ok(new Shared.GXCreateVaultTableResult { TableName = target.ToString(), RequiresCreation = true });
            if (exists && !request.Overwrite)
            {
                var roles = GXVaultTableMappingBuilder.Build(request, target, null).Columns;
                var hashAlgorithm = (await _administrationService.GetDataVaultSettingsAsync(cancellationToken)).HashAlgorithm;
                var existing = manager.Describe(existingTableName!);
                target.Name = existing.Name;
                target.Schema = existing.Schema;
                if (string.Equals(source.ToString(), target.ToString(), StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Source and target tables must be different.");
                foreach (var expected in target.Columns)
                {
                    var actual = existing.Columns.FirstOrDefault(c => string.Equals(c.Name, expected.Name, StringComparison.OrdinalIgnoreCase));
                    bool hash = roles.Any(c => string.Equals(c.TargetColumn, expected.Name, StringComparison.OrdinalIgnoreCase) &&
                        Gurux.Data.Relay.Client.GXDataVaultHashStorage.IsHash(c.Role));
                    var role = roles.FirstOrDefault(c => string.Equals(c.TargetColumn, expected.Name, StringComparison.OrdinalIgnoreCase))?.Role;
                    if (hash && actual is not null)
                    {
                        try { Gurux.Data.Relay.Client.GXDataVaultHashStorage.Validate(actual, hashAlgorithm); }
                        catch (ArgumentException ex) { return BadRequest(ex.Message + " The table was not changed."); }
                    }
                    // SQLite stores the generated DateTimeOffset LOAD_DATE column as TEXT.
                    bool compatibleType = hash && actual is not null || actual?.Type == expected.Type ||
                        role == DataVaultColumnRole.LoadDate && actual?.Type == typeof(DateTime) ||
                        database.Type == DatabaseType.SqLite && expected.Type == typeof(DateTimeOffset) && actual?.Type == typeof(string);
                    if (actual == null || actual.IsComputed || actual.IsGenerated || actual.IsIdentity || actual.IsAutoIncrement || !compatibleType || expected.IsPrimaryKey && !actual.IsPrimaryKey ||
                        expected.IsNullable && !actual.IsNullable ||
                        !hash && role != DataVaultColumnRole.RecordSource && expected.MaxLength > 0 && actual.MaxLength > 0 && actual.MaxLength < expected.MaxLength)
                        return BadRequest($"Existing table column {expected.Name} does not match the selected mapping (actual type: {actual?.Type?.Name}, expected: {expected.Type?.Name}; primary key: {actual?.IsPrimaryKey}, required: {expected.IsPrimaryKey}; nullable: {actual?.IsNullable}, expected: {expected.IsNullable}; length: {actual?.MaxLength}, expected: {expected.MaxLength}). The table was not changed.");
                }
                foreach (var column in existing.Columns.Where(c => !target.Columns.Any(t => string.Equals(t.Name, c.Name, StringComparison.OrdinalIgnoreCase))))
                    if (!column.IsNullable && column.DefaultValue == null && !column.IsComputed && !column.IsGenerated && !column.IsIdentity && !column.IsAutoIncrement)
                        return BadRequest($"Required target column {column.Name} is not mapped and has no default. The table was not changed.");
                // Keep only the selected columns, using the provider's actual names.
                foreach (var column in target.Columns)
                    column.Name = existing.Columns.Single(c => string.Equals(c.Name, column.Name, StringComparison.OrdinalIgnoreCase)).Name;
            }
            GXDataVaultTableMapping? mapping = null;
            if (request.AddMapping)
            {
                int index = databases.ToList().FindIndex(item => item.Id == databaseId);
                var staging = mappings.Where(item => item.DatabaseIndex == index && item.Mapping.ObjectType == DataVaultObjectType.Staging &&
                    (string.Equals(item.Mapping.TargetTable.Name, request.SourceTable, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.Mapping.TargetTable.Name, source.ToString(), StringComparison.OrdinalIgnoreCase))).Select(item => item.Mapping).ToArray();
                if (staging.Length > 1) return BadRequest("Multiple staging mappings match the source table. Select Add mapping off and configure the mapping manually.");
                mapping = GXVaultTableMappingBuilder.Build(request, target, staging.SingleOrDefault());
                if (mappings.Any(item => item.DatabaseIndex == index && item.Mapping.ObjectType == mapping.ObjectType &&
                    string.Equals(item.Mapping.SourceTable.Name, mapping.SourceTable.Name, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(item.Mapping.TargetTable.Name, target.ToString(), StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.Mapping.TargetTable.Name, target.Name, StringComparison.OrdinalIgnoreCase))))
                    return Conflict("This source and target already have a mapping. Use Modify mapping to change it.");
                foreach (var column in mapping.Columns)
                    column.TargetColumn = target.Columns.Single(c => string.Equals(c.Name, column.TargetColumn, StringComparison.OrdinalIgnoreCase)).Name;
            }
            // Validate the request and mapping before removing any existing data.
            if (exists && request.Overwrite)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Use catalog casing: the ORM checks existence before dropping the table.
                manager.DropTable(existingTableName!);
            }
            if (!exists || request.Overwrite) manager.CreateTable(target);
            var result = new Shared.GXCreateVaultTableResult { TableName = target.ToString() };
            if (mapping != null)
            {
                try
                {
                    int index = databases.ToList().FindIndex(item => item.Id == databaseId);
                    await _administrationService.CreateDataVaultMappingAsync(index, mapping, cancellationToken);
                }
                catch (Exception ex)
                {
                    result.MappingError = $"Table {target} {(exists ? "exists" : "was created")}, but its mapping could not be saved: {ex.Message}.";
                }
            }
            return Ok(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Problem(detail: ex.Message, statusCode: 400);
        }
    }

    /// <summary>Read the current settings and entity concurrency stamps.</summary>
    [HttpGet("settings")]
    public Task<GXDataVaultConfiguration> GetSettings(CancellationToken cancellationToken, [FromQuery] GXDatabase? filter = null)
    {
        return _administrationService.GetDataVaultSettingsAsync(cancellationToken, filter);
    }

    /// <summary>Replace settings, preserving IDs and concurrency stamps from the latest read.</summary>
    [HttpPut("settings")]
    [ProducesResponseType<GXDataVaultConfiguration>(200)]
    public async Task<IActionResult> UpdateSettings([FromBody] GXDataVaultConfiguration configuration, CancellationToken cancellationToken)
    {
        await _administrationService.UpdateDataVaultSettingsAsync(configuration, cancellationToken);
        return Ok(configuration);
    }

    /// <summary>List configured databases.</summary>
    [HttpGet("databases")]
    public Task<IReadOnlyList<GXDatabaseConfiguration>> GetDatabases(CancellationToken cancellationToken)
    {
        return _administrationService.GetDatabasesAsync(ApplicationMode.DataVault, cancellationToken);
    }

    /// <summary>Replace the configured database list. Use If-Match to detect concurrent edits.</summary>
    [HttpPut("databases")]
    [ProducesResponseType<IReadOnlyList<GXDatabaseConfiguration>>(200)]
    public async Task<IActionResult> SaveDatabases([FromBody] IReadOnlyList<GXDatabaseConfiguration> databases, CancellationToken cancellationToken)
    {
        string? version = await _administrationService.SaveDatabasesAsync(ApplicationMode.DataVault, databases, cancellationToken,
            Request.Headers.IfMatch.FirstOrDefault()?.Trim('"'));
        if (version is not null) Response.Headers.ETag = $"\"{version}\"";
        return Ok(databases);
    }

    /// <summary>List Data Vault mappings.</summary>
    [HttpGet("mappings")]
    public async Task<IActionResult> ListMappings(CancellationToken cancellationToken)
    {
        IReadOnlyList<(int DatabaseIndex, GXDataVaultTableMapping Mapping)> mappings = await _administrationService.ListDataVaultMappingsAsync(cancellationToken);
        return Ok(mappings.Select(item => new
        {
            item.Mapping.Database,
            item.Mapping.Id,
            item.Mapping.SourceTable,
            item.Mapping.TargetTable,
            item.Mapping.ObjectType,
            item.Mapping.Columns,
            item.Mapping.Schedule,
            item.Mapping.Updated,
        }));
    }

    /// <summary>Read one Data Vault mapping.</summary>
    [HttpGet("mappings/{id:guid}")]
    public async Task<IActionResult> GetMapping(Guid id, CancellationToken cancellationToken)
    {
        (int DatabaseIndex, GXDataVaultTableMapping Mapping)? mapping = await _administrationService.GetDataVaultMappingAsync(id, cancellationToken);
        if (mapping is null)
        {
            return NotFound();
        }

        return Ok(new GXDataVaultMappingDetails
        {
            Database = mapping.Value.Mapping.Database,
            Mapping = GXConfigurationMapper.ToEntity(mapping.Value.Mapping),
            Description = GetDataVaultObjectDescription(mapping.Value.Mapping.ObjectType),
        });
    }

    private async Task<IActionResult> SaveMappingAsync(Guid? id, Shared.GXDataVaultMappingRequest request, CancellationToken token)
    {
        try
        {
            var configuration = await _administrationService.GetDataVaultSettingsAsync(token);
            if (id.HasValue && !configuration.Databases.Any(d => d.Mappings?.Any(m => m.Id == id.Value) == true))
                return NotFound("The mapping no longer exists. Refresh the list.");
            var database = configuration.Databases.FirstOrDefault(d => d.Id == request.Database);
            if (database is null)
            {
                var catalogDatabase = _catalog is null ? null : (await _catalog.GetDatabasesAsync(token)).FirstOrDefault(d => d.Id == request.Database);
                if (catalogDatabase is null) return BadRequest("The selected database no longer exists. Refresh the database list.");
                database = new GXDatabaseConfiguration
                {
                    Id = catalogDatabase.Id,
                    Type = catalogDatabase.Type.Value,
                    ConnectionString = catalogDatabase.ConnectionString,
                    Description = catalogDatabase.Description,
                    ConcurrencyStamp = catalogDatabase.ConcurrencyStamp
                };
                configuration.Databases.Add(database);
            }
            var mapping = GXConfigurationMapper.FromEntity(request.Mapping);
            mapping.Id = id ?? (mapping.Id == Guid.Empty ? Guid.NewGuid() : mapping.Id);
            mapping.Database = database.Id;
            mapping.Updated = DateTimeOffset.UtcNow;
            if (id.HasValue)
                foreach (var item in configuration.Databases) item.Mappings?.RemoveAll(m => m.Id == id.Value);
            database.Mappings ??= [];
            database.Mappings.Add(mapping);
            await RecoverLegacyMappingsAsync(configuration, token);
            await _administrationService.UpdateDataVaultSettingsAsync(configuration, token);
            return Ok(mapping);
        }
        catch (System.Data.DBConcurrencyException ex) { return Conflict(new ProblemDetails { Detail = ex.Message }); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.DataAnnotations.ValidationException)
        { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
    }

    private async Task RecoverLegacyMappingsAsync(GXDataVaultConfiguration configuration, CancellationToken cancellationToken)
    {
        GXDataVaultTableMapping[] incomplete = configuration.GetMappings()
            .Where(mapping => mapping.ObjectType == DataVaultObjectType.Staging && mapping.Columns.Count == 0 ||
                mapping.Columns.Any(column => string.IsNullOrWhiteSpace(column.TargetColumn)))
            .ToArray();

        var schemas = new Dictionary<Guid, IReadOnlyList<Gurux.Service.Orm.Common.Model.GXTableSchema>>();
        foreach (IGrouping<Guid, GXDataVaultTableMapping> group in incomplete.GroupBy(mapping => mapping.Database))
        {
            GXDatabaseConfiguration database = configuration.Databases.SingleOrDefault(item => item.Id == group.Key)
                ?? throw new InvalidOperationException($"The mapping database '{group.Key}' was not found.");
            string[] targets = group.Select(mapping => mapping.TargetTable.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            schemas[group.Key] = await Task.WhenAll(targets.Select(target =>
                _administrationService.DescribeTableAsync(database, target, cancellationToken)));
        }
        GXDataVaultLegacyMappingRecovery.Recover(configuration.GetMappings(), schemas);
    }

    /// <summary>Create a Data Vault mapping in a shared catalog database.</summary>
    [HttpPost("mappings")]
    public Task<IActionResult> CreateMapping([FromBody] Shared.GXDataVaultMappingRequest request, CancellationToken cancellationToken)
        => SaveMappingAsync(null, request, cancellationToken);

    /// <summary>Update a Data Vault mapping.</summary>
    [HttpPut("mappings/{id:guid}")]
    public Task<IActionResult> UpdateMapping(Guid id, [FromBody] Shared.GXDataVaultMappingRequest request, CancellationToken cancellationToken)
        => SaveMappingAsync(id, request, cancellationToken);
    /// <summary>Delete a Data Vault mapping.</summary>
    [HttpDelete("mappings/{id:guid}")]
    public async Task<IActionResult> DeleteMapping(Guid id, CancellationToken cancellationToken)
    {
        bool deleted = await _administrationService.DeleteDataVaultMappingAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>Read a filtered page of event log entries.</summary>
    [HttpPost("events/page")]
    public Task<GXEventPage<GXEventLog>> GetEventPage([FromBody] Shared.GXEventPageRequest request, CancellationToken cancellationToken)
        => _administrationService.GetEventPageAsync(ApplicationMode.DataVault, request, cancellationToken);
    /// <summary>Read event log entries with optional level and database filters.</summary>
    [HttpPost("events")]
    public Task<IReadOnlyList<GXEventLog>> GetEvents([FromBody] GXEventQuery query, CancellationToken cancellationToken)
    {
        return _administrationService.GetEventsAsync(ApplicationMode.DataVault, query.Top, query.LogLevel, query.DatabaseIndex, cancellationToken);
    }

    /// <summary>Delete matching event log entries.</summary>
    [HttpDelete("events")]
    public async Task<IActionResult> ClearEvents([FromQuery] int? databaseIndex, CancellationToken cancellationToken)
    {
        int deletedRows = await _administrationService.ClearEventsAsync(ApplicationMode.DataVault, databaseIndex, cancellationToken);
        return Ok(new { Succeeded = true, DeletedRows = deletedRows });
    }

    /// <summary>Download the settings as a JSON file.</summary>
    [HttpGet("settings/export")]
    public async Task<FileContentResult> ExportSettings(CancellationToken cancellationToken)
    {
        string json = await _administrationService.ExportSettingsJsonAsync(ApplicationMode.DataVault, cancellationToken);
        return File(Encoding.UTF8.GetBytes(json), "application/json", "Gurux.Data.Relay.datavault.settings.json");
    }

    /// <summary>Import settings from a JSON document.</summary>
    [HttpPost("settings/import")]
    public async Task<IActionResult> ImportSettings([FromBody] GXSettingsImportRequest request, CancellationToken cancellationToken)
    {
        await _administrationService.ImportSettingsJsonAsync(ApplicationMode.DataVault, request.Json, request.DatabaseMap, cancellationToken);
        return Ok(new { Succeeded = true });
    }

    private static string GetDataVaultObjectDescription(DataVaultObjectType? objectType)
    {
        return objectType switch
        {
            DataVaultObjectType.Hub => "Hub stores unique business keys as stable business entities.",
            DataVaultObjectType.Link => "Link stores relationships between Hub entities.",
            DataVaultObjectType.Satellite => "Satellite stores descriptive and historical attributes for a Hub or Link.",
            DataVaultObjectType.Reference => "Reference stores controlled reference data used by business entities.",
            DataVaultObjectType.Staging => "Staging stores source rows for pre-processing before Data Vault load.",
            DataVaultObjectType.InformationMart => "Information Mart stores analytical views built from Data Vault structures.",
            _ => "Data Vault mapping defines how source columns are stored in a target table.",
        };
    }
}



