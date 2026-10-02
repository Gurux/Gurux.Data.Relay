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

using System.Data.Common;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Database;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gurux.Data.Relay.Client;

public sealed class GXDatabaseChangeNotifierFactory : IDatabaseChangeNotifierFactory
{
    private readonly IGXDatabaseConnectionFactory _connectionFactory;
    private readonly ILogger _logger;

    public GXDatabaseChangeNotifierFactory(IGXDatabaseConnectionFactory connectionFactory, ILogger<GXDatabaseChangeNotifierFactory>? logger = null)
        : this(connectionFactory, (ILogger)(logger ?? NullLogger<GXDatabaseChangeNotifierFactory>.Instance))
    {
    }

    internal GXDatabaseChangeNotifierFactory(IGXDatabaseConnectionFactory connectionFactory, ILogger logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IDatabaseChangeSubscription> CreateAsync(
        GXDatabaseConfiguration database,
        GXTableConfiguration table,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _connectionFactory.CreateConnection(database);
        GXDbConnection? guruxConnection = null;
        try
        {
            await connection.OpenAsync(cancellationToken);
            guruxConnection = new(connection);
            string[] fallback = table.Columns.Count != 0 ? [.. table.Columns]
                : new GXSchemaManager(guruxConnection).Describe(table.Name).Columns.Select(column => column.Name).ToArray();
            string[] columns = fallback.Concat(new[] { table.ChangeTracking.CreatedColumn, table.ChangeTracking.UpdatedColumn, table.ChangeTracking.DeletedColumn }
                .OfType<string>().Where(column => !string.IsNullOrWhiteSpace(column))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, table.Schedule.IntervalSeconds ?? 1));
            GXDatabaseChangeNotifier notifier = new(
                guruxConnection,
                null,
                [new DatabaseMonitor(table.Name, columns)],
                null,
                interval);
            return new GXDatabaseChangeSubscription(connection, guruxConnection, notifier, _logger);
        }
        catch
        {
            if (guruxConnection != null) await guruxConnection.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class GXDatabaseChangeSubscription : IDatabaseChangeSubscription
    {
        private readonly DbConnection _connection;
        private readonly GXDbConnection _guruxConnection;
        private readonly GXDatabaseChangeNotifier _notifier;
        private readonly ILogger _logger;

        public GXDatabaseChangeSubscription(
            DbConnection connection,
            GXDbConnection guruxConnection,
            GXDatabaseChangeNotifier notifier, ILogger logger)
        {
            _connection = connection;
            _guruxConnection = guruxConnection;
            _notifier = notifier;
            _logger = logger;
            _notifier.Changed += OnChanged;
            _notifier.Error += OnError;
        }

        public event EventHandler<GXDatabaseChangedEventArgs>? Changed;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _notifier.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _notifier.StopAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _notifier.Changed -= OnChanged;
            _notifier.Error -= OnError;
            await _notifier.DisposeAsync();
            await _guruxConnection.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private void OnChanged(object? sender, GXDatabaseChangedEventArgs args)
        {
            Changed?.Invoke(sender, args);
        }

        private void OnError(object? sender, Exception error) => _logger.LogError(error, "Source database change monitoring failed.");
    }
}

