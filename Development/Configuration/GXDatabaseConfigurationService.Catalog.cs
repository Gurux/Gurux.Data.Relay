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
using System.Data;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm;
using Microsoft.Extensions.Logging;

namespace Gurux.Data.Relay.Configuration;

public sealed partial class GXDatabaseConfigurationService : IGXDatabaseCatalogService
{
    public async Task<List<GXDatabase>> GetDatabasesAsync(CancellationToken token)
    {
        await ConfigurationGate.WaitAsync(token);
        try
        {
            await EnsureConfigurationStoreAsync(token);
            await using var native = CreateConfigurationConnection();
            await native.OpenAsync(token);
            using var connection = new GXDbConnection(native);
            using var transaction = connection.BeginTransaction();
            var result = await connection.SelectAllAsync<GXDatabase>(transaction, token);
            transaction.Commit();
            return result.OrderBy(d => d.Description).ThenBy(d => d.Id).ToList();
        }
        finally { ConfigurationGate.Release(); }
    }

    public async Task SaveDatabaseAsync(GXDatabase database, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(database);
        ValidateCatalogDatabase(database);
        await ConfigurationGate.WaitAsync(token);
        try
        {
            await EnsureConfigurationStoreAsync(token);
            await using var native = CreateConfigurationConnection();
            await native.OpenAsync(token);
            using var connection = new GXDbConnection(native);
            using var transaction = connection.BeginTransaction();
            if (database.Id == Guid.Empty) database.Id = Guid.NewGuid();
            var existing = await connection.SingleOrDefaultAsync<GXDatabase>(transaction,
                Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXDatabase>(connection, ("Id", database.Id)), token);
            await GXEntityPersistence.SaveAsync(connection, transaction, database, existing, token);
            transaction.Commit();
        }
        finally { ConfigurationGate.Release(); }
        await CatalogChangedAsync();
    }

    private static void ValidateCatalogDatabase(GXDatabase database)
    {
        Validator.ValidateObject(database, new ValidationContext(database), true);
        if (!Enum.IsDefined(database.Type.Value) || string.IsNullOrWhiteSpace(database.ConnectionString))
            throw new ArgumentException("Database type and connection string are required.");
        if (database.Tables?.Count > 0 || database.Mappings?.Count > 0)
            throw new ArgumentException("Configure tables and mappings in Client, Server or Data Vault, not in the shared catalog.");
    }

    public async Task DeleteDatabaseAsync(Guid id, string? concurrencyStamp, CancellationToken token)
    {
        await ConfigurationGate.WaitAsync(token);
        try
        {
            await EnsureConfigurationStoreAsync(token);
            await using var native = CreateConfigurationConnection();
            await native.OpenAsync(token);
            using var connection = new GXDbConnection(native);
            using var transaction = connection.BeginTransaction();
            var database = await connection.SingleOrDefaultAsync<GXDatabase>(transaction, Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXDatabase>(connection, ("Id", id)), token)
                ?? throw new DBConcurrencyException("Database was deleted. Reload the catalog.");
            if (concurrencyStamp is null || database.ConcurrencyStamp != concurrencyStamp)
                throw new DBConcurrencyException("Database has changed. Reload the catalog before deleting.");
            var references = await connection.SelectAllAsync<GXSettingsDatabaseReference>(transaction, token);
            if (references.Any(r => r.TargetId == id))
            {
                throw new InvalidOperationException("Database is in use. Remove it from Client, Server and Data Vault first.");
            }
            //TODO: This can be removed later after the GXDataVaultTableMapping table is removed from the catalog. It is only used for legacy data vaults.
            await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<GXDataVaultTableMapping>(s => s.Database == id), token);
            await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<GXTransport>(s => s.Database == id), token);


            // Schedules retain a database context even after their mode selection is removed.
            // Remove them before the database because this foreign key does not cascade.

            await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<GXSchedule>(s => s.Database == id), token);
            await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<GXDatabase>(d => d.Id == id), token);
            transaction.Commit();
        }
        finally
        {
            ConfigurationGate.Release();
        }
        await CatalogChangedAsync();
    }

    private async Task CatalogChangedAsync()
    {
        _changes?.Publish(new(null, GXRelayChangeKind.Configuration));
        foreach (var handlers in new[] { ClientConfigurationSaved, ServerConfigurationSaved, DataVaultConfigurationSaved })
            if (handlers is not null)
                foreach (Func<Task> handler in handlers.GetInvocationList())
                    try { await handler(); }
                    catch (Exception ex) { _logger.LogError(ex, "Database catalog was saved, but a runtime configuration refresh failed."); }
    }
}
