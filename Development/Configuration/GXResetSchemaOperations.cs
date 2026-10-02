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

using System.Data.Common;
using System.Globalization;
using Gurux.Service.Orm.Model;
using Gurux.Service.Orm;
using Gurux.Service.Orm.Common.Enums;
using Gurux.Service.Orm.Common.Model;

namespace Gurux.Data.Relay.Configuration;

/// <summary>Provider-specific operations used by the offline table reset.</summary>
internal static class GXResetSchemaOperations
{
    internal static void DropForeignKey(DbConnection connection, DatabaseType provider,
        GXTableSchema table, string constraintName)
    {
        if (provider == DatabaseType.SqLite)
            throw new NotSupportedException("SQLite foreign keys must be changed by rebuilding the table.");
        var builder = new GXSqlBuilder(provider);
        string action = provider is DatabaseType.MySQL or DatabaseType.MariaDB ? " DROP FOREIGN KEY " : " DROP CONSTRAINT ";
        using var command = connection.CreateCommand();
        command.CommandText = "ALTER TABLE " + (string.IsNullOrEmpty(table.Schema) ? builder.Settings.EscapeIdentifier(null, table.Name) : builder.Settings.EscapeIdentifier(null, table.Schema) + "." + builder.Settings.EscapeIdentifier(null, table.Name)) + action + builder.Settings.EscapeIdentifier(null, constraintName);
        command.ExecuteNonQuery();
        new GXSchemaManager(connection).SchemaCache.Clear();
    }

    internal static bool GetForeignKeyEnforcement(DbConnection connection, DatabaseType provider)
    {
        if (provider != DatabaseType.SqLite)
            throw new NotSupportedException("This operation is only supported for SQLite.");
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    internal static void SetForeignKeyEnforcement(DbConnection connection, DatabaseType provider, bool enabled)
    {
        if (provider != DatabaseType.SqLite)
            throw new NotSupportedException("This operation is only supported for SQLite.");
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=" + (enabled ? "ON" : "OFF");
        command.ExecuteNonQuery();
        if (GetForeignKeyEnforcement(connection, provider) != enabled)
            throw new InvalidOperationException("Foreign-key enforcement cannot be changed inside a transaction.");
    }
}
