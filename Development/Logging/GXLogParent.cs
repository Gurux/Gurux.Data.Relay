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
using Gurux.Data.Relay.Shared;
using Gurux.Data.Relay.Shared.Enums;
using Gurux.Service.Orm;

namespace Gurux.Data.Relay.Log;

internal static class GXLogParent
{
    public static async Task<Guid> ResolveAsync(
        GXDbConnection connection, 
        ApplicationMode mode, CancellationToken cancellationToken)
    {
        var settings = await connection.SelectAsync<GXSettings>(
            Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXSettings>(connection, ("Mode", mode)), cancellationToken);
        var existing = settings.SingleOrDefault();
        if (existing is not null) return existing.Id;
        var parent = new GXSettings
        {
            Id = Gurux.Data.Relay.Configuration.GXTransportRouting.StableId($"settings:{mode}"), Mode = mode
        };
        Gurux.Data.Relay.Configuration.GXEntityPersistence.Initialize(parent);
        try { await connection.InsertAsync(GXInsertArgs.Insert(parent), cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Another logger may have created this mode's parent concurrently.
            var created = await connection.SingleOrDefaultAsync<GXSettings>(Gurux.Data.Relay.Database.GXMetadataQueries.Select<GXSettings>(connection, ("Id", parent.Id)), cancellationToken);
            if (created is null) throw;
        }
        return parent.Id;
    }
}


