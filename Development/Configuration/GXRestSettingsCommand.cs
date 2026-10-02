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
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Configuration;

/// <summary>Updates persisted REST availability without starting relay transports.</summary>
public static class GXRestSettingsCommand
{
    /// <summary>Changes only REST availability in an existing mode configuration.</summary>
    public static async Task ExecuteAsync(
        IGXConfigurationService service, 
        ApplicationMode mode, bool enabled, 
        CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case ApplicationMode.Client:
                var client = await service.LoadClientAsync(cancellationToken) ?? throw Missing(mode);
                client.RestEnabled = enabled;
                await service.SaveClientAsync(client, cancellationToken);
                break;
            case ApplicationMode.Server:
                var server = await service.LoadServerAsync(cancellationToken) ?? throw Missing(mode);
                server.RestEnabled = enabled;
                await service.SaveServerAsync(server, cancellationToken);
                break;
            case ApplicationMode.DataVault:
                var vault = await service.LoadDataVaultAsync(cancellationToken) ?? throw Missing(mode);
                vault.RestEnabled = enabled;
                await service.SaveDataVaultAsync(vault, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private static InvalidOperationException Missing(ApplicationMode mode) => new($"No {mode} configuration exists. Configure the mode first.");
}
