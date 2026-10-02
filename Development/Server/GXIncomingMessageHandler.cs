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

using Gurux.Data.Relay.Enums;
using Gurux.Data.Relay.Shared.Protocol;
using Microsoft.Extensions.Logging;
using Gurux.Data.Relay.Realtime;
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Server;

public sealed class GXIncomingMessageHandler : IGXIncomingMessageHandler
{
    private readonly IDataWriterService _dataWriterService;
    private readonly IDestinationTableService _destinationTableService;
    private readonly IProcessedMessageService _processedMessageService;
    private readonly ITableMappingService _tableMappingService;
    private readonly ILogger<GXIncomingMessageHandler> _logger;
    private readonly IGXRelayChangePublisher? _changes;

    public GXIncomingMessageHandler(
        IDataWriterService dataWriterService,
        IDestinationTableService destinationTableService,
        IProcessedMessageService processedMessageService,
        ITableMappingService tableMappingService,
        ILogger<GXIncomingMessageHandler> logger, IGXRelayChangePublisher? changes = null)
    {
        _dataWriterService = dataWriterService;
        _destinationTableService = destinationTableService;
        _processedMessageService = processedMessageService;
        _tableMappingService = tableMappingService;
        _logger = logger;
        _changes = changes;
    }

    public async Task<GXDataAcknowledgement> HandleAsync(GXDataMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (message.MessageType == MessageType.Ping)
        {
            _logger.LogInformation("Received ping message {MessageId} for table {TableName}.", message.MessageId, message.Table.Name);
            return new GXDataAcknowledgement
            {
                MessageId = message.MessageId,
                Status = AcknowledgementStatus.Ok,
            };
        }

        string? resolvedDestination = GXServerDatabaseContext.DestinationTable;
        GXDataAcknowledgement? existing = await _processedMessageService.TryGetAcknowledgementAsync(message.MessageId, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Replaying stored acknowledgement for message {MessageId}.", message.MessageId);
            return existing;
        }

        string destinationTable = resolvedDestination
            ?? await _tableMappingService.GetOrCreateDestinationTableAsync(message, cancellationToken);
        if (message.MessageType == MessageType.Schema)
        {
            await _destinationTableService.EnsureSchemaAsync(destinationTable, message, cancellationToken);
            _changes?.Publish(new("server", GXRelayChangeKind.Data));
            _logger.LogInformation(
                "Received schema message {MessageId} for source table {TableName}, destination {DestinationTable}.",
                message.MessageId,
                message.Table.Name,
                destinationTable);

            GXDataAcknowledgement schemaAcknowledgement = new()
            {
                MessageId = message.MessageId,
                Status = AcknowledgementStatus.Ok,
            };

            await _processedMessageService.SaveAcknowledgementAsync(schemaAcknowledgement, cancellationToken);
            return schemaAcknowledgement;
        }

        await _destinationTableService.EnsureReadyAsync(destinationTable, message, cancellationToken);
        _logger.LogInformation(
            "Received message {MessageId} for source table {TableName}, destination {DestinationTable}, with {ChangeCount} changes.",
            message.MessageId,
            message.Table.Name,
            destinationTable,
            message.Changes.Count);

        int insertedRows = await _dataWriterService.WriteAsync(destinationTable, message, cancellationToken);
        _changes?.Publish(new("server", GXRelayChangeKind.Data));
        _logger.LogInformation("Inserted {InsertedRows} changes into destination table {DestinationTable}.", insertedRows, destinationTable);

        GXDataAcknowledgement acknowledgement = new()
        {
            MessageId = message.MessageId,
            Status = AcknowledgementStatus.Ok,
        };

        await _processedMessageService.SaveAcknowledgementAsync(acknowledgement, cancellationToken);
        return acknowledgement;
    }
}


