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

using System.Text.Json;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Sources;
using Gurux.Data.Relay.Sources;
using Gurux.Service.Orm;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXDataSourceConfigurationService(GXConfigurationStoreSettings storeSettings,
    IGXDatabaseConnectionFactory connectionFactory, GXDataSourceSecretProtector protector) : IGXDataSourceConfigurationService
{
    public event Func<Task>? Changed;

    public async Task<IReadOnlyList<GXStoredDataSource>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var native = connectionFactory.CreateConnection(new GXDatabaseConfiguration
        { Type = storeSettings.Type, ConnectionString = storeSettings.ConnectionString });
        await native.OpenAsync(cancellationToken);
        using var connection = new GXDbConnection(native);
        using var transaction = connection.BeginTransaction();
        var sources = await connection.SelectAllAsync<GXDataSource>(transaction, cancellationToken);
        var routes = await connection.SelectAllAsync<GXDataSourceRoute>(transaction, cancellationToken);
        var encrypted = await connection.SelectAllAsync<GXDataSourceSecret>(transaction, cancellationToken);
        transaction.Commit();
        return sources.Select(source => new GXStoredDataSource(new GXDataSourceConfiguration
        {
            InstanceId = source.Id,
            Provider = source.Provider,
            Enabled = source.Enabled,
            Pull = source.Pull,
            Streaming = source.Streaming,
            DeliveryAttempts = source.DeliveryAttempts,
            RetryDelaySeconds = source.RetryDelaySeconds,
            Schedule = JsonSerializer.Deserialize<GXSchedule>(source.ScheduleJson) ?? new(),
            Limits = JsonSerializer.Deserialize<GXSourceLimits>(source.LimitsJson) ?? new(),
            RouteIds = routes.Where(route => route.DataSource == source.Id).OrderBy(route => route.Position).Select(route => route.Transport).ToList()
        }, source.ConfigurationJson, encrypted.Where(secret => secret.DataSource == source.Id)
            .ToDictionary(secret => secret.Name, secret => protector.Decrypt(secret.Name, secret), StringComparer.OrdinalIgnoreCase))).ToList();
    }

    public async Task NotifyChangedAsync()
    {
        if (Changed is not { } changed) return;
        foreach (Func<Task> callback in changed.GetInvocationList()) await callback();
    }

    public async Task SaveAsync(GXStoredDataSource stored, CancellationToken cancellationToken)
    {
        GXDataSourceConfiguration source = stored.Configuration;
        if (source.InstanceId == Guid.Empty) source.InstanceId = Guid.NewGuid();
        await using var native = connectionFactory.CreateConnection(new GXDatabaseConfiguration { Type = storeSettings.Type, ConnectionString = storeSettings.ConnectionString });
        await native.OpenAsync(cancellationToken);
        using var connection = new GXDbConnection(native);
        using var transaction = connection.BeginTransaction();
        try
        {
            GXDataSource entity = new()
            {
                Id = source.InstanceId,
                Provider = source.Provider,
                Enabled = source.Enabled,
                Pull = source.Pull,
                Streaming = source.Streaming,
                ConfigurationJson = stored.SettingsJson,
                ScheduleJson = JsonSerializer.Serialize(source.Schedule),
                LimitsJson = JsonSerializer.Serialize(source.Limits),
                DeliveryAttempts = source.DeliveryAttempts,
                RetryDelaySeconds = source.RetryDelaySeconds
            };
            GXDataSource? existing = await connection.SingleOrDefaultAsync<GXDataSource>(transaction, GXSelectArgs.SelectById<GXDataSource>(entity.Id), cancellationToken);
            await GXEntityPersistence.SaveAsync(connection, transaction, entity, existing, cancellationToken);
            await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<GXDataSourceRoute>(route => route.DataSource == entity.Id), cancellationToken);
            for (int index = 0; index < source.RouteIds.Count; ++index)
                await connection.InsertAsync(transaction, GXInsertArgs.Insert(new GXDataSourceRoute { DataSource = entity.Id, Transport = source.RouteIds[index], Position = index }), cancellationToken);
            foreach (GXDataSourceSecretChange change in stored.SecretChanges)
            {
                if (string.IsNullOrWhiteSpace(change.Name) || change.Name.Length > 128 ||
                    (change.Clear && change.Replacement is not null))
                    throw new ArgumentException("Invalid data-source secret change.");
                await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<GXDataSourceSecret>(secret =>
                    secret.DataSource == entity.Id && secret.Name == change.Name), cancellationToken);
                if (!change.Clear && !string.IsNullOrEmpty(change.Replacement))
                {
                    GXDataSourceSecret encrypted = protector.Encrypt(change.Name, change.Replacement);
                    encrypted.DataSource = entity.Id;
                    await connection.InsertAsync(transaction, GXInsertArgs.Insert(encrypted), cancellationToken);
                }
            }
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
        await NotifyChangedAsync();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var native = connectionFactory.CreateConnection(new GXDatabaseConfiguration { Type = storeSettings.Type, ConnectionString = storeSettings.ConnectionString });
        await native.OpenAsync(cancellationToken);
        using var connection = new GXDbConnection(native);
        using var transaction = connection.BeginTransaction();
        await connection.DeleteAsync(transaction, GXDeleteArgs.Delete<GXDataSource>(source => source.Id == id), cancellationToken);
        transaction.Commit();
        await NotifyChangedAsync();
    }
}
