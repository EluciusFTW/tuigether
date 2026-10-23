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

        var headers = new RequestHeaderValues(
            context.Request.Headers[RequestHeaders.ApiKey].FirstOrDefault(),
            context.Request.Headers[RequestHeaders.ProtocolVersion].FirstOrDefault(),
            context.Request.Headers[RequestHeaders.UserName].FirstOrDefault());

        return RequestRules.Check(headers, options.Value.ApiKey).Match(_ => next(context), rejection => Reject(context, rejection));
    }

    private static Task Reject(HttpContext context, RequestRejection rejection)
    {
        var (status, reason) = RequestRules.Describe(rejection);
        return Results.Problem(statusCode: status, detail: reason).ExecuteAsync(context);
    }
}
