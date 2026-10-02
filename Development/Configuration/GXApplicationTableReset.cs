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
using E = System.Linq.Expressions.Expression;
using Gurux.Service.Orm;
using System.Data.Common;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Service.Orm.Model;

namespace Gurux.Data.Relay.Configuration;

/// <summary>Offline reset of the explicitly owned Relay and example-data tables.</summary>
public static class GXApplicationTableReset
{
    public static void Reset(DbConnection connection, DatabaseType provider, Action<string>? report = null)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("Open the database before resetting tables.");
        var names = new HashSet<string>(GXRelationalConfigurationStore.EntityTypes.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        names.UnionWith(new[] { "RelayRuntimeState", "GXClientTableState", "GXTableMapping", "GXProcessedMessageState",
            "GXEventLog", "GXTransportMessageLog", "GXDataVaultMappingState",
            "Customer", "STG_Customer", "Hub_Company", "HUB_Customer", "LINK_CustomerCompany",
            "MART_Customer", "MART_Customer2", "MART_Customer5", "REF_Country", "Sat_Company", "SAT_Customer" });
        var manager = new GXSchemaManager(connection);
        var tables = manager.GetTables();
        // No prefix matching, no cascade, and no removal in other schemas.
        var selected = tables.Where(names.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var descriptions = tables.ToDictionary(t => t, t => manager.Describe(t), StringComparer.OrdinalIgnoreCase);
        foreach (var (table, schema) in descriptions)
            if (!selected.Contains(table) && schema.ForeignKeys.Any(k => selected.Contains(k.ReferencedTable)))
                throw new InvalidOperationException($"Reset refused: unrelated table '{table}' references a selected table.");
        GXTableSchema PhysicalTable(string name) => provider == DatabaseType.MSSQL ? new GXTableSchema { Name = name, Schema = "dbo" } : new GXTableSchema { Name = name };
        bool sqlite = provider == DatabaseType.SqLite;
        bool enforcement = sqlite && GXResetSchemaOperations.GetForeignKeyEnforcement(connection, provider);
        foreach (var table in selected) report?.Invoke("Will remove table and all rows: " + table);
        try
        {
            if (sqlite) GXResetSchemaOperations.SetForeignKeyEnforcement(connection, provider, false);
            else
                foreach (var table in selected)
                    foreach (var key in descriptions[table].ForeignKeys)
                    {
                        var physical = PhysicalTable(table);
                        GXResetSchemaOperations.DropForeignKey(connection, provider, new() { Name = physical.Name, Schema = physical.Schema, Catalog = physical.Catalog }, key.Name);
                    }
            foreach (var table in selected)
            {
                manager.DropTable(PhysicalTable(table));
                report?.Invoke("Removed: " + table);
            }
        }
        finally
        {
            if (sqlite) GXResetSchemaOperations.SetForeignKeyEnforcement(connection, provider, enforcement);
        }
    }
}
