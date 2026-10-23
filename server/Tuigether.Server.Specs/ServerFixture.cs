using Alba;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Tuigether.Server.Specs;

/// <summary>
/// One PostgreSQL container and one server node for the whole spec run.
/// </summary>
public sealed class ServerFixture : IAsyncLifetime
{
    public const string ApiKey = "spec-api-key";

    /// <summary>
    /// The server clock in every spec, so that a spec can state the time of an event.
    /// </summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private IAlbaHost? _host;

    public IAlbaHost Host => _host ?? throw new InvalidOperationException("The fixture has not started its node.");

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
            .For<Program>(static builder =>
                builder.ConfigureServices(static services => services.AddSingleton<TimeProvider>(new FixedClock(Now))))
            .WithConfiguration("ConnectionStrings:Postgres", _postgres.GetConnectionString())
            .WithConfiguration($"{ServerOptions.Section}:{nameof(ServerOptions.ApiKey)}", ApiKey)
            .StartAsync();

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
