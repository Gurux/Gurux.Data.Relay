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

using System.Data;
using Gurux.Data.Relay.Shared;
using Gurux.Service.Orm;

namespace Gurux.Data.Relay.Configuration;

internal static class GXEntityPersistence
{
    public static void CopyMetadata(IGXEntityMetadata source, IGXEntityMetadata target)
    {
        target.CreationTime = source.CreationTime;
        target.Updated = source.Updated;
        target.ConcurrencyStamp = source.ConcurrencyStamp;
    }

    public static void Initialize(IGXEntityMetadata value)
    {
        value.CreationTime = DateTimeOffset.Now;
        value.Updated = value.CreationTime;
        value.ConcurrencyStamp = Guid.NewGuid().ToString();
    }

    public static async Task SaveAsync<T>(GXDbConnection connection, IDbTransaction transaction,
        T value, T? existing, CancellationToken cancellationToken, DateTimeOffset? importedAt = null) where T : class, IGXEntityMetadata
    {
        if (existing is null)
        {
            if (value.ConcurrencyStamp is not null)
            {
                throw Conflict<T>();
            }
            Initialize(value);
            if (importedAt is not null) value.CreationTime = value.Updated = importedAt;
            await connection.InsertAsync(transaction, GXInsertArgs.Insert(value), cancellationToken);
            return;
        }

        string? expected = value.ConcurrencyStamp;
        if (expected is null || !string.Equals(expected, existing.ConcurrencyStamp, StringComparison.Ordinal))
        {
            throw Conflict<T>();
        }
        value.CreationTime = importedAt ?? existing.CreationTime;
        value.Updated = importedAt ?? DateTimeOffset.UtcNow;
        value.ConcurrencyStamp = Guid.NewGuid().ToString();
        var update = GXUpdateArgs.Update(value);
        update.Where.And<T>(row => row.ConcurrencyStamp == expected);
        update.UseQueryCache(connection.QueryCache);
        int count = await connection.UpdateAsync(transaction, update, cancellationToken);
        if (count != 1)
        {
            throw Conflict<T>();
        }
    }

    private static DBConcurrencyException Conflict<T>() => new(
        $"{typeof(T).Name} has changed or was deleted since it was loaded. Reload the data before saving.");
}
