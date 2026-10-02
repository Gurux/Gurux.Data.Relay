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
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Web.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Gurux.Data.Relay.Realtime;
using Gurux.Service.Orm.Model;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/database")]
public sealed class DatabaseController : ControllerBase
{
    /// <summary>
    /// Export a client table schema as JSON, without table data.
    /// </summary>
    [HttpGet("client/{databaseId:guid}/schema")]
    public async Task<IActionResult> ExportSchema(Guid databaseId, [FromQuery] string tableName,
        [FromServices] Database.IGXDatabaseConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        var database = await FindConfiguredDatabaseAsync("client", databaseId, cancellationToken);
        if (database.Result != null) return database.Result;
        await using var connection = connections.CreateConnection(database.Value!);
        await connection.OpenAsync(cancellationToken);
        var manager = new GXSchemaManager(connection);
        if (!manager.GetTables().Contains(tableName, StringComparer.OrdinalIgnoreCase))
        {
            return NotFound("Table not found.");
        }
        var schema = manager.Describe(tableName);
        return new JsonResult(schema, SchemaJsonOptions());
    }

    /// <summary>
    /// Create missing tables from JSON schema and register their columns and keys in client settings.
    /// </summary>
    [HttpPost("client/{databaseId:guid}/schema")]
    [Consumes("application/json")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ImportSchema(Guid databaseId,
        [FromServices] Database.IGXDatabaseConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        var settings = await _administrationService.GetClientSettingsAsync(cancellationToken);
        var database = settings.Databases.FirstOrDefault(d => d.Id == databaseId);
        if (database is null) return NotFound();
        return await ImportSchemaCoreAsync(database, connections, settings, cancellationToken);
    }

    /// <summary>Create missing physical tables in a catalog database without configuring a relay mode.</summary>
    [HttpPost("/api/databases/{databaseId:guid}/schema")]
    [Consumes("application/json")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ImportCatalogSchema(Guid databaseId,
        [FromServices] IGXDatabaseCatalogService catalog,
        [FromServices] Database.IGXDatabaseConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        var database = (await catalog.GetDatabasesAsync(cancellationToken)).SingleOrDefault(d => d.Id == databaseId);
        if (database is null) return NotFound("The selected database is no longer in the catalog.");
        return await ImportSchemaCoreAsync(new GXDatabaseConfiguration
        {
            Id = database.Id,
            Type = database.Type.Value,
            ConnectionString = database.ConnectionString
        }, connections, null, cancellationToken);
    }

    private async Task<IActionResult> ImportSchemaCoreAsync(GXDatabaseConfiguration database,
        Database.IGXDatabaseConnectionFactory connections, GXClientConfiguration? settings,
        CancellationToken cancellationToken)
    {
        List<GXTableSchema?> schemas;
        try
        {
            using var document = await System.Text.Json.JsonDocument.ParseAsync(Request.Body,
                new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true }, cancellationToken);
            var options = SchemaJsonOptions(import: true);
            schemas = document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                ? System.Text.Json.JsonSerializer.Deserialize<List<GXTableSchema?>>(document.RootElement, options)!
                : [System.Text.Json.JsonSerializer.Deserialize<GXTableSchema>(document.RootElement, options)];
        }
        catch (System.Text.Json.JsonException ex) { return BadRequest($"Invalid schema JSON: {ex.Message}"); }
        catch (Exception ex) when (ex is TypeLoadException or System.IO.FileNotFoundException or ArgumentException)
        {
            return BadRequest($"Invalid schema type: {ex.Message}");
        }
        if (schemas.Count == 0) return BadRequest("The schema file must contain at least one table.");
        foreach (var schema in schemas)
        {
            if (schema == null || string.IsNullOrWhiteSpace(schema.Name) || schema.Columns.Count == 0)
                return BadRequest("The schema must contain a table name and columns.");
            if (schema.Columns.Any(column => column is null || string.IsNullOrWhiteSpace(column.Name)) ||
                schema.Columns.Select(column => column.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != schema.Columns.Count)
                return BadRequest("Column names must be non-empty and unique.");
            foreach (var column in schema.Columns)
            {
                // DbType is inserted into DDL by the ORM. Accept type names and size/precision only.
                if (!string.IsNullOrEmpty(column.DbType) && !System.Text.RegularExpressions.Regex.IsMatch(
                    column.DbType, @"\A[A-Za-z][A-Za-z0-9_ ]*(\(\s*(\d+|[Mm][Aa][Xx])(\s*,\s*\d+)?\s*\))?([ ][A-Za-z ]+)?(\[\])?\z"))
                    return BadRequest($"Unsupported database type for column '{column.Name}'.");
                if (string.IsNullOrEmpty(column.DbType) && column.Type == null)
                    return BadRequest($"Column '{column.Name}' must specify a database type or CLR type.");
            }
            if (schema.Columns.Any(column => column.IsComputed || !string.IsNullOrEmpty(column.ComputedExpression)))
                return BadRequest("Schema import does not support computed columns.");
            var names = schema.Columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (schema.Indexes.Any(index => index == null || string.IsNullOrWhiteSpace(index.Name) || index.Columns == null || index.Columns.Count == 0 ||
                index.Columns.Any(column => column == null || !names.Contains(column.Name) || !Enum.IsDefined(column.Order))))
                return BadRequest($"Table '{schema}' contains an invalid index.");
            if (schema.ForeignKeys == null || schema.ForeignKeys.Any(key => key == null || string.IsNullOrWhiteSpace(key.ReferencedTable) || key.Columns == null || key.Columns.Count == 0 ||
                !Enum.IsDefined(key.OnDelete) || !Enum.IsDefined(key.OnUpdate) || key.Columns.Any(column => column == null ||
                    !names.Contains(column.Column) || string.IsNullOrWhiteSpace(column.ReferencedColumn))))
                return BadRequest($"Table '{schema}' contains an invalid foreign key.");
        }
        if (schemas.Select(s => s!.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != schemas.Count)
            return BadRequest("The schema file contains duplicate table names.");

        // Referenced tables must exist before their dependants are created.
        var pending = schemas.Select(s => s!).ToList();
        var ordered = new List<GXTableSchema>();
        while (pending.Count != 0)
        {
            var next = pending.FirstOrDefault(table => table.ForeignKeys.All(key => !pending.Any(target => target != table &&
                string.Equals(target.Name, key.ReferencedTable, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(target.Schema) || string.IsNullOrEmpty(key.ReferencedSchema) ||
                 string.Equals(target.Schema, key.ReferencedSchema, StringComparison.OrdinalIgnoreCase)))));
            if (next == null) return BadRequest("The schema contains circular foreign key dependencies. Import those constraints separately.");
            pending.Remove(next);
            ordered.Add(next);
        }

        await using var connection = connections.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        var manager = new GXSchemaManager(connection);
        var existingTables = manager.GetTables();
        try
        {
            using var transaction = connection.BeginTransaction();
            foreach (var schema in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (existingTables.Contains(schema.ToString(), StringComparer.OrdinalIgnoreCase))
                    continue;
                Database.GXDataVaultSchemaCompatibility.Normalize(schema, database.Type);
                manager.CreateTable(transaction, schema);
            }
            transaction.Commit();
        }
        catch (Exception ex) when (ex is ArgumentException or System.Data.Common.DbException or Gurux.Service.Orm.GXDatabaseException)
        {
            // Some providers implicitly commit DDL, so rollback cannot undo every import.
            string message = ex.Message;
            try
            {
                var remaining = manager.GetTables().Except(existingTables, StringComparer.OrdinalIgnoreCase).ToArray();
                if (remaining.Length != 0)
                    message += " Tables already created: " + string.Join(", ", remaining) + ". Review these tables before retrying the import.";
            }
            catch (Exception)
            {
                message += " Could not verify whether tables were created. Check the database before retrying the import.";
            }
            return BadRequest(message);
        }
        _changes?.Publish(new(settings is null ? null : "client", GXRelayChangeKind.Data));
        if (settings is null)
            return Ok(new
            {
                TableName = ordered.Count == 1 ? ordered[0].ToString() : null,
                TableNames = ordered.Select(schema => schema.ToString()).ToArray()
            });
        database.Tables ??= [];
        foreach (var schema in ordered)
        {
            if (database.Tables.Any(table => string.Equals(table.Name, schema.ToString(), StringComparison.OrdinalIgnoreCase)))
                continue;
            database.Tables.Add(new GXTableConfiguration
            {
                Name = schema.ToString(),
                Columns = schema.Columns.Select(column => column.Name).ToList(),
                Keys = schema.Columns.Where(column => column.IsPrimaryKey).Select(column => column.Name).ToList()
            });
        }
        try
        {
            await _administrationService.UpdateClientSettingsAsync(settings, cancellationToken);
        }
        catch (Exception ex) when (ex is System.Data.DBConcurrencyException or InvalidOperationException)
        {
            // The source database and configuration store have separate transactions.
            var problem = new ProblemDetails
            {
                Status = ex is System.Data.DBConcurrencyException ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest,
                Title = "Tables created, but client settings could not be saved.",
                Detail = $"Tables were created, but client settings could not be saved: {ex.Message} Reload Client Tables and configure the created source tables."
            };
            return new ObjectResult(problem) { StatusCode = problem.Status };
        }
        return Ok(new
        {
            TableName = ordered.Count == 1 ? ordered[0].ToString() : null,
            TableNames = ordered.Select(schema => schema.ToString()).ToArray()
        });
    }

    private static System.Text.Json.JsonSerializerOptions SchemaJsonOptions(bool import = false)
    {
        var options = new Microsoft.AspNetCore.Mvc.JsonOptions();
        GXApiJsonOptions.Configure(options);
        if (import)
        {
            // Populate read-only ORM collections; reference handling is only needed when exporting.
            options.JsonSerializerOptions.ReferenceHandler = null;
            options.JsonSerializerOptions.AllowTrailingCommas = true;
            options.JsonSerializerOptions.PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Populate;
        }
        options.JsonSerializerOptions.WriteIndented = true;
        return options.JsonSerializerOptions;
    }

    /// <summary>List physical tables using a configured database ID, without resending connection credentials.</summary>
    [HttpGet("{mode}/{databaseId:guid}/tables")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetConfiguredTables(string mode, Guid databaseId, CancellationToken cancellationToken)
    {
        var database = await FindConfiguredDatabaseAsync(mode, databaseId, cancellationToken);
        if (database.Result != null) return database.Result;
        return Ok(await _administrationService.GetTableNamesAsync(database.Value!, cancellationToken));
    }

    /// <summary>Read the columns, types, generated fields and complete primary key of a configured database table.</summary>
    [HttpGet("{mode}/{databaseId:guid}/columns")]
    public async Task<ActionResult<GXTableSchema>> GetConfiguredColumns(string mode, Guid databaseId, [FromQuery] string tableName, CancellationToken cancellationToken)
    {
        var database = await FindConfiguredDatabaseAsync(mode, databaseId, cancellationToken);
        if (database.Result != null) return database.Result;
        var tables = await _administrationService.GetTableNamesAsync(database.Value!, cancellationToken);
        if (!tables.Contains(tableName, StringComparer.Ordinal)) return NotFound();
        return await _administrationService.DescribeTableAsync(database.Value!, tableName, cancellationToken);
    }

    private async Task<ActionResult<GXDatabaseConfiguration>> FindConfiguredDatabaseAsync(string mode, Guid databaseId, CancellationToken token)
    {
        if (!Enum.TryParse<ApplicationMode>(mode, true, out var parsed) ||
            parsed is not (ApplicationMode.Client or ApplicationMode.Server or ApplicationMode.DataVault))
            return BadRequest("Select client, server or datavault.");
        var databases = await _administrationService.GetDatabasesAsync(parsed, token);
        var database = databases.FirstOrDefault(db => db.Id == databaseId);
        return database == null ? NotFound() : database;
    }

    /// <summary>Export the schema diagram for a configured database.</summary>
    [HttpGet("{mode}/{databaseId:guid}/diagram")]
    public async Task<IActionResult> GetDiagram(string mode, Guid databaseId, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ApplicationMode>(mode, true, out var applicationMode)
            || applicationMode is not (ApplicationMode.Client
                or ApplicationMode.Server or ApplicationMode.DataVault))
            return BadRequest("Select client, server or datavault.");
        var databases = await _administrationService.GetDatabasesAsync(applicationMode, cancellationToken);
        var database = databases.FirstOrDefault(d => d.Id == databaseId);
        if (database is null) return NotFound("The selected database is no longer configured.");
        string source = await _administrationService.ExportDatabaseDiagramAsync(database, cancellationToken);
        return Content(source, "text/plain; charset=utf-8");
    }

    /// <summary>Import JSON rows into a client table using a database ID or name.</summary>
    [HttpPost("client/{databaseId}/import-json")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<int>> ImportJson(string databaseId, string tableName,
        [FromServices] Gurux.Data.Relay.Database.IGXDatabaseConnectionFactory connections,
        [FromServices] Gurux.Data.Relay.Database.IGXDatabaseMetadataService metadata,
        CancellationToken cancellationToken)
    {
        var databases = await _administrationService.GetDatabasesAsync(ApplicationMode.Client, cancellationToken);
        var matches = Guid.TryParse(databaseId, out var id)
            ? databases.Where(d => d.Id == id).ToArray()
            : databases.Where(d => string.Equals(d.Description, databaseId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1)
            return Conflict("Multiple client databases have this name. Use the database GUID instead.");
        var database = matches.SingleOrDefault();
        if (database is null) return NotFound("The selected client database is no longer configured.");
        using var reader = new StreamReader(Request.Body, System.Text.Encoding.UTF8, true);
        string json = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            int count = await new Database.GXTableCsvImporter(connections, metadata)
                .ImportJsonAsync(database, tableName, json, cancellationToken);
            _changes?.Publish(new("client", GXRelayChangeKind.Data));
            return count;
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>Import CSV rows into a client table.</summary>
    [HttpPost("client/{databaseId:guid}/import-csv")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<int>> ImportCsv(Guid databaseId, string tableName,
        [FromServices] Database.IGXDatabaseConnectionFactory connections,
        [FromServices] Database.IGXDatabaseMetadataService metadata,
        CancellationToken cancellationToken, string delimiter = ",", bool hasHeader = true)
    {
        var databases = await _administrationService.GetDatabasesAsync(ApplicationMode.Client, cancellationToken);
        var database = databases.FirstOrDefault(d => d.Id == databaseId);
        if (database is null) return NotFound("The selected client database is no longer configured.");
        using var reader = new StreamReader(Request.Body, System.Text.Encoding.UTF8, true);
        string csv = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            int count = await new Database.GXTableCsvImporter(connections, metadata)
                .ImportAsync(database, tableName, csv, delimiter, hasHeader, cancellationToken);
            _changes?.Publish(new("client", GXRelayChangeKind.Data));
            return count;
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>Import JSON or CSV rows into a configured staging table.</summary>
    [HttpPost("datavault/{databaseId:guid}/import-json")]
    [HttpPost("datavault/{databaseId:guid}/import-csv")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<int>> ImportStage(Guid databaseId, string tableName,
        [FromServices] Database.IGXDatabaseConnectionFactory connections,
        [FromServices] Database.IGXDatabaseMetadataService metadata,
        CancellationToken cancellationToken, string delimiter = ",", bool hasHeader = true)
    {
        var configuration = await _administrationService.GetDataVaultSettingsAsync(cancellationToken);
        var database = configuration.Databases.FirstOrDefault(d => d.Id == databaseId);
        if (database == null) return NotFound("The selected Data Vault database is no longer configured.");
        var stage = database.Mappings?.FirstOrDefault(m => m.ObjectType == DataVaultObjectType.Staging &&
            string.Equals(m.TargetTable?.Name, tableName, StringComparison.OrdinalIgnoreCase));
        if (stage == null) return BadRequest("Data can only be imported into a configured Stage table.");
        using var reader = new StreamReader(Request.Body, System.Text.Encoding.UTF8, true);
        string content = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            var importer = new Database.GXTableCsvImporter(connections, metadata);
            int count = Request.Path.Value?.EndsWith("import-json", StringComparison.OrdinalIgnoreCase) == true
                ? await importer.ImportJsonAsync(database, stage.TargetTable!.Name, content, cancellationToken)
                : await importer.ImportAsync(database, stage.TargetTable!.Name, content, delimiter, hasHeader, cancellationToken);
            _changes?.Publish(new("datavault", GXRelayChangeKind.Data));
            return count;
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>Read a page of table rows with optional column filters.</summary>
    [HttpGet("{mode}/{databaseId:guid}/data")]
    public async Task<ActionResult<GXTableData>> GetData(
        string mode, Guid databaseId, string tableName,
        [FromServices] Database.IGXDatabaseConnectionFactory connections,
        [FromServices] Database.IGXDatabaseMetadataService metadata,
        CancellationToken cancellationToken, int startIndex = 0, int count = 50,
        [FromQuery(Name = "filters")] Dictionary<string, string>? filters = null)
    {
        if (!Enum.TryParse<ApplicationMode>(mode, true, out var applicationMode)
            || applicationMode is not (ApplicationMode.Client or ApplicationMode.Server or ApplicationMode.DataVault))
            return BadRequest("Select client, server or datavault.");
        if (startIndex < 0 || count < 1 || count > 1000) return BadRequest("Invalid page size or offset.");
        var databases = await _administrationService.GetDatabasesAsync(applicationMode, cancellationToken);
        var database = databases.FirstOrDefault(d => d.Id == databaseId);
        if (database is null) return NotFound("The selected database is no longer configured.");
        try
        {
            return await new Database.GXTableDataReader(connections, metadata)
                .ReadAsync(database, tableName, startIndex, count, cancellationToken, filters);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    private readonly IGXRelayAdministrationService _administrationService;

    private readonly IGXRelayChangePublisher? _changes;
    public DatabaseController(IGXRelayAdministrationService administrationService, IGXRelayChangePublisher? changes = null)
    {
        _administrationService = administrationService;
        _changes = changes;
    }

    /// <summary>Test a database connection.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> TestConnection([FromBody] GXDatabaseConfiguration configuration, CancellationToken cancellationToken)
    {
        await _administrationService.TestConnectionAsync(configuration, cancellationToken);
        return Ok(new { Succeeded = true });
    }

    /// <summary>List tables in a database.</summary>
    [HttpPost("tables")]
    public Task<IReadOnlyList<string>> GetTables([FromBody] GXDatabaseConfiguration configuration, CancellationToken cancellationToken)
    {
        return _administrationService.GetTableNamesAsync(configuration, cancellationToken);
    }

    /// <summary>Describe table columns, keys and indexes.</summary>
    [HttpPost("columns/{tableName}")]
    public async Task<ActionResult<Gurux.Service.Orm.Common.Model.GXTableSchema>> GetColumns(
        [FromBody] GXDatabaseConfiguration configuration,
        [FromRoute] string tableName,
        CancellationToken cancellationToken)
    {
        Gurux.Service.Orm.Common.Model.GXTableSchema schema;
        try
        {
            schema = await _administrationService.DescribeTableAsync(configuration, tableName, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: 400);
        }
        return schema;
    }
}

