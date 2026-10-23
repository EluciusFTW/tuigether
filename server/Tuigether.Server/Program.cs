using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Tuigether.Server;
using Tuigether.Server.Requests;
using Tuigether.Server.Sessions;
using Tuigether.Server.Startup;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<ServerOptions>()
    .BindConfiguration(ServerOptions.Section)
    .Validate(static options => !string.IsNullOrWhiteSpace(options.ApiKey), $"Set {ServerOptions.Section}:ApiKey.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<ContractsVersionCheck>();
builder.Services.AddHealthChecks();

builder.Services
    .AddMarten(options =>
    {
        options.Connection(
            builder.Configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Set ConnectionStrings:Postgres."));
        options.DatabaseSchemaName = "tuigether";

        // Bobcat finds the events that a request caused by its correlation id.
        options.Events.MetadataConfig.CorrelationIdEnabled = true;

        options.Projections.Snapshot<Session>(SnapshotLifecycle.Inline);
        options.Projections.Add<SessionSummaryProjection>(ProjectionLifecycle.Inline);
    })
    .IntegrateWithWolverine()
    .UseLightweightSessions();

builder.Host.UseWolverine(static options =>
{
    options.ServiceName = "tuigether";

    // Under a test host the entry assembly is the spec assembly, so Wolverine finds no endpoints without this.
    options.Discovery.IncludeAssembly(typeof(Program).Assembly);
    options.Policies.AutoApplyTransactions();
});

builder.Services.AddWolverineHttp();

var app = builder.Build();

app.UseMiddleware<RequestCheckMiddleware>();
app.MapHealthChecks(ServerRoutes.Health);
app.MapWolverineEndpoints(static options => options.ApplyCorrelationIdHeaderToAllEndpoints = true);

return await app.RunJasperFxCommands(args);
