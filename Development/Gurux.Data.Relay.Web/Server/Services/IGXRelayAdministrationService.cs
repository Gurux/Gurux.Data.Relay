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
using Gurux.Data.Relay.Log;
using Gurux.Service.Orm.Common.Model;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Shared;

namespace Gurux.Data.Relay.Web.Server.Services;

public interface IGXRelayAdministrationService
{
    Task<string> ExportDatabaseDiagramAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken);
    Task<IReadOnlyList<GXDatabaseConfiguration>> GetDatabasesAsync(ApplicationMode mode, CancellationToken cancellationToken);
    Task<string?> SaveDatabasesAsync(ApplicationMode mode, IReadOnlyList<GXDatabaseConfiguration> databases, CancellationToken cancellationToken, string? concurrencyStamp = null);

    Task<GXClientConfiguration> GetClientSettingsAsync(CancellationToken cancellationToken, GXDatabase? filter = null);
    Task UpdateClientSettingsAsync(GXClientConfiguration configuration, CancellationToken cancellationToken);
    Task<GXClientState> GetClientStateAsync(CancellationToken cancellationToken);
    Task ResetClientStateAsync(CancellationToken cancellationToken);
    Task ResetClientTableCheckpointAsync(Guid databaseId, string stateTableName, string? concurrencyStamp, CancellationToken cancellationToken);
    Task<int> SendClientSchemaAsync(CancellationToken cancellationToken);

    Task<GXServerConfiguration> GetServerSettingsAsync(CancellationToken cancellationToken, GXDatabase? filter = null);
    Task UpdateServerSettingsAsync(GXServerConfiguration configuration, CancellationToken cancellationToken);
    Task<GXServerState> GetServerStateAsync(CancellationToken cancellationToken);

    Task<GXDataVaultConfiguration> GetDataVaultSettingsAsync(CancellationToken cancellationToken, GXDatabase? filter = null);
    Task UpdateDataVaultSettingsAsync(GXDataVaultConfiguration configuration, CancellationToken cancellationToken);

    Task<string> ExportSettingsJsonAsync(ApplicationMode mode, CancellationToken cancellationToken);
    Task ImportSettingsJsonAsync(ApplicationMode mode, string json, IReadOnlyDictionary<Guid, Guid>? databaseMap, CancellationToken cancellationToken);

    Task<IReadOnlyList<GXEventLog>> GetEventsAsync(ApplicationMode mode, int top, LogLevel? minimumLevel, int? databaseIndex, CancellationToken cancellationToken);
    Task<int> ClearEventsAsync(ApplicationMode mode, int? databaseIndex, CancellationToken cancellationToken);
    Task<Shared.GXEventPage<GXEventLog>> GetEventPageAsync(ApplicationMode mode, Shared.GXEventPageRequest request, CancellationToken cancellationToken);

    Task TestConnectionAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetTableNamesAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken);
    Task<GXTableSchema> DescribeTableAsync(GXDatabaseConfiguration configuration, string tableName, CancellationToken cancellationToken);

    Task<IReadOnlyList<(int DatabaseIndex, GXDataVaultTableMapping Mapping)>> ListDataVaultMappingsAsync(CancellationToken cancellationToken);
    Task<(int DatabaseIndex, GXDataVaultTableMapping Mapping)?> GetDataVaultMappingAsync(Guid id, CancellationToken cancellationToken);
    Task<GXDataVaultTableMapping> CreateDataVaultMappingAsync(int databaseIndex, GXDataVaultTableMapping mapping, CancellationToken cancellationToken);
    Task<GXDataVaultTableMapping> UpdateDataVaultMappingAsync(Guid id, int databaseIndex, GXDataVaultTableMapping mapping, CancellationToken cancellationToken);
    Task<bool> DeleteDataVaultMappingAsync(Guid id, CancellationToken cancellationToken);
}

