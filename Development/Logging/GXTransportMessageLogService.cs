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
using Gurux.Data.Relay.Realtime;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Transport;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Protocol;

namespace Gurux.Data.Relay.Log;

public sealed class GXTransportMessageLogService : IGXTransportMessageLogService
{
    private readonly IGXRelayChangePublisher? _changes;

    private readonly IGXDatabaseConnectionFactory _connectionFactory;
    private readonly IGXMessageSerializer _serializer;
    private readonly GXConfigurationStoreSettings? _storeSettings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed class ModeState
    {
        public GXDatabaseConfiguration? Configuration;
        public ApplicationMode Mode;
        public bool TableReady;
        public bool Enabled;
    }
    private readonly ModeState _defaultState = new();
    private readonly ConcurrentDictionary<ApplicationMode, ModeState> _modeStates = new();
    private ModeState State => GXRelayModeContext.Current.Value is { } mode
        ? _modeStates.GetOrAdd(mode, _ => new ModeState { Mode = mode }) : _defaultState;
    private GXDatabaseConfiguration? _configuration { get => State.Configuration; set => State.Configuration = value; }
    private ApplicationMode _mode { get => State.Mode; set => State.Mode = value; }
    private bool _tableReady { get => State.TableReady; set => State.TableReady = value; }
    public GXTransportMessageLogService(
        IGXDatabaseConnectionFactory connectionFactory,
        IGXMessageSerializer serializer, IGXRelayChangePublisher? changes = null, GXConfigurationStoreSettings? storeSettings = null)
    {
        _connectionFactory = connectionFactory;
        _serializer = serializer; _changes = changes; _storeSettings = storeSettings;
    }

    public bool IsEnabled { get => State.Enabled; private set => State.Enabled = value; }

    public async Task InitializeAsync(GXModeConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _mode = configuration.Mode;
            IsEnabled = (_storeSettings?.CommunicationLogLevel ?? configuration.CommunicationLogLevel) == LogLevel.Trace;
            if (!IsEnabled)
            {
                _configuration = null;
                _tableReady = false;
                return;
            }

            GXDatabaseConfiguration database = GetLogDatabaseConfiguration(configuration);
            _configuration = new GXDatabaseConfiguration
            {
                Description = database.Description,
                Type = database.Type,
                ConnectionString = database.ConnectionString,
            };

            try
            {
                await EnsureTableAsync(cancellationToken);
            }
            catch
            {
                _configuration = null;
                _tableReady = false;
                IsEnabled = false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task WriteDataMessageAsync(
        TransportMessageDirection direction,
        GXTransportConfiguration transport,
        GXDataMessage message,
        string? endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(message);

        return WriteAsync(new GXTransportMessageLog
        {
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Trace,
            Mode = _mode,
            Direction = direction,
            TransportType = transport.Type,
            MessageId = message.MessageId,
            MessageType = message.MessageType,
            Endpoint = endpoint,
            Message = Encoding.UTF8.GetString(_serializer.Serialize(message)),
        }, cancellationToken);
    }

    public Task WriteAcknowledgementAsync(
        TransportMessageDirection direction,
        GXTransportConfiguration transport,
        GXDataAcknowledgement acknowledgement,
        string? endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(acknowledgement);

        return WriteAsync(new GXTransportMessageLog
        {
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Trace,
            Mode = _mode,
            Direction = direction,
            TransportType = transport.Type,
            MessageId = acknowledgement.MessageId,
            AcknowledgementStatus = acknowledgement.Status,
            Endpoint = endpoint,
            Message = Encoding.UTF8.GetString(_serializer.Serialize(acknowledgement)),
        }, cancellationToken);
    }

    private async Task WriteAsync(GXTransportMessageLog entry, CancellationToken cancellationToken)
    {
        if (!IsEnabled || _configuration is null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsEnabled || _configuration is null)
            {
                return;
            }

            await EnsureTableAsync(cancellationToken);
            await InsertAsync(entry, cancellationToken);
        }
        catch
        {
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (_tableReady)
        {
            return;
        }

        await using DbConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        GXSchemaManager schemaManager = new(guruxConnection);
        if (!schemaManager.TableExist(nameof(GXTransportMessageLog)))
        {
            schemaManager.CreateTable<GXTransportMessageLog>();
        }

        _tableReady = true;
    }

    private async Task InsertAsync(
        GXTransportMessageLog entry,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        entry.Settings = await GXLogParent.ResolveAsync(guruxConnection, entry.Mode, cancellationToken);
        GXEntityPersistence.Initialize(entry);
        await guruxConnection.InsertAsync(GXInsertArgs.Insert(entry));
        if (_storeSettings?.NotifyLogChanges == true)
            _changes?.Publish(new(entry.Mode.ToString().ToLowerInvariant(), GXRelayChangeKind.Events));
    }

    private DbConnection CreateConnection()
    {
        return _connectionFactory.CreateConnection(_configuration ?? throw new InvalidOperationException("Transport message log service is not initialized."));
    }

    private static GXDatabaseConfiguration GetLogDatabaseConfiguration(GXModeConfiguration configuration)
    {
        return configuration switch
        {
            GXClientConfiguration clientConfiguration => clientConfiguration.GetSourceDatabases()[0],
            GXServerConfiguration serverConfiguration => serverConfiguration.GetTargetDatabases()[0],
            GXDataVaultConfiguration dataVaultConfiguration => dataVaultConfiguration.GetDatabases()[0],
            _ => throw new NotSupportedException($"Configuration type '{configuration.GetType().Name}' is not supported."),
        };
    }
}


