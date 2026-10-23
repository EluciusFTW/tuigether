using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.AspNetCore.SignalR;
using Tuigether.Contracts;
using Tuigether.Server;
using Tuigether.Server.Driving;
using Tuigether.Server.Presence;
using Tuigether.Server.Push;
using Tuigether.Server.Requests;
using Tuigether.Server.Sessions;
using Tuigether.Server.Startup;
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
builder.Services.AddHostedService<ContractsVersionCheck>();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IUserIdProvider, UserNameHeaderProvider>();

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
        options.Projections.Add<SessionDataProjection>(ProjectionLifecycle.Inline);
        options.Projections.Add<SessionParticipantsProjection>(ProjectionLifecycle.Inline);
        options.Projections.Add<SessionTimerProjection>(ProjectionLifecycle.Inline);
        options.Projections.Add<SessionDriveHistoryProjection>(ProjectionLifecycle.Inline);
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

    // A durable queue keeps a scheduled leave check across a node restart.
    options.PublishMessage<CheckParticipantLeft>().ToLocalQueue("presence").UseDurableInbox();

    // Inline: a hub call returns after its handler ran. SignalR runs the calls of one connection one at a time, so
    // the node handles the messages of a Client in their order, for example a close and the next open.
    options.UseSignalR<TuigetherHub>().ProcessInline();
    options.PublishMessage<SessionSummary>().ToSignalR();
    options.PublishMessage<SessionRemoved>().ToSignalR();
    options.PublishMessage<SessionData>().ToSignalR();
    options.PublishMessage<SessionParticipants>().ToSignalR();
    options.PublishMessage<SessionTimer>().ToSignalR();
    options.PublishMessage<SessionDriveHistory>().ToSignalR();
});

builder.Services.AddWolverineHttp();

var app = builder.Build();

app.UseMiddleware<RequestCheckMiddleware>();
app.MapHealthChecks(ServerRoutes.Health);
app.MapWolverineSignalRHub<TuigetherHub>(HubProtocol.Path);
app.MapWolverineEndpoints(static options => options.ApplyCorrelationIdHeaderToAllEndpoints = true);

return await app.RunJasperFxCommands(args);
