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
using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Web.Server.Services;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Web.Server.Mcp;

public interface IGXDataVaultWriteStore
{
    Task<GXDataVaultConfiguration> LoadAsync(CancellationToken token);
    Task<IReadOnlyList<string>> TablesAsync(GXDatabaseConfiguration database, CancellationToken token);
    Task<GXTableSchema> DescribeAsync(GXDatabaseConfiguration database, string table, CancellationToken token);
    Task SaveAsync(GXDataVaultConfiguration configuration, CancellationToken token);
    Task CreateTableAsync(GXDatabaseConfiguration database, GXTableSchema schema, CancellationToken token);
}

/// <summary>Uses the configuration store's transaction and optimistic concurrency stamp.</summary>
public sealed class GXDataVaultWriteStore(IGXRelayAdministrationService administration, IGXConfigurationService configurationService, IGXDatabaseConnectionFactory connections) : IGXDataVaultWriteStore
{
    public async Task<GXDataVaultConfiguration> LoadAsync(CancellationToken token)
        => await configurationService.LoadDataVaultAsync(token) ?? new GXDataVaultConfiguration();
    public Task<IReadOnlyList<string>> TablesAsync(GXDatabaseConfiguration database, CancellationToken token)
        => administration.GetTableNamesAsync(database, token);
    public Task<GXTableSchema> DescribeAsync(GXDatabaseConfiguration database, string table, CancellationToken token)
        => administration.DescribeTableAsync(database, table, token);
    public Task SaveAsync(GXDataVaultConfiguration configuration, CancellationToken token)
        => configurationService.SaveDataVaultAsync(configuration, token);
    public async Task CreateTableAsync(GXDatabaseConfiguration database, GXTableSchema schema, CancellationToken token)
    {
        await using var connection = connections.CreateConnection(database);
        await connection.OpenAsync(token);
        var manager = new Gurux.Service.Orm.Model.GXSchemaManager(connection);
        if (!manager.GetTables().Any(name => string.Equals(name, schema.ToString(), StringComparison.OrdinalIgnoreCase)))
            await Task.Run(() => manager.CreateTable(schema), token);
    }
}
