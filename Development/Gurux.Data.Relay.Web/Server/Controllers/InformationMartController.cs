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

using Gurux.Data.Relay.Client;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Web.Server.Services;
using Gurux.Service.Orm.Model;
using Microsoft.AspNetCore.Mvc;

namespace Gurux.Data.Relay.Web.Server.Controllers;

[ApiController]
[Route("api/datavault/databases/{databaseId:guid}/marts")]
public sealed class InformationMartController(IGXRelayAdministrationService administration, IGXDatabaseConnectionFactory connections) : ControllerBase
{
    /// <summary>Refresh an Information Mart.</summary>
    [HttpPost("{mappingId:guid}/refresh")]
    public async Task<IActionResult> Refresh(Guid databaseId, Guid mappingId, CancellationToken cancellationToken,
        [FromServices] Configuration.IGXDataVaultRuntimeStateStore runtimeState)
    {
        var settings = await administration.GetDataVaultSettingsAsync(cancellationToken);
        var database = settings.Databases.FirstOrDefault(d => d.Id == databaseId);
        var mapping = database?.Mappings?.FirstOrDefault(m => m.Id == mappingId);
        if (mapping == null || !GXInformationMartBuilder.HasColumnMappings(mapping))
            return NotFound("Mart column mappings not found.");
        try
        {
            var scheduler = new GXDataVaultScheduler(connections, new GXDatabaseChangeNotifierFactory(connections),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<GXDataVaultScheduler>.Instance) { RuntimeStateStore = runtimeState };
            return Ok(await scheduler.RefreshInformationMartAsync(database!, mappingId, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Problem(detail: ex.Message, statusCode: 400);
        }
    }

    /// <summary>Preview an Information Mart definition.</summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview(Guid databaseId, [FromQuery] string sourceTable, [FromQuery] Guid? hubMappingId, CancellationToken cancellationToken)
    {
        var settings = await administration.GetDataVaultSettingsAsync(cancellationToken);
        var database = settings.Databases.FirstOrDefault(d => d.Id == databaseId);
        if (database == null) return NotFound("Database not found.");
        try
        {
            await using var connection = connections.CreateConnection(database);
            await connection.OpenAsync(cancellationToken);
            var schema = new GXSchemaManager(connection);
            return Ok(new GXInformationMartBuilder(database.Mappings ?? [], schema.Describe, database.Type).Preview(sourceTable, hubMappingId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Problem(detail: ex.Message, statusCode: 400);
        }
    }

    /// <summary>Create an Information Mart and its mapping.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(Guid databaseId, [FromBody] GXCreateMartRequest request, CancellationToken cancellationToken)
    {
        var settings = await administration.GetDataVaultSettingsAsync(cancellationToken);
        var databases = settings.Databases;
        var database = databases.FirstOrDefault(d => d.Id == databaseId);
        if (database == null) return NotFound("Database not found.");
        try
        {
            await using var connection = connections.CreateConnection(database);
            await connection.OpenAsync(cancellationToken);
            var schema = new GXSchemaManager(connection);
            var builder = new GXInformationMartBuilder(database.Mappings ?? [], schema.Describe, database.Type, request.Definition.ReferenceJoins);
            var tables = schema.GetTables();
            if (request.UseExistingTable && !tables.Any(t => string.Equals(t, request.TargetTable, StringComparison.OrdinalIgnoreCase)))
                return BadRequest("The selected target table does not exist.");
            var plan = builder.Build(request);
            if ((!request.UseExistingTable && tables.Any(t => string.Equals(t, plan.TargetSchema.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, plan.TargetSchema.ToString(), StringComparison.OrdinalIgnoreCase))) ||
                (database.Mappings ?? []).Any(m => string.Equals(m.TargetTable.Name, plan.TargetSchema.ToString(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.TargetTable.Name, request.TargetTable, StringComparison.OrdinalIgnoreCase)))
                return Conflict("The target table or mapping already exists.");
            if (request.UseExistingTable)
            {
                plan = builder.ValidateExistingTarget(plan);
                await administration.CreateDataVaultMappingAsync(databases.ToList().IndexOf(database), plan.Mapping, cancellationToken);
                return Ok(new GXCreateVaultTableResult { TableName = plan.TargetSchema.ToString() });
            }
            schema.CreateTable(plan.TargetSchema);
            var result = new GXCreateVaultTableResult { TableName = plan.TargetSchema.ToString() };
            // Resolve actual provider casing/escaping after DDL (notably PostgreSQL and Oracle).
            try
            {
                plan = new GXInformationMartBuilder(database.Mappings ?? [], schema.Describe, database.Type).ResolveTarget(plan);
                result.TableName = plan.TargetSchema.ToString();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.MappingError = $"Table {result.TableName} was created, but its target columns could not be resolved: {ex.Message}. The table was not populated.";
                return Ok(result);
            }
            var errors = new List<string>();
            try
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                // Disposing an uncommitted transaction rolls back if refresh fails.
                await GXInformationMartBuilder.PopulateAsync(connection, transaction, plan, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"Populating the table failed: {ex.Message}.");
            }
            try
            {
                await administration.CreateDataVaultMappingAsync(databases.ToList().IndexOf(database), plan.Mapping, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"The column mappings could not be saved: {ex.Message}. Scheduled refresh is unavailable.");
            }
            if (errors.Count > 0) result.MappingError = $"Table {result.TableName} was created. " + string.Join(" ", errors);
            return Ok(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Problem(detail: ex.Message, statusCode: 400);
        }
    }
}
