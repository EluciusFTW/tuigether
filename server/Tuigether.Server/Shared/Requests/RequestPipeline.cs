namespace Tuigether.Server.Shared.Requests;

public static class RequestPipeline
{
    extension(WebApplication app)
    {
        /// <summary>
        /// Runs <see cref="RequestCheckMiddleware"/>. It must run before the hub and the endpoints.
        /// </summary>
        public IApplicationBuilder UseRequestCheck() => app.UseMiddleware<RequestCheckMiddleware>();
    }
}
