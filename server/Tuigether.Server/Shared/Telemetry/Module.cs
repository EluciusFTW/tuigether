using JasperFx.OpenTelemetry;
using Marten;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Tuigether.Server.Shared.Telemetry;

public static class Module
{
    // UseOtlpExporter reads this and the other standard OTEL_* variables itself.
    private const string OtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Exports traces and logs over OTLP. A node without an OTLP endpoint registers nothing and sends nothing.
        /// </summary>
        public IServiceCollection AddSharedTelemetry(IConfiguration configuration)
        {
            if (string.IsNullOrWhiteSpace(configuration[OtlpEndpoint]))
            {
                return services;
            }

            services.ConfigureMarten(static options => options.OpenTelemetry.TrackConnections = TrackLevel.Normal);
            services
                .AddOpenTelemetry()
                .WithTracing(static tracing => tracing.AddAspNetCoreInstrumentation().AddSource("Wolverine").AddSource("Marten"))
                .WithLogging()
                .UseOtlpExporter();
            return services;
        }
    }
}
