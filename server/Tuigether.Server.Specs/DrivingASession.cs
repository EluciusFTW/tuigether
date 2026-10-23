using Marten;
using Microsoft.Extensions.DependencyInjection;
using Tuigether.Server.Domain;

namespace Tuigether.Server.Specs;

[BobcatFeature("Driving a Session")]
public sealed class DrivingASession(ServerFixture server) : ServerSpec(server)
{
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(25);
    private static readonly DateTimeOffset EndsAt = ServerFixture.Now + Duration;

    // A Drive that started 10 minutes before the server clock and has 15 minutes left.
    private static readonly DateTimeOffset StartedBefore = ServerFixture.Now - TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Left = TimeSpan.FromMinutes(15);

    private readonly Guid _sessionId = Guid.CreateVersion7();
    private long _givenVersion;

    [Fact]
    public async Task a_rotate_from_an_idle_timer_starts_a_Drive_of_the_next_Driver()
    {
        await GivenTheSession(AnnAndBobJoined());

        await WhenPosted(new RotateDriver(_sessionId, null, "bob", Duration), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(DriverChanged("bob", "ann"), DriveStarted("bob", EndsAt, "ann"));
        await ThenReadModel<SessionTimer>(_sessionId, new SessionTimer(_sessionId, TimerPhase.DriveRunning, "bob", EndsAt, Duration));
        await ThenReadModel<SessionDriveHistory>(_sessionId, History(Entry(DriveHistoryKind.Started, "bob", "ann")));
    }

    [Fact]
    public async Task a_rotate_during_a_Drive_switches_the_Drive_to_the_next_Driver()
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives()]);

        await WhenPosted(new RotateDriver(_sessionId, "bob", "ann", Duration), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(DriveSwitched("ann"), DriverChanged("ann", "ann"), DriveStarted("ann", EndsAt, "ann"));
        await ThenReadModel<SessionTimer>(_sessionId, new SessionTimer(_sessionId, TimerPhase.DriveRunning, "ann", EndsAt, Duration));
        await ThenReadModel<SessionDriveHistory>(
            _sessionId,
            History(
                Entry(DriveHistoryKind.Started, "bob", "ann", StartedBefore),
                Entry(DriveHistoryKind.Switched, "bob", "ann"),
                Entry(DriveHistoryKind.Started, "ann", "ann")));
    }

    [Fact]
    public async Task a_rotate_with_a_stale_current_Driver_is_rejected()
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives()]);

        await WhenPosted(new RotateDriver(_sessionId, null, "ann", Duration), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("Another Participant changed the Driver to bob.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_rotate_to_an_absent_next_Driver_is_rejected()
    {
        await GivenTheSession(AnnAndBobJoined());

        await WhenPosted(new RotateDriver(_sessionId, null, "cid", Duration), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("cid is not a Participant of the Session.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_running_Drive_is_paused_with_its_remaining_time()
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives()]);

        await WhenPosted(new PauseDrive(_sessionId), As("bob"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new DrivePaused(_sessionId, Left, "bob", ServerFixture.Now));
        await ThenReadModel<SessionTimer>(_sessionId, new SessionTimer(_sessionId, TimerPhase.DrivePaused, "bob", null, Left));
    }

    [Fact]
    public async Task a_paused_Drive_is_resumed_with_an_end_from_the_server_clock()
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives(), new DrivePaused(_sessionId, Left, "bob", StartedBefore)]);

        await WhenPosted(new ResumeDrive(_sessionId), As("bob"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new DriveResumed(_sessionId, ServerFixture.Now + Left, "bob", ServerFixture.Now));
        await ThenReadModel<SessionTimer>(
            _sessionId,
            new SessionTimer(_sessionId, TimerPhase.DriveRunning, "bob", ServerFixture.Now + Left, Left));
        await ThenReadModel<SessionDriveHistory>(
            _sessionId,
            History(
                Entry(DriveHistoryKind.Started, "bob", "ann", StartedBefore),
                Entry(DriveHistoryKind.Paused, "bob", "bob", StartedBefore),
                Entry(DriveHistoryKind.Resumed, "bob", "bob")));
    }

    [Fact]
    public async Task a_pause_without_a_running_Drive_is_rejected()
    {
        await GivenTheSession(AnnAndBobJoined());

        await WhenPosted(new PauseDrive(_sessionId), As("bob"));

        ThenResponseIs(409);
        ThenRefusedWith("No Drive runs.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_resume_without_a_paused_Drive_is_rejected()
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives()]);

        await WhenPosted(new ResumeDrive(_sessionId), As("bob"));

        ThenResponseIs(409);
        ThenRefusedWith("No Drive is paused.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_Drive_Command_on_an_unknown_Session_is_not_found()
    {
        await WhenPosted(new PauseDrive(Guid.CreateVersion7()), As("bob"));

        ThenResponseIs(404);
        ThenNoEvents();
    }

    [Fact]
    public async Task two_concurrent_rotations_give_one_rotation()
    {
        await GivenTheSession(AnnAndBobJoined());
        var statuses = new List<int>();

        await WhenActing(
            "ann and bob rotate the Driver at the same moment",
            async _ =>
            {
                var responses = await Task.WhenAll(
                    PostAsync(new RotateDriver(_sessionId, null, "ann", Duration), "ann"),
                    PostAsync(new RotateDriver(_sessionId, null, "bob", Duration), "bob"));
                statuses.AddRange(responses.Select(static response => response.Context.Response.StatusCode).Order());
            });

        ThenTheActSucceeded();
        statuses.ShouldBe([204, 409]);
        var events = await StreamEventsAsync();
        events.OfType<DriverChanged>().ShouldHaveSingleItem();
        events.OfType<DriveStarted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task the_Driver_leaves_and_the_Drive_pauses_with_no_Driver()
    {
        await GivenEvents<Session>(_sessionId, Created());
        var ann = await GivenAClientWithTheSessionOpen("ann", "Fox");
        var bob = await GivenAClientWithTheSessionOpen("bob", "Owl");
        await GivenTheDriverDrives("ann");

        await WhenActing("the Driver ann closes the Session", _ => ann.SendAsync(new CloseSession(_sessionId)));

        ThenTheActSucceeded();
        await ThenEventually(
            async () => (await StreamEventsAsync()).OfType<DriverChanged>().Any(static changed => changed.Driver is null),
            "the stream has no DriverChanged with no Driver");
        var events = await StreamEventsAsync();
        events.SkipWhile(static e => e is not ParticipantLeft).ShouldBe(
            [
                new ParticipantLeft(_sessionId, "ann", ServerFixture.Now),
                new DrivePaused(_sessionId, Duration, "ann", ServerFixture.Now),
                DriverChanged(null, "ann"),
            ]);
        await ThenEventuallyPushed(
            () => bob.Received<SessionTimer>().Contains(new(_sessionId, TimerPhase.DrivePaused, null, null, Duration)),
            "bob's Client did not get the paused Drive with no Driver");
    }

    [Given("the Session has these events")]
    internal async Task GivenTheSession(object[] events)
    {
        await GivenEvents<Session>(_sessionId, events);
        _givenVersion = events.Length;
    }

    // See "Bobcat and an invoked handler" in docs/agents/csharp.md.
    [Then("the Session stream gained exactly these events")]
    internal Task ThenTheSessionGained(params object[] events) => ThenEventsOn<Session>(_sessionId, _givenVersion, events);

    [Given("a Client of the User has the Session open")]
    internal async Task<HubClient> GivenAClientWithTheSessionOpen(string user, string avatar)
    {
        var client = Own(await HubClient.ConnectAsync(Fixture.Host, ServerFixture.ApiKey, CurrentProtocol, user));
        await client.SendAsync(new OpenSession(_sessionId, avatar));
        await ThenEventuallyPushed(
            () => client.Received<SessionParticipants>().Any(participants => participants.Participants.Any(p => p.User == user)),
            $"the Client of {user} got no Participants with {user}");
        return client;
    }

    [Given("the User drives")]
    internal async Task GivenTheDriverDrives(string driver)
    {
        var response = await PostAsync(new RotateDriver(_sessionId, null, driver, Duration), driver);
        response.Context.Response.StatusCode.ShouldBe(204);
    }

    private async Task<IReadOnlyList<object>> StreamEventsAsync()
    {
        await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
        var events = await query.Events.FetchStreamAsync(_sessionId);
        return events.Select(static e => e.Data).ToList();
    }

    private SessionCreated Created() => new(_sessionId, "Mob on the timer", "tuigether", "main", "ann", ServerFixture.Now);

    private object[] AnnAndBobJoined() =>
        [
            Created(),
            new ParticipantJoined(_sessionId, "ann", "Fox", ServerFixture.Now),
            new SessionStarted(_sessionId, "ann", ServerFixture.Now),
            new ParticipantJoined(_sessionId, "bob", "Owl", ServerFixture.Now),
        ];

    private object[] BobDrives() =>
        [
            new DriverChanged(_sessionId, "bob", "ann", StartedBefore),
            new DriveStarted(_sessionId, "bob", ServerFixture.Now + Left, "ann", StartedBefore),
        ];

    private DriverChanged DriverChanged(string? driver, string user) => new(_sessionId, driver, user, ServerFixture.Now);

    private DriveStarted DriveStarted(string driver, DateTimeOffset endsAt, string user) =>
        new(_sessionId, driver, endsAt, user, ServerFixture.Now);

    private DriveSwitched DriveSwitched(string user) => new(_sessionId, user, ServerFixture.Now);

    private static DriveHistoryEntry Entry(DriveHistoryKind kind, string driver, string user, DateTimeOffset? at = null) =>
        new(kind, driver, user, at ?? ServerFixture.Now);

    private SessionDriveHistory History(params DriveHistoryEntry[] entries) => new(_sessionId) { Entries = [.. entries] };
}
