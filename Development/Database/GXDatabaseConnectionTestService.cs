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

using Gurux.Service.Orm;
using Gurux.Service.Orm.Model;
using Gurux.Data.Relay.Configuration;
using Gurux.Data.Relay.Log;
using System.Data;

namespace Gurux.Data.Relay.Database;

public sealed class GXDatabaseConnectionTestService : IGXDatabaseConnectionTestService
{
    private readonly IGXDatabaseConnectionFactory _connectionFactory;

    public GXDatabaseConnectionTestService(IGXDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task TestConnectionAsync(GXDatabaseConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection(configuration);
        await connection.OpenAsync(cancellationToken);
        await using GXDbConnection guruxConnection = new(connection);
        if (guruxConnection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("The database connection is not open.");
        }
    }
}
