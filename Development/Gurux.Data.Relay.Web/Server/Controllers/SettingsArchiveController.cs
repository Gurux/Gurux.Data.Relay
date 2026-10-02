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

using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Web.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/settings/archive")]
public sealed class SettingsArchiveController(IGXSettingsArchiveService archives) : ControllerBase
{
    /// <summary>Export all client, server and Data Vault settings, including credentials.</summary>
    [HttpGet("export")]
    public async Task<FileContentResult> Export(CancellationToken cancellationToken)
    {
        var options = new JsonOptions();
        GXApiJsonOptions.Configure(options);
        options.JsonSerializerOptions.WriteIndented = true;
        var archive = await archives.ExportAllSettingsAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(archive, options.JsonSerializerOptions)),
            "application/json", "Gurux.Data.Relay.settings.json");
    }

    /// <summary>Replace all three modes' settings from one archive atomically.</summary>
    [HttpPost("import")]
    [Consumes("application/json")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Import([FromBody] GXSettingsImportRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Json)) return BadRequest("Import payload cannot be empty.");
            var options = new JsonOptions();
            GXApiJsonOptions.Configure(options);
            var archive = JsonSerializer.Deserialize<GXSettingsArchive>(request.Json, options.JsonSerializerOptions);
            if (archive is null) return BadRequest("Import payload cannot be read as a settings archive.");
            await archives.ImportAllSettingsAsync(archive, request.DatabaseMap, cancellationToken);
            return NoContent();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ValidationException)
        {
            return BadRequest(ex.Message);
        }
    }
}
