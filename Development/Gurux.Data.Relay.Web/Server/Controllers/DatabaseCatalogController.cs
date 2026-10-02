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
using Gurux.Data.Relay.Shared;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.ComponentModel.DataAnnotations;
using Gurux.Service.Orm.Model;
using Gurux.Data.Relay.Web.Server.Services;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/databases")]
public sealed class DatabaseCatalogController(IGXDatabaseCatalogService catalog) : ControllerBase
{
    /// <summary>Download the physical table schemas of a catalog database as JSON.</summary>
    [HttpGet("{id:guid}/schema")]
    public async Task<IActionResult> ExportSchema(Guid id,
        [FromServices] Database.IGXDatabaseConnectionFactory connections, CancellationToken token, [FromQuery] string? tableName = null)
    {
        var database = await FindDatabaseAsync(id, token);
        if (database is null) return NotFound("Database not found.");
        await using var connection = connections.CreateConnection(database);
        await connection.OpenAsync(token);
        var manager = new GXSchemaManager(connection);
        var options = new JsonOptions();
        GXApiJsonOptions.Configure(options);
        options.JsonSerializerOptions.WriteIndented = true;
        if (tableName is not null)
        {
            var actualName = manager.GetTables().FirstOrDefault(name => string.Equals(name, tableName, StringComparison.OrdinalIgnoreCase));
            if (actualName is null) return NotFound("Table not found.");
            return File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manager.Describe(actualName), options.JsonSerializerOptions),
                "application/json", $"database-{id}-table-schema.json");
        }
        var schemas = new List<Gurux.Service.Orm.Common.Model.GXTableSchema>();
        foreach (var name in manager.GetTables().OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            schemas.Add(manager.Describe(name));
        }
        return File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(schemas, options.JsonSerializerOptions),
            "application/json", $"database-{id}-schema.json");
    }

    private async Task<GXDatabaseConfiguration?> FindDatabaseAsync(Guid id, CancellationToken token)
    {
        var database = (await catalog.GetDatabasesAsync(token)).SingleOrDefault(d => d.Id == id);
        return database is null ? null : new GXDatabaseConfiguration
        {
            Id = database.Id,
            Type = database.Type.Value,
            ConnectionString = database.ConnectionString,
            Description = database.Description
        };
    }

    [HttpGet("{id:guid}/data")]
    public async Task<IActionResult> GetData(Guid id, string tableName,
        [FromServices] Database.IGXDatabaseConnectionFactory connections,
        [FromServices] Database.IGXDatabaseMetadataService metadata,
        CancellationToken token, int startIndex = 0, int count = 50,
        [FromQuery(Name = "filters")] Dictionary<string, string>? filters = null)
    {
        if (startIndex < 0 || count < 1 || count > 1000) return BadRequest("Invalid page size or offset.");
        var database = await FindDatabaseAsync(id, token);
        if (database is null) return NotFound("Database not found.");
        try { return Ok(await new Database.GXTableDataReader(connections, metadata).ReadAsync(database, tableName, startIndex, count, token, filters)); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/import-json")]
    [HttpPost("{id:guid}/import-csv")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ImportData(Guid id, string tableName,
        [FromServices] Database.IGXDatabaseConnectionFactory connections,
        [FromServices] Database.IGXDatabaseMetadataService metadata,
        [FromServices] Gurux.Data.Relay.Realtime.IGXRelayChangePublisher changes,
        CancellationToken token, string delimiter = ",", bool hasHeader = true)
    {
        var database = await FindDatabaseAsync(id, token);
        if (database is null) return NotFound("Database not found.");
        using var reader = new StreamReader(Request.Body, System.Text.Encoding.UTF8, true);
        var content = await reader.ReadToEndAsync(token);
        try
        {
            var importer = new Database.GXTableCsvImporter(connections, metadata);
            var count = Request.Path.Value?.EndsWith("import-json", StringComparison.OrdinalIgnoreCase) == true
                ? await importer.ImportJsonAsync(database, tableName, content, token)
                : await importer.ImportAsync(database, tableName, content, delimiter, hasHeader, token);
            changes.Publish(new(null, GXRelayChangeKind.Data));
            return Ok(count);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("{id:guid}/diagram")]
    public async Task<IActionResult> Diagram(Guid id,
        [FromServices] Gurux.Data.Relay.Web.Server.Services.IGXRelayAdministrationService administration, CancellationToken token)
    {
        var database = (await catalog.GetDatabasesAsync(token)).SingleOrDefault(d => d.Id == id);
        if (database is null) return NotFound();
        return Content(await administration.ExportDatabaseDiagramAsync(new GXDatabaseConfiguration
        {
            Id = database.Id,
            Type = database.Type.Value,
            ConnectionString = database.ConnectionString
        }, token), "text/plain; charset=utf-8");
    }
    [HttpPost("test")]
    public async Task<IActionResult> Test(GXDatabase database,
        [FromServices] Gurux.Data.Relay.Database.IGXDatabaseConnectionTestService tester, CancellationToken token)
    {
        try
        {
            await tester.TestConnectionAsync(new GXDatabaseConfiguration
            {
                Type = database.Type.Value,
                ConnectionString = database.ConnectionString
            }, token);
            return Ok();
        }
        catch (Exception ex) { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
    }
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        try { return Ok(await catalog.GetDatabasesAsync(token)); }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Detail = ex.Message }); }
    }

    /// <summary>Create a database connection with a server-generated identifier.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(GXDatabase database, CancellationToken token)
    {
        // Creation must never overwrite a record identified by the request body.
        database.Id = Guid.NewGuid();
        database.ConcurrencyStamp = null;
        database.CreationTime = null;
        database.Updated = null;
        try
        {
            await catalog.SaveDatabaseAsync(database, token);
            return StatusCode(StatusCodes.Status201Created, database);
        }
        catch (Exception ex) when (ex is ArgumentException or ValidationException or InvalidOperationException)
        { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Save(Guid id, GXDatabase database, CancellationToken token)
    {
        if (id != database.Id) return BadRequest("Database ID does not match the URL.");
        try { await catalog.SaveDatabaseAsync(database, token); return Ok(database); }
        catch (DBConcurrencyException ex) { return Conflict(new ProblemDetails { Detail = ex.Message }); }
        catch (Exception ex) when (ex is ArgumentException or ValidationException or InvalidOperationException)
        { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string? concurrencyStamp, CancellationToken token)
    {
        try { await catalog.DeleteDatabaseAsync(id, concurrencyStamp, token); return NoContent(); }
        catch (DBConcurrencyException ex) { return Conflict(new ProblemDetails { Detail = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Detail = ex.Message }); }
    }
}
