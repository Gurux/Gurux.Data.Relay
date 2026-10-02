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

namespace Gurux.Data.Relay.Server;

public sealed record GXServerMessageRoute(int? DatabaseIndex, string? DestinationTable, string? RouteKey)
{
    public static GXServerMessageRoute Resolve(GXServerConfiguration configuration, GXTransportConfiguration transport, string source,
        bool allowNewTable = false)
    {
        IReadOnlyList<GXDatabaseConfiguration> databases = configuration.GetTargetDatabases();
        if (transport.Id != Guid.Empty)
        {
            Guid transportId = transport.Id;
            transport = configuration.Transports.SingleOrDefault(current => current.Id == transportId)
                ?? throw new InvalidOperationException("This transport has been removed from the server configuration.");
        }
        var matches = transport.Tables.Where(table => string.Equals(
            string.IsNullOrWhiteSpace(table.Source) ? table.Table : table.Source,
            source, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException($"Source table '{source}' has ambiguous transport routes.");
        if (matches.Count == 1)
        {
            GXTransportTableConfiguration table = matches[0];
            int index = FindDatabase(databases, table.DatabaseId);
            if (string.IsNullOrWhiteSpace(table.Table))
                throw new InvalidOperationException("Transport destination table is required.");
            return new(index, table.Table, $"{table.DatabaseId:N}:{table.Table.ToUpperInvariant()}");
        }
        if (allowNewTable)
        {
            Guid[] candidates = transport.Tables.Select(table => table.DatabaseId).Distinct().ToArray();
            if (candidates.Length == 0)
                candidates = databases.Select(db => db.Id).ToArray();
            if (candidates.Length != 1)
                throw new InvalidOperationException($"Cannot select a destination database for new source table '{source}'. Configure a transport mapping for this source table.");
            int index = FindDatabase(databases, candidates[0]);
            return new(index, source, $"{candidates[0]:N}:{source.ToUpperInvariant()}");
        }
        throw new InvalidOperationException($"Source table '{source}' is not configured for this transport.");
    }

    private static int FindDatabase(IReadOnlyList<GXDatabaseConfiguration> databases, Guid id)
    {
        int found = -1;
        for (int index = 0; index != databases.Count; ++index)
        {
            if (databases[index].Id != id || id == Guid.Empty)
                continue;
            if (found != -1)
                throw new InvalidOperationException($"Destination database '{id}' is ambiguous.");
            found = index;
        }
        return found >= 0 ? found : throw new InvalidOperationException($"Destination database '{id}' was not found.");
    }
}


