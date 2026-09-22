using System.Data.Common;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Realtime;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Web.Server.Models;
using Gurux.Data.Relay.Web.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Gurux.Data.Relay.Web.Server.Controllers;

/// <summary>CRUD operations on rows in configured client, server and Data Vault databases.</summary>
[ApiController]
[Route("api/{mode}/databases/{databaseId:guid}/rows")]
public sealed class RowsController(IGXRelayAdministrationService administration, IGXDatabaseConnectionFactory connections,
    IGXDatabaseMetadataService metadata, IGXRelayChangePublisher? changes = null) : ControllerBase
{
    /// <summary>Read a page of rows. TableName is the physical table name returned by database metadata.</summary>
    [HttpGet]
    public async Task<ActionResult<Shared.GXTableData>> Read(string mode, Guid databaseId, [FromQuery] string tableName,
        CancellationToken cancellationToken, int startIndex = 0, int count = 50, [FromQuery(Name = "filters")] Dictionary<string, string>? filters = null)
    {
        var database = await FindDatabaseAsync(mode, databaseId, cancellationToken);
        if (database.Result != null) return database.Result;
        try { return await new GXTableDataReader(connections, metadata).ReadAsync(database.Value!, tableName, startIndex, count, cancellationToken, filters); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Detail = ex.Message, Status = 400 }); }
    }

    /// <summary>Insert one row. Omit identity and computed columns; return the affected row count.</summary>
    [HttpPost]
    [ProducesResponseType<GXRowWriteResult>(201)]
    public Task<IActionResult> Insert(string mode, Guid databaseId, [FromQuery] string tableName, [FromBody] GXInsertRowRequest request, CancellationToken cancellationToken) =>
        WriteAsync(mode, databaseId, db => new GXTableRowWriter(connections, metadata).InsertAsync(db, tableName, request.Values, cancellationToken), true, cancellationToken);

    /// <summary>Update supplied columns of one row, identified by its complete primary key.</summary>
    [HttpPatch]
    [ProducesResponseType<GXRowWriteResult>(200)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Update(string mode, Guid databaseId, [FromQuery] string tableName, [FromBody] GXUpdateRowRequest request, CancellationToken cancellationToken) =>
        WriteAsync(mode, databaseId, db => new GXTableRowWriter(connections, metadata).UpdateAsync(db, tableName, request.Keys, request.Values, cancellationToken), false, cancellationToken);

    /// <summary>Delete exactly one row using its complete primary key in the JSON request body.</summary>
    [HttpDelete]
    [ProducesResponseType<GXRowWriteResult>(200)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Delete(string mode, Guid databaseId, [FromQuery] string tableName, [FromBody] GXRowKeyRequest request, CancellationToken cancellationToken) =>
        WriteAsync(mode, databaseId, db => new GXTableRowWriter(connections, metadata).DeleteAsync(db, tableName, request.Keys, cancellationToken), false, cancellationToken);

    private async Task<ActionResult<Configuration.GXDatabaseConfiguration>> FindDatabaseAsync(string mode, Guid databaseId, CancellationToken token)
    {
        if (!Enum.TryParse<ApplicationMode>(mode, true, out var parsed) || parsed is not (ApplicationMode.Client or ApplicationMode.Server or ApplicationMode.DataVault))
            return BadRequest(new ProblemDetails { Detail = "Select client, server or datavault.", Status = 400 });
        var databases = await administration.GetDatabasesAsync(parsed, token);
        var database = databases.FirstOrDefault(db => db.Id == databaseId);
        return database == null ? NotFound() : database;
    }

    private async Task<IActionResult> WriteAsync(string mode, Guid databaseId, Func<Configuration.GXDatabaseConfiguration, Task<int>> write, bool insert, CancellationToken token)
    {
        var database = await FindDatabaseAsync(mode, databaseId, token);
        if (database.Result != null) return database.Result;
        try
        {
            int affected = await write(database.Value!);
            if (affected == 0) return NotFound();
            changes?.Publish(new(mode.ToLowerInvariant(), Shared.GXRelayChangeKind.Data));
            return StatusCode(insert ? 201 : 200, new GXRowWriteResult(affected));
        }
        catch (ArgumentException ex) { return Problem(detail: ex.Message, statusCode: 400); }
        catch (InvalidOperationException ex) { return Problem(detail: ex.Message, statusCode: 409); }
        catch (DbException ex) { return Problem(detail: ex.Message, statusCode: 400); }
    }
}
