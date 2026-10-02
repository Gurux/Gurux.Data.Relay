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
using Gurux.Data.Relay.Log;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/server")]
public sealed class ServerController : ControllerBase
{
    private readonly IGXRelayAdministrationService _administrationService;

    public ServerController(IGXRelayAdministrationService administrationService)
    {
        _administrationService = administrationService;
    }

    /// <summary>Read the current settings and entity concurrency stamps.</summary>
    [HttpGet("settings")]
    public Task<GXServerConfiguration> GetSettings(CancellationToken cancellationToken, [FromQuery] GXDatabase? filter = null)
    {
        return _administrationService.GetServerSettingsAsync(cancellationToken, filter);
    }

    /// <summary>Replace settings, preserving IDs and concurrency stamps from the latest read.</summary>
    [HttpPut("settings")]
    [ProducesResponseType<GXServerConfiguration>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    public async Task<IActionResult> UpdateSettings([FromBody] GXServerConfiguration configuration, CancellationToken cancellationToken)
    {
        string? originalStamp = configuration.ConcurrencyStamp;
        try
        {
            await _administrationService.UpdateServerSettingsAsync(configuration, cancellationToken);
        }
        catch (Exception ex) when (configuration.ConcurrencyStamp != originalStamp)
        {
            // Runtime activation happens after commit. Return that exact committed
            // snapshot so a correction can be saved without bypassing concurrency checks.
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Settings saved, but server activation failed.",
                Detail = $"Settings were saved, but server activation failed: {ex.Message}"
            };
            problem.Extensions["savedSettings"] = configuration;
            return new ObjectResult(problem) { StatusCode = problem.Status };
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid server settings.",
                Detail = ex.Message
            });
        }
        return Ok(configuration);
    }

    /// <summary>List configured databases.</summary>
    [HttpGet("databases")]
    public Task<IReadOnlyList<GXDatabaseConfiguration>> GetDatabases(CancellationToken cancellationToken)
    {
        return _administrationService.GetDatabasesAsync(ApplicationMode.Server, cancellationToken);
    }

    /// <summary>Replace the configured database list. Use If-Match to detect concurrent edits.</summary>
    [HttpPut("databases")]
    [ProducesResponseType<IReadOnlyList<GXDatabaseConfiguration>>(200)]
    public async Task<IActionResult> SaveDatabases([FromBody] IReadOnlyList<GXDatabaseConfiguration> databases, CancellationToken cancellationToken)
    {
        string? version = await _administrationService.SaveDatabasesAsync(ApplicationMode.Server, databases, cancellationToken,
            Request.Headers.IfMatch.FirstOrDefault()?.Trim('"'));
        if (version is not null) Response.Headers.ETag = $"\"{version}\"";
        return Ok(databases);
    }

    /// <summary>Read server table mappings.</summary>
    [HttpGet("mappings")]
    public async Task<IReadOnlyList<GXTableMapping>> GetMappings(CancellationToken cancellationToken)
    {
        GXServerState state = await _administrationService.GetServerStateAsync(cancellationToken);
        return state.TableMappings;
    }

    /// <summary>Read the persisted runtime state.</summary>
    [HttpGet("state")]
    public Task<GXServerState> GetState(CancellationToken cancellationToken)
    {
        return _administrationService.GetServerStateAsync(cancellationToken);
    }

    /// <summary>
    /// Read a filtered page of event log entries.
    /// </summary>
    [HttpPost("events/page")]
    public Task<GXEventPage<GXEventLog>> GetEventPage([FromBody] Shared.GXEventPageRequest request, CancellationToken cancellationToken)
        => _administrationService.GetEventPageAsync(ApplicationMode.Server, request, cancellationToken);
    /// <summary>Read event log entries with optional level and database filters.</summary>
    [HttpPost("events")]
    public Task<IReadOnlyList<GXEventLog>> GetEvents([FromBody] GXEventQuery query, CancellationToken cancellationToken)
    {
        return _administrationService.GetEventsAsync(ApplicationMode.Server, query.Top, query.LogLevel, query.DatabaseIndex, cancellationToken);
    }

    /// <summary>Delete matching event log entries.</summary>
    [HttpDelete("events")]
    public async Task<IActionResult> ClearEvents([FromQuery] int? databaseIndex, CancellationToken cancellationToken)
    {
        int deletedRows = await _administrationService.ClearEventsAsync(ApplicationMode.Server, databaseIndex, cancellationToken);
        return Ok(new { Succeeded = true, DeletedRows = deletedRows });
    }

    /// <summary>Download the settings as a JSON file.</summary>
    [HttpGet("settings/export")]
    public async Task<FileContentResult> ExportSettings(CancellationToken cancellationToken)
    {
        string json = await _administrationService.ExportSettingsJsonAsync(ApplicationMode.Server, cancellationToken);
        return File(Encoding.UTF8.GetBytes(json), "application/json", "Gurux.Data.Relay.server.settings.json");
    }

    /// <summary>Import settings from a JSON document.</summary>
    [HttpPost("settings/import")]
    public async Task<IActionResult> ImportSettings([FromBody] GXSettingsImportRequest request, CancellationToken cancellationToken)
    {
        await _administrationService.ImportSettingsJsonAsync(ApplicationMode.Server, request.Json, request.DatabaseMap, cancellationToken);
        return Ok(new { Succeeded = true });
    }
}

