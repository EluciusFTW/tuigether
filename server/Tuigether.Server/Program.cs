using JasperFx;
using Marten;
using Tuigether.Server.Domain;
using Tuigether.Server.Driving;
using Tuigether.Server.Goal;
using Tuigether.Server.Notes;
using Tuigether.Server.Presence;
using Tuigether.Server.Sessions;
using Tuigether.Server.Shared;
using Tuigether.Server.Shared.Hubs;
using Tuigether.Server.Shared.Requests;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Http;
using Wolverine.Marten;
using Wolverine.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<ServerOptions>()
    .BindConfiguration(ServerOptions.Section)
    .Validate(static options => !string.IsNullOrWhiteSpace(options.ApiKey), $"Set {ServerOptions.Section}:ApiKey.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddShared();
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
    })
    .IntegrateWithWolverine()
    .UseLightweightSessions();

builder.Host.UseWolverine(static options =>
{
    options.ServiceName = "tuigether";

    // Under a test host the entry assembly is the spec assembly, so Wolverine finds no endpoints without this.
    options.Discovery.IncludeAssembly(typeof(Program).Assembly);
    options.Policies.AutoApplyTransactions();

    // BroadcastToAllNodesAsync reaches the other nodes only in this mode.
    options.Durability.Mode = DurabilityMode.Balanced;

    // A scheduled message runs close to its time only with a short polling interval. The default is 5 seconds.
    options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);

    // Two handlers that append to one Session at the same moment: the second one runs again against the new state.
    options.Policies.OnException<ConcurrencyException>().RetryWithCooldown(
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250));

    // UseSignalR registers services, so it cannot move into a module (docs/agents/csharp.md, "SignalR in a module").
    // Inline: a hub call returns after its handler ran. SignalR runs the calls of one connection one at a time, so
    // the node handles the messages of a Client in their order, for example a close and the next open.
    options.UseSignalR<TuigetherHub>().ProcessInline();
});

builder.Services
    .AddDomain()
    .AddSessions()
    .AddPresence()
    .AddDriving()
    .AddNotes()
    .AddGoal();

builder.Services.AddWolverineHttp();

var app = builder.Build();

app.UseRequestCheck();
app.MapHealthChecks(ServerRoutes.Health);
app.MapTuigetherHub();
app.MapWolverineEndpoints(static options => options.ApplyCorrelationIdHeaderToAllEndpoints = true);

return await app.RunJasperFxCommands(args);
