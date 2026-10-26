using JasperFx.OpenTelemetry;
using Marten;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Wolverine;

namespace Tuigether.Server.Shared.Telemetry;

public static class Module
{
    // UseOtlpExporter reads this and the other standard OTEL_* variables itself.
    private const string OtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Logs each Command and each appended event, and exports traces and logs over OTLP. A node without an OTLP
        /// endpoint registers no exporter and sends nothing.
        /// </summary>
        public IServiceCollection AddSharedTelemetry(IConfiguration configuration)
        {
            services.ConfigureWolverine(static options =>
            {
                // Without it, a handler that SessionCommand invokes writes no start, success or failure log.
                options.InvokeTracing = InvokeTracingMode.Full;

                // Wolverine 6 logs a success at Debug, unlike its docs.
                options.Policies.MessageSuccessLogLevel(LogLevel.Information);
            });
            services.AddSingleton<IConfigureMarten, EventLog>();

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
