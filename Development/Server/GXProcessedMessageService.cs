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
using Gurux.Data.Relay.Shared.Protocol;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Server;

public sealed class GXProcessedMessageService : IProcessedMessageService
{
    private readonly IGXConfigurationService _configurationService;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GXProcessedMessageService(IGXConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public async Task<GXDataAcknowledgement?> TryGetAcknowledgementAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            GXServerState? state = await _configurationService.LoadServerStateAsync(cancellationToken);
            GXProcessedMessageState? existing = state?.ProcessedMessages.FirstOrDefault(item => item.MessageId == messageId && item.RouteKey == GXServerDatabaseContext.RouteKey);
            if (existing is null)
            {
                return null;
            }

            return new GXDataAcknowledgement
            {
                MessageId = existing.MessageId,
                Status = existing.Status,
                Error = existing.Error,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAcknowledgementAsync(GXDataAcknowledgement acknowledgement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            GXServerState state = await _configurationService.LoadServerStateAsync(cancellationToken) ?? new GXServerState();
            GXProcessedMessageState? existing = state.ProcessedMessages.FirstOrDefault(item => item.MessageId == acknowledgement.MessageId && item.RouteKey == GXServerDatabaseContext.RouteKey);
            if (existing is null)
            {
                state.ProcessedMessages.Add(new GXProcessedMessageState
                {
                    MessageId = acknowledgement.MessageId,
                    RouteKey = GXServerDatabaseContext.RouteKey,
                    ProcessedAt = DateTimeOffset.UtcNow,
                    Status = acknowledgement.Status,
                    Error = acknowledgement.Error,
                });
            }
            else
            {
                existing.ProcessedAt = DateTimeOffset.UtcNow;
                existing.Status = acknowledgement.Status;
                existing.Error = acknowledgement.Error;
            }

            await _configurationService.SaveServerStateAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
