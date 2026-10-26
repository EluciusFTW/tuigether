using Alba;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Tuigether.Server.Shared;
using Wolverine;

namespace Tuigether.Server.Specs;

/// <summary>
/// One PostgreSQL container and one server node for the whole spec run.
/// </summary>
public sealed class ServerFixture : IAsyncLifetime
{
    public const string ApiKey = "spec-api-key";

    /// <summary>
    /// The server clock at the start of every spec, so that a spec can state the time of an event.
    /// </summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Short enough for a spec to wait for the end of a Break.
    /// </summary>
    public static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Long enough for a Client to open a Session again within it, short enough for a spec to wait for its end.
    /// </summary>
    public static readonly TimeSpan LeaveGracePeriod = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Short, so that a live node has a heartbeat soon after the reset at the start of a spec.
    /// </summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(3);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private IAlbaHost? _host;

    public IAlbaHost Host => _host ?? throw new InvalidOperationException("The fixture has not started its node.");

    /// <summary>
    /// The server clock of every node. It stands still until a spec moves it.
    /// </summary>
    public SpecClock Clock { get; } = new(Now);

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _host = await StartNodeAsync();
    }

    /// <summary>
    /// Starts another node on the same database.
    /// </summary>
    public Task<IAlbaHost> StartNodeAsync() =>
        AlbaHost
            .For<Program>(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<TimeProvider>(Clock);
                    services.AddSingleton<NodeErrors>();
                    services.AddSingleton<ILoggerProvider>(static provider => provider.GetRequiredService<NodeErrors>());

                    // No tracked host receives a message to another node or to a SignalR client, so a tracked session
                    // would wait for it until the timeout. A spec waits on the SignalR client instead.
                    services.ConfigureWolverine(static options =>
                        options.Tracking.IgnoreEnvelopes(static envelope => envelope.Destination?.Scheme is "dbcontrol" or "signalr"));
                }))
            .WithConfiguration("ConnectionStrings:Postgres", _postgres.GetConnectionString())
            .WithConfiguration($"{ServerOptions.Section}:{nameof(ServerOptions.ApiKey)}", ApiKey)
            .WithConfiguration($"{ServerOptions.Section}:{nameof(ServerOptions.LeaveGracePeriod)}", LeaveGracePeriod.ToString())
            .WithConfiguration($"{ServerOptions.Section}:{nameof(ServerOptions.BreakDuration)}", BreakDuration.ToString())
            .WithConfiguration($"{ServerOptions.Section}:{nameof(ServerOptions.HeartbeatInterval)}", HeartbeatInterval.ToString())
            .WithConfiguration($"{ServerOptions.Section}:{nameof(ServerOptions.HeartbeatTimeout)}", HeartbeatTimeout.ToString())
            .StartAsync();

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}

/// <summary>
/// Handlers read the clock on other threads. So it holds the UTC ticks, which one write replaces as a whole.
/// </summary>
public sealed class SpecClock(DateTimeOffset start) : TimeProvider
{
    private long _utcTicks = start.UtcTicks;

    public void Set(DateTimeOffset now) => Interlocked.Exchange(ref _utcTicks, now.UtcTicks);

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
}
