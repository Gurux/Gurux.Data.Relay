using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Gurux.Data.Relay.Web.Server.Services;

public sealed class GXConcurrencyExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DBConcurrencyException) return;
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "The data has changed",
            Detail = context.Exception.Message
        }) { StatusCode = StatusCodes.Status409Conflict };
        context.ExceptionHandled = true;
    }
}
