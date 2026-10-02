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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Gurux.Data.Relay.Web.Server.Services;

public sealed class GXDatabaseConnectionExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted && CannotOpenDatabase(ex))
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Cannot open the SQL Server database",
                Detail = "The selected database does not exist or the login does not have permission to access it. " +
                    "Check the Server and Database values in the connection string. " +
                    "Create the database if it is missing, or grant the login access, then test the connection again."
            }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
        }
    }

    private static bool CannotOpenDatabase(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Errors.Cast<SqlError>().Any(error => error.Number == 4060)) return true;
            if (current is GXDatabaseException { ErrorCode: 4060 }) return true;
        }
        return false;
    }
}
