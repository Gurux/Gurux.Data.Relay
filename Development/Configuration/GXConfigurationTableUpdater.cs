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

using Gurux.Data.Relay.Database;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm.Model;

namespace Gurux.Data.Relay.Configuration;

public static class GXConfigurationTableUpdater
{
    /// <summary>Reads pending configuration model changes without updating tables.</summary>
    public static async Task<List<Shared.GXConfigurationTableChange>> GetChangesAsync(GXConfigurationStoreSettings settings,
        IGXDatabaseConnectionFactory connectionFactory, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection(new GXDatabaseConfiguration { Type = settings.Type, ConnectionString = settings.ConnectionString });
        await connection.OpenAsync(cancellationToken);
        var schema = new GXSchemaManager(connection);
        var changes = new List<GXConfigurationTableChange>();
        foreach (var type in GXRelationalConfigurationStore.EntityTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = schema.GetTableChanges(type);
            if (pending.Any())
            {
                //TODO: This is not work with SQLite and fix is under construction.
                changes.Add(new() { TableName = type.Name, Changes = pending });
            }
        }
        return changes;
    }

    public static async Task UpdateAsync(GXConfigurationStoreSettings settings,
        IGXDatabaseConnectionFactory connectionFactory, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection(new GXDatabaseConfiguration
        {
            Type = settings.Type,
            ConnectionString = settings.ConnectionString
        });
        await connection.OpenAsync(cancellationToken);
        var schema = new GXSchemaManager(connection);

        foreach (var type in GXRelationalConfigurationStore.EntityTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!schema.TableExist(type)) schema.CreateTable(type, relations: false, overwrite: false);
        }
        foreach (var type in GXRelationalConfigurationStore.EntityTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            schema.UpdateTable(type, updateForeignKeys: true, removeUnusedColumns: true);
        }

    }
}
