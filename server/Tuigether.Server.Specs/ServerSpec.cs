using System.Diagnostics;
using System.Globalization;
using Alba;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Tuigether.Server.Specs;

/// <summary>
/// The base of every spec that sends a Command over HTTP. It clears the database before each spec.
/// </summary>
public abstract class ServerSpec(ServerFixture server) : WolverineHttpSpec(server.Host), IAsyncLifetime
{
    // A push to another node goes through the Wolverine control queue, which polls every 1 to 5 seconds.
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(20);

    private readonly Stack<IAsyncDisposable> _owned = new();
    private readonly List<IAlbaHost> _nodes = [server.Host];

    protected ServerFixture Fixture { get; } = server;

    protected static string CurrentProtocol => Protocol.Version.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The headers of a Client of the user <paramref name="userName"/> with the right API key and protocol version.
    /// </summary>
    protected static Action<Scenario> As(string userName) => Headers(ServerFixture.ApiKey, CurrentProtocol, userName);

    protected static Action<Scenario> Headers(string apiKey, string protocolVersion, string userName) =>
        scenario =>
        {
            scenario.WithRequestHeader(RequestHeaders.ApiKey, apiKey);
            scenario.WithRequestHeader(RequestHeaders.ProtocolVersion, protocolVersion);
            scenario.WithRequestHeader(RequestHeaders.UserName, userName);
        };

    /// <summary>
    /// Disposes <paramref name="resource"/> after the spec, in the reverse order of the calls.
    /// </summary>
    protected T Own<T>(T resource)
        where T : IAsyncDisposable
    {
        _owned.Push(resource);
        return resource;
    }

    /// <summary>
    /// Starts another node on the same database. The spec owns it.
    /// </summary>
    protected async Task<IAlbaHost> StartNodeAsync()
    {
        var node = Own(await Fixture.StartNodeAsync());
        _nodes.Add(node);
        return node;
    }

    /// <summary>
    /// Waits until <paramref name="arrived"/> answers true. It stops at the first error that a node of the spec logs,
    /// so a failed handler fails the spec at once and names the error.
    /// </summary>
    protected Task ThenEventuallyPushed(Func<bool> arrived, string because) =>
        ThenEventually(() => Task.FromResult(arrived()), because);

    /// <inheritdoc cref="ThenEventuallyPushed"/>
    protected async Task ThenEventually(Func<Task<bool>> arrived, string because)
    {
        var waited = Stopwatch.StartNew();
        while (!await arrived() && NodeErrorsSoFar().Count == 0 && waited.Elapsed < PushTimeout)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        var errors = NodeErrorsSoFar();
        SpecAssert.Fact(await arrived(), errors.Count > 0 ? $"{because}. A node logged: {string.Join(" | ", errors)}" : because);
    }

    /// <summary>
    /// Posts <paramref name="command"/> outside the act of the scenario, for a Given step or for concurrent requests.
    /// </summary>
    protected Task<IScenarioResult> PostAsync(object command, string user) =>
        Fixture.Host.Scenario(scenario =>
        {
            As(user)(scenario);
            scenario.Post.Json(command, JsonStyle.MinimalApi).ToUrl(RouteOf(command));
            scenario.IgnoreStatusCode();
        });

    // See "Bobcat and an invoked handler" in docs/agents/csharp.md. Each spec starts on an empty database.
    [Then("the database holds no events")]
    internal async Task ThenTheDatabaseHoldsNoEvents()
    {
        await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
        SpecAssert.Check("events in the database", await query.Events.QueryAllRawEvents().CountAsync(), 0);
    }

    private static string RouteOf(object command) =>
        command switch
        {
            CreateSession => CommandRoutes.CreateSession,
            DeleteSession => CommandRoutes.DeleteSession,
            RotateDriver => CommandRoutes.RotateDriver,
            PauseDrive => CommandRoutes.PauseDrive,
            ResumeDrive => CommandRoutes.ResumeDrive,
            SkipDrive => CommandRoutes.SkipDrive,
            SkipBreak => CommandRoutes.SkipBreak,
            AcquireNotesEditLock => CommandRoutes.AcquireNotesEditLock,
            EndNotesEditing => CommandRoutes.EndNotesEditing,
            AcquireGoalEditLock => CommandRoutes.AcquireGoalEditLock,
            EndGoalEditing => CommandRoutes.EndGoalEditing,
            AddTodo => CommandRoutes.AddTodo,
            ChangeTodoText => CommandRoutes.ChangeTodoText,
            CompleteTodo => CommandRoutes.CompleteTodo,
            ReopenTodo => CommandRoutes.ReopenTodo,
            MoveTodo => CommandRoutes.MoveTodo,
            RemoveTodo => CommandRoutes.RemoveTodo,
            _ => throw new ArgumentException($"No route for {command.GetType().Name}."),
        };

    private List<string> NodeErrorsSoFar() =>
        _nodes.SelectMany(static node => node.Services.GetRequiredService<NodeErrors>().All).ToList();

    public async ValueTask InitializeAsync()
    {
        Fixture.Host.Services.GetRequiredService<NodeErrors>().Clear();
        Fixture.Clock.Set(ServerFixture.Now);
        await ResetAsync();
    }

    public async ValueTask DisposeAsync()
    {
        while (_owned.TryPop(out var resource))
        {
            await resource.DisposeAsync();
        }
    }
}
