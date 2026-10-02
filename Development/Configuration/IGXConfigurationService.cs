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

using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;

namespace Gurux.Data.Relay.Configuration;

public interface IGXConfigurationService
{
    /// <summary>
    /// Returns the configuration path based on the application mode.
    /// </summary>
    /// <param name="mode">The application mode.</param>
    /// <returns>The configuration path.</returns>
    string GetConfigurationPath(ApplicationMode mode);

    /// <summary>
    /// Returns the state path based on the application mode.
    /// </summary>
    /// <param name="mode">The application mode.</param>
    /// <returns>The state path.</returns>
    string GetStatePath(ApplicationMode mode);

    /// <summary>
    /// Load client configuration from the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>Client configuration if found; otherwise, null.</returns>
    Task<GXClientConfiguration?> LoadClientAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Load client state from the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>Client state if found; otherwise, null.</returns>
    Task<GXClientState?> LoadClientStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Load server configuration from the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>Server configuration if found; otherwise, null.</returns>  
    Task<GXServerConfiguration?> LoadServerAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Load server state from the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>Server state if found; otherwise, null.</returns>
    Task<GXServerState?> LoadServerStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Load datavault configuration from the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>Datavault configuration if found; otherwise, null.</returns>
    Task<GXDataVaultConfiguration?> LoadDataVaultAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Save client configuration to the database.
    /// </summary>
    /// <param name="configuration">Client configuration to save.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    Task SaveClientAsync(GXClientConfiguration configuration, CancellationToken cancellationToken);

    /// <summary>Import client settings with fresh local metadata.</summary>
    Task ImportClientAsync(GXClientConfiguration configuration, CancellationToken cancellationToken)
        => SaveClientAsync(configuration, cancellationToken);

    /// <summary>
    /// Save client state to the database.
    /// </summary>
    /// <param name="state">Client state to save.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    Task SaveClientStateAsync(GXClientState state, CancellationToken cancellationToken);

    async Task UpdateClientStateAsync(Action<GXClientState> update, CancellationToken cancellationToken)
    {
        var state = await LoadClientStateAsync(cancellationToken);
        if (state is null) return;
        update(state);
        await SaveClientStateAsync(state, cancellationToken);
    }

    /// <summary>
    /// Save server configuration to the database.
    /// </summary>
    /// <param name="configuration">Server configuration to save.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    Task SaveServerAsync(GXServerConfiguration configuration, CancellationToken cancellationToken);

    /// <summary>Import server settings with fresh local metadata.</summary>
    Task ImportServerAsync(GXServerConfiguration configuration, CancellationToken cancellationToken)
        => SaveServerAsync(configuration, cancellationToken);

    /// <summary>
    /// Save server state to the database.
    /// </summary>
    /// <param name="state">Server state to save.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    Task SaveServerStateAsync(GXServerState state, CancellationToken cancellationToken);

    /// <summary>
    /// Save datavault configuration to the database.
    /// </summary>
    /// <param name="configuration">Datavault configuration to save.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    Task SaveDataVaultAsync(GXDataVaultConfiguration configuration, CancellationToken cancellationToken);

    /// <summary>Import Data Vault settings with fresh local metadata.</summary>
    Task ImportDataVaultAsync(GXDataVaultConfiguration configuration, CancellationToken cancellationToken)
        => SaveDataVaultAsync(configuration, cancellationToken);
}

