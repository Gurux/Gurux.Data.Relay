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
using Gurux.Data.Relay.Realtime;
using Gurux.Data.Relay.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/update")]
public sealed class UpdateController(
    GXConfigurationStoreSettings settings,
    IGXDatabaseConnectionFactory connectionFactory,
    ILogger<UpdateController>? logger = null) : ControllerBase
{
    /// <summary>
    /// List configuration tables with pending schema changes.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<GXConfigurationTableChange>>> GetChanges(CancellationToken cancellationToken)
    {
        try
        {
            return await GXConfigurationTableUpdater.GetChangesAsync(settings, connectionFactory, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Failed to inspect configuration tables for {DatabaseType}.", settings.Type);
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "Unable to read configuration table structures.", detail: ex.Message);
        }
    }

    /// <summary>
    /// Update the configuration database schema.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Update(CancellationToken cancellationToken)
    {
        await GXConfigurationTableUpdater.UpdateAsync(settings, connectionFactory, cancellationToken);
        HttpContext?.RequestServices.GetService<IGXRelayChangePublisher>()?
            .Publish(new(null, GXRelayChangeKind.Store));
        return NoContent();
    }
}
