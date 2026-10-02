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

using Gurux.Data.Relay.Shared.Enums;
using Gurux.Data.Relay.Enums;

namespace Gurux.Data.Relay.Configuration;

public sealed class GXClientConfiguration : GXModeConfiguration,
    System.Text.Json.Serialization.IJsonOnDeserialized, System.Text.Json.Serialization.IJsonOnSerializing
{
    public List<GXDatabaseConfiguration> Databases { get; set; } = [];
    public List<GXTransportConfiguration> Transports { get; set; } = [];

    public GXClientConfiguration()
    {
        Mode = ApplicationMode.Client;
    }

    public IReadOnlyList<GXDatabaseConfiguration> GetSourceDatabases()
    {
        if (Databases.Count == 0)
        {
            throw new InvalidOperationException("Client configuration must contain at least one source database in Databases.");
        }

        EnsureRoutingIds();
        return Databases;
    }

    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized() => EnsureRoutingIds();
    void System.Text.Json.Serialization.IJsonOnSerializing.OnSerializing() => EnsureRoutingIds();

    public void EnsureRoutingIds()
    {
        GXTransportRouting.EnsureDatabaseIds(Databases);
        GXTransportRouting.EnsureTransportIds(Transports);
    }

    public IReadOnlyList<GXTransportConfiguration> GetTransports(int databaseIndex, string tableName)
    {
        EnsureRoutingIds();
        Guid databaseId = Databases[databaseIndex].Id;
        return Transports.Where(t => t.Tables.Any(route => route.DatabaseId == databaseId &&
            string.Equals(route.Table, tableName, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}



