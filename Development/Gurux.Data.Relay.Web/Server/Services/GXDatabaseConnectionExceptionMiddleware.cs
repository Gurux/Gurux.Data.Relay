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
