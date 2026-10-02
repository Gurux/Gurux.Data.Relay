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

using System.Text;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Web.Server.Models;
using Gurux.Data.Relay.Web.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Gurux.Data.Relay.Client;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/client")]
public sealed class ClientController : ControllerBase
{
    private readonly IGXRelayAdministrationService _administrationService;

    public ClientController(IGXRelayAdministrationService administrationService)
    {
        _administrationService = administrationService;
    }

    /// <summary>Read the current settings and entity concurrency stamps.</summary>
    [HttpGet("settings")]
    public Task<GXClientConfiguration> GetSettings(CancellationToken cancellationToken, [FromQuery] GXDatabase? filter = null)
    {
        return _administrationService.GetClientSettingsAsync(cancellationToken, filter);
    }

    /// <summary>Replace settings, preserving IDs and concurrency stamps from the latest read.</summary>
    [HttpPut("settings")]
    [ProducesResponseType<GXClientConfiguration>(200)]
    public async Task<IActionResult> UpdateSettings([FromBody] GXClientConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            await _administrationService.UpdateClientSettingsAsync(configuration, cancellationToken);
            return Ok(configuration);
        }
        catch (InvalidOperationException ex) { return Problem(detail: ex.Message, statusCode: 400); }
    }

    /// <summary>List configured databases.</summary>
    [HttpGet("databases")]
    public Task<IReadOnlyList<GXDatabaseConfiguration>> GetDatabases(CancellationToken cancellationToken)
    {
        return _administrationService.GetDatabasesAsync(ApplicationMode.Client, cancellationToken);
    }

    /// <summary>Replace the configured database list. Use If-Match to detect concurrent edits.</summary>
    [HttpPut("databases")]
    [ProducesResponseType<IReadOnlyList<GXDatabaseConfiguration>>(200)]
    public async Task<IActionResult> SaveDatabases([FromBody] IReadOnlyList<GXDatabaseConfiguration> databases, CancellationToken cancellationToken)
    {
        string? version = await _administrationService.SaveDatabasesAsync(ApplicationMode.Client, databases, cancellationToken,
            Request.Headers.IfMatch.FirstOrDefault()?.Trim('"'));
        if (version is not null) Response.Headers.ETag = $"\"{version}\"";
        return Ok(databases);
    }

    /// <summary>Read the persisted runtime state.</summary>
    [HttpGet("state")]
    public Task<GXClientState> GetState(CancellationToken cancellationToken)
    {
        return _administrationService.GetClientStateAsync(cancellationToken);
    }

    /// <summary>Reset all client transfer checkpoints.</summary>
    [HttpPost("reset")]
    public async Task<IActionResult> ResetState(CancellationToken cancellationToken)
    {
        await _administrationService.ResetClientStateAsync(cancellationToken);
        return Ok(new { Succeeded = true });
    }

    /// <summary>Reset the checkpoint for one client table.</summary>
    [HttpPost("state/reset-checkpoint")]
    public async Task<IActionResult> ResetTableCheckpoint([FromBody] Shared.GXResetCheckpointRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _administrationService.ResetClientTableCheckpointAsync(request.DatabaseId, request.StateTableName, request.ConcurrencyStamp, cancellationToken);
            return NoContent();
        }
        catch (System.Data.DBConcurrencyException ex) { return Problem(detail: ex.Message, statusCode: 409); }
        catch (InvalidOperationException ex) { return Problem(detail: ex.Message, statusCode: 400); }
    }

    /// <summary>Send all configured client table schemas.</summary>
    [HttpPost("send-schema")]
    public async Task<IActionResult> SendSchema(CancellationToken cancellationToken)
    {
        int sentSchemas = await _administrationService.SendClientSchemaAsync(cancellationToken);
        return Ok(new { Succeeded = true, SentSchemas = sentSchemas });
    }

    /// <summary>Read a filtered page of event log entries.</summary>
    [HttpPost("events/page")]
    public Task<Shared.GXEventPage<Log.GXEventLog>> GetEventPage([FromBody] Shared.GXEventPageRequest request, CancellationToken cancellationToken)
        => _administrationService.GetEventPageAsync(ApplicationMode.Client, request, cancellationToken);

    /// <summary>Read event log entries with optional level and database filters.</summary>
    [HttpPost("events")]
    public Task<IReadOnlyList<Log.GXEventLog>> GetEvents([FromBody] GXEventQuery query, CancellationToken cancellationToken)
    {
        return _administrationService.GetEventsAsync(ApplicationMode.Client, query.Top, query.LogLevel, query.DatabaseIndex, cancellationToken);
    }

    /// <summary>Send one client table schema.</summary>
    [HttpPost("tables/send-schema")]
    public async Task<IActionResult> SendTableSchema(
        [FromBody] Shared.GXClientTableRequest request,
        [FromServices] GXClientTableActions actions, CancellationToken cancellationToken)
    {
        try
        {
            await actions.SendSchemaAsync(request, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return Problem(detail: ex.Message, statusCode: 400); }
    }

    /// <summary>Test the destination connection for one client table.</summary>
    [HttpPost("tables/ping")]
    public async Task<IActionResult> PingTable(
        [FromBody] GXClientTableRequest request,
        [FromServices] GXClientTableActions actions, CancellationToken cancellationToken)
    {
        try
        {
            await actions.PingAsync(request, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return Problem(detail: ex.Message, statusCode: 400); }
    }

    /// <summary>Transfer one client table immediately.</summary>
    [HttpPost("tables/run")]
    public async Task<IActionResult> RunTable(
        [FromBody] Shared.GXClientTableRequest request,
        [FromServices] GXClientTableActions actions,
        [FromServices] GXTransferScheduler scheduler,
        CancellationToken cancellationToken)
    {
        try
        {
            await actions.RunAsync(request, scheduler, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return Problem(detail: ex.Message, statusCode: 400); }
    }

    /// <summary>Test a configured transport connection.</summary>
    [HttpPost("transports/{transportId:guid}/test-connection")]
    public async Task<IActionResult> TestTransportConnection(Guid transportId,
        [FromServices] GXClientTableActions actions, CancellationToken cancellationToken)
    {
        try
        {
            await actions.TestTransportAsync(transportId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return Problem(detail: ex.Message, statusCode: 400); }
    }

    /// <summary>Transfer all tables routed through the selected transport immediately.</summary>
    [HttpPost("transports/{transportId:guid}/run")]
    public async Task<IActionResult> RunTransport(Guid transportId,
        [FromServices] GXClientTableActions actions,
        [FromServices] GXTransferScheduler scheduler, CancellationToken cancellationToken)
    {
        try
        {
            await actions.RunTransportAsync(transportId, scheduler, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return Problem(detail: ex.Message, statusCode: 400); }
    }



    /// <summary>Delete matching event log entries.</summary>
    [HttpDelete("events")]
    public async Task<IActionResult> ClearEvents([FromQuery] int? databaseIndex, CancellationToken cancellationToken)
    {
        int deletedRows = await _administrationService.ClearEventsAsync(ApplicationMode.Client, databaseIndex, cancellationToken);
        return Ok(new { Succeeded = true, DeletedRows = deletedRows });
    }

    /// <summary>Download the settings as a JSON file.</summary>
    [HttpGet("settings/export")]
    public async Task<FileContentResult> ExportSettings(CancellationToken cancellationToken)
    {
        string json = await _administrationService.ExportSettingsJsonAsync(ApplicationMode.Client, cancellationToken);
        return File(Encoding.UTF8.GetBytes(json), "application/json", "Gurux.Data.Relay.client.settings.json");
    }

    /// <summary>Import settings from a JSON document.</summary>
    [HttpPost("settings/import")]
    public async Task<IActionResult> ImportSettings([FromBody] GXSettingsImportRequest request, CancellationToken cancellationToken)
    {
        await _administrationService.ImportSettingsJsonAsync(ApplicationMode.Client, request.Json, request.DatabaseMap, cancellationToken);
        return Ok(new { Succeeded = true });
    }
}


