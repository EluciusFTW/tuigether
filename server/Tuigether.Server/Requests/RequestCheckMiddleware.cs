using Microsoft.Extensions.Options;
using Tuigether.Contracts;

namespace Tuigether.Server.Requests;

/// <summary>
/// Checks the headers of every request before an endpoint sees it. The health check needs no headers.
/// </summary>
public sealed class RequestCheckMiddleware(RequestDelegate next, IOptions<ServerOptions> options)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(ServerRoutes.Health))
        {
            return next(context);
        }

        var headers = RequestHeaderValues.Read(context.Request.Headers);

        // TuigetherHub checks the other headers of a connect, so that the client gets the reason.
        return context.Request.Path.StartsWithSegments(HubProtocol.Path)
            ? RequestRules.CheckApiKey(headers, options.Value.ApiKey).Match(_ => next(context), rejection => Reject(context, rejection))
            : RequestRules.Check(headers, options.Value.ApiKey).Match(_ => next(context), rejection => Reject(context, rejection));
    }

    private static Task Reject(HttpContext context, RequestRejection rejection)
    {
        var (status, reason) = RequestRules.Describe(rejection);
        return Results.Problem(statusCode: status, detail: reason).ExecuteAsync(context);
    }
}
