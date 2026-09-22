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

using Gurux.Service.Orm.Common.Model;
using System.Data.Common;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Transport;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Log;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Client;

namespace Gurux.Data.Relay.Shared.Client;

public sealed class GXClientSchemaSender : IClientSchemaSender
{
    private readonly IGXDatabaseConnectionFactory _connectionFactory;
    private readonly IGXTransportFactory _transportFactory;
    private readonly ILogger<GXClientSchemaSender> _logger;

    public GXClientSchemaSender(
        IGXDatabaseConnectionFactory connectionFactory,
        IGXTransportFactory transportFactory,
        ILogger<GXClientSchemaSender> logger)
    {
        _connectionFactory = connectionFactory;
        _transportFactory = transportFactory;
        _logger = logger;
    }

    public async Task<int> SendAsync(GXClientConfiguration configuration, CancellationToken cancellationToken)
    {
        int sentMessages = 0;
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetSourceDatabases();
        for (int databaseIndex = 0; databaseIndex < databases.Count; ++databaseIndex)
        {
            GXDatabaseConfiguration database = databases[databaseIndex];
            using var databaseContext = GXEventLogContext.BeginDatabase(GXEventLogContext.DatabaseIndex ?? databaseIndex);
            foreach (GXTableConfiguration table in database.Tables ?? [])
            {
                IReadOnlyList<GXTransportConfiguration> transports = configuration.GetTransports(databaseIndex, table.Name);
                if (transports.Count == 0) continue;
                cancellationToken.ThrowIfCancellationRequested();
                GXTableSchema schema = await DescribeAsync(database, table.Name, cancellationToken);
                GXDataMessage message = BuildSchemaMessage(table, schema);
                message.RecordSource = GXRecordSource.Resolve(database, table);
                await SendToConfiguredTransportsAsync(transports, message, cancellationToken);
                sentMessages += 1;
                _logger.LogInformation(
                    "Sent schema message {MessageId} for table {TableName} from source database {DatabaseIndex}/{DatabaseCount}.",
                    message.MessageId,
                    table.Name,
                    databaseIndex + 1,
                    databases.Count);
            }
        }

        return sentMessages;
    }

    private async Task<GXTableSchema> DescribeAsync(
        GXDatabaseConfiguration database,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = _connectionFactory.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        GXSchemaManager schemaManager = new(guruxConnection);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            GXTableSchema schema = schemaManager.Describe(tableName);
            schema.Columns.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));
            return schema;
        }, cancellationToken);
    }

    private static GXDataMessage BuildSchemaMessage(GXTableConfiguration table, GXTableSchema schema)
    {
        return new GXDataMessage
        {
            MessageType = MessageType.Schema,
            Keys = table.Keys.Count != 0
                    ? [.. table.Keys]
                    : schema.Columns
                        .Where(static column => column.IsPrimaryKey)
                        .Select(static column => column.Name)
                        .ToList(),
            Table = new GXTableSchema
            {
                Name = table.Name,
            },
            Schema = schema,
        };
    }

    private async Task<GXDataAcknowledgement> SendToConfiguredTransportsAsync(
        IReadOnlyList<GXTransportConfiguration> transports,
        GXDataMessage message,
        CancellationToken cancellationToken)
    {
        return await new GXMessageDelivery(_transportFactory, _logger).DeliverAsync(transports, message, cancellationToken);
    }
}



