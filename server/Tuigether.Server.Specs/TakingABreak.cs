using Marten;
using Microsoft.Extensions.DependencyInjection;
using Tuigether.Server.Domain;
using Tuigether.Server.Driving;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Util;

namespace Tuigether.Server.Specs;

[BobcatFeature("Ending a Drive and taking a Break")]
public sealed class TakingABreak(ServerFixture server) : ServerSpec(server)
{
    // Short enough for a spec to wait for the scheduled end.
    private static readonly TimeSpan ShortDrive = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(25);

    // A Break that started 1 minute before the server clock and has 4 minutes left.
    private static readonly DateTimeOffset BreakStartedAt = ServerFixture.Now - TimeSpan.FromMinutes(1);
    private static readonly DateTimeOffset BreakEndsAt = ServerFixture.Now + TimeSpan.FromMinutes(4);

    private readonly Guid _sessionId = Guid.CreateVersion7();
    private long _givenVersion;

    [Fact]
    public async Task a_Drive_ends_on_time_with_no_Client_open()
    {
        await GivenTheSession(AnnAndBobJoined());
        var endsAt = ServerFixture.Now + ShortDrive;

        await WhenPosted(new RotateDriver(_sessionId, null, "bob", ShortDrive), As("ann"));

        ThenResponseIs(204);
        await ThenEventuallyTheSessionHas<BreakStarted>();
        await ThenTheSessionGainedFirst(
            DriverChanged("bob", "ann"),
            new DriveStarted(_sessionId, "bob", endsAt, "ann", ServerFixture.Now),
            new DriveFinished(_sessionId, endsAt),
            new BreakStarted(_sessionId, endsAt + ServerFixture.BreakDuration, null, endsAt));
        await ThenReadModel<SessionDriveHistory>(
            _sessionId,
            History(Entry(DriveHistoryKind.Started, "bob", "ann", ServerFixture.Now), Entry(DriveHistoryKind.Finished, "bob", "bob", endsAt)));
    }

    [Fact]
    public async Task a_Drive_paused_before_its_end_does_not_finish()
    {
        await GivenTheSession(AnnAndBobJoined());
        await GivenTheDriverDrives("bob", ShortDrive);

        await WhenPosted(new PauseDrive(_sessionId), As("bob"));

        ThenResponseIs(204);
        await ThenEventuallyNoScheduledDriveEndIsDue();
        await ThenTheSessionGained(
            DriverChanged("bob", "bob"),
            new DriveStarted(_sessionId, "bob", ServerFixture.Now + ShortDrive, "bob", ServerFixture.Now),
            new DrivePaused(_sessionId, ShortDrive, "bob", ServerFixture.Now));
    }

    [Fact]
    public async Task after_a_resume_the_old_end_is_ignored_and_the_new_end_finishes_the_Drive()
    {
        await GivenTheSession(AnnAndBobJoined());
        await GivenTheDriverDrives("bob", ShortDrive);
        await GivenPosted(new PauseDrive(_sessionId), "bob");
        var resumedAt = ServerFixture.Now + TimeSpan.FromMinutes(1);
        GivenTheServerClockAt(resumedAt);
        var endsAt = resumedAt + ShortDrive;

        await WhenPosted(new ResumeDrive(_sessionId), As("bob"));

        ThenResponseIs(204);
        await ThenEventuallyTheSessionHas<BreakStarted>();
        await ThenEventuallyNoScheduledDriveEndIsDue();
        await ThenTheSessionGainedFirst(
            DriverChanged("bob", "bob"),
            new DriveStarted(_sessionId, "bob", ServerFixture.Now + ShortDrive, "bob", ServerFixture.Now),
            new DrivePaused(_sessionId, ShortDrive, "bob", ServerFixture.Now),
            new DriveResumed(_sessionId, endsAt, "bob", resumedAt),
            new DriveFinished(_sessionId, endsAt),
            new BreakStarted(_sessionId, endsAt + ServerFixture.BreakDuration, null, endsAt));
        (await StreamEventsAsync()).OfType<DriveFinished>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task a_skipped_Drive_starts_the_Break()
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives()]);

        await WhenPosted(new SkipDrive(_sessionId), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGainedFirst(
            new DriveSkipped(_sessionId, "ann", ServerFixture.Now),
            new BreakStarted(_sessionId, ServerFixture.Now + ServerFixture.BreakDuration, "ann", ServerFixture.Now));
        await ThenReadModel<SessionDriveHistory>(
            _sessionId,
            History(
                Entry(DriveHistoryKind.Started, "bob", "ann", ServerFixture.Now - TimeSpan.FromMinutes(10)),
                Entry(DriveHistoryKind.Skipped, "bob", "ann", ServerFixture.Now)));
    }

    [Fact]
    public async Task a_skip_without_a_running_Drive_is_rejected()
    {
        await GivenTheSession(AnnAndBobJoined());

        await WhenPosted(new SkipDrive(_sessionId), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("No Drive runs.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_Break_ends_on_time()
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives()]);
        var breakEndsAt = ServerFixture.Now + ServerFixture.BreakDuration;

        await WhenPosted(new SkipDrive(_sessionId), As("ann"));

        ThenResponseIs(204);
        await ThenEventuallyTheSessionHas<BreakEnded>();
        await ThenTheSessionGained(
            new DriveSkipped(_sessionId, "ann", ServerFixture.Now),
            new BreakStarted(_sessionId, breakEndsAt, "ann", ServerFixture.Now),
            new BreakEnded(_sessionId, breakEndsAt));
        await ThenReadModel<SessionTimer>(_sessionId, new SessionTimer(_sessionId, TimerPhase.Idle, "bob", null, TimeSpan.Zero));
    }

    [Fact]
    public async Task a_skipped_Break_ends_the_Break()
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives(), .. BobsDriveEndsInABreak()]);

        await WhenPosted(new SkipBreak(_sessionId), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new BreakSkipped(_sessionId, "ann", ServerFixture.Now));
        await ThenReadModel<SessionTimer>(_sessionId, new SessionTimer(_sessionId, TimerPhase.Idle, "bob", null, TimeSpan.Zero));
    }

    [Fact]
    public async Task a_skip_of_a_Break_without_a_Break_is_rejected()
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives()]);

        await WhenPosted(new SkipBreak(_sessionId), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("No Break runs.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_rotate_during_a_Break_skips_the_Break_and_starts_the_next_Drive()
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives(), .. BobsDriveEndsInABreak()]);
        var endsAt = ServerFixture.Now + Duration;

        await WhenPosted(new RotateDriver(_sessionId, "bob", "ann", Duration), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(
            new BreakSkipped(_sessionId, "ann", ServerFixture.Now),
            DriverChanged("ann", "ann"),
            new DriveStarted(_sessionId, "ann", endsAt, "ann", ServerFixture.Now));
        await ThenReadModel<SessionTimer>(_sessionId, new SessionTimer(_sessionId, TimerPhase.DriveRunning, "ann", endsAt, Duration));
    }

    [Fact]
    public async Task a_pause_during_a_Break_is_rejected() => await ThenRejectedDuringABreak(new PauseDrive(_sessionId));

    [Fact]
    public async Task a_resume_during_a_Break_is_rejected() => await ThenRejectedDuringABreak(new ResumeDrive(_sessionId));

    [Fact]
    public async Task a_skip_of_a_Drive_during_a_Break_is_rejected() => await ThenRejectedDuringABreak(new SkipDrive(_sessionId));

    [Fact]
    public async Task a_Client_that_opens_the_Session_during_a_Break_gets_the_Break()
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives(), .. BobsDriveEndsInABreak()]);
        var cid = Own(await HubClient.ConnectAsync(Fixture.Host, ServerFixture.ApiKey, CurrentProtocol, "cid"));

        await WhenActing("cid opens the Session", _ => cid.SendAsync(new OpenSession(_sessionId, "Elk")));

        ThenTheActSucceeded();
        await ThenEventuallyPushed(
            () => cid.Received<SessionTimer>().Contains(
                new(_sessionId, TimerPhase.Break, "bob", BreakEndsAt, BreakEndsAt - BreakStartedAt)),
            "cid's Client did not get the Break");
    }

    [Given("the Session has these events")]
    internal async Task GivenTheSession(object[] events)
    {
        await GivenEvents<Session>(_sessionId, events);
        _givenVersion = events.Length;
    }

    [Given("the User drives")]
    internal Task GivenTheDriverDrives(string driver, TimeSpan duration) =>
        GivenPosted(new RotateDriver(_sessionId, null, driver, duration), driver);

    [Given("the User posted the Command")]
    internal async Task GivenPosted(object command, string user)
    {
        var response = await PostAsync(command, user);
        response.Context.Response.StatusCode.ShouldBe(204);
    }

    [Given("the server clock shows")]
    internal void GivenTheServerClockAt(DateTimeOffset now) => Fixture.Clock.Set(now);

    // See "Bobcat and an invoked handler" in docs/agents/csharp.md.
    [Then("the Session stream gained exactly these events")]
    internal Task ThenTheSessionGained(params object[] events) => ThenEventsOn<Session>(_sessionId, _givenVersion, events);

    // A scheduled end appends more events later, so the spec checks the events that came first.
    [Then("the Session stream gained these events first")]
    internal async Task ThenTheSessionGainedFirst(params object[] events) =>
        (await StreamEventsAsync()).Skip((int)_givenVersion).Take(events.Length).ShouldBe(events);

    [Then("the Session stream eventually has the event")]
    internal Task ThenEventuallyTheSessionHas<TEvent>() =>
        ThenEventually(
            async () => (await StreamEventsAsync()).OfType<TEvent>().Any(),
            $"the Session stream has no {typeof(TEvent).Name}");

    // A spec cannot see a scheduled message that does nothing. It waits until no FinishDrive that is due is left.
    // The specs of other features leave FinishDrive messages for the end of a 25 minute Drive. The due time is on the
    // system clock, see DrivingRules.ScheduleEnds.
    [Then("no scheduled Drive end is due")]
    internal Task ThenEventuallyNoScheduledDriveEndIsDue() =>
        ThenEventually(
            async () =>
            {
                var incoming = await Services.GetRequiredService<IWolverineRuntime>().Storage.Admin.AllIncomingAsync();
                var dueSoon = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1);
                return !incoming.Any(envelope =>
                    envelope.MessageType == typeof(FinishDrive).ToMessageTypeName()
                    && envelope.Status is EnvelopeStatus.Scheduled or EnvelopeStatus.Incoming
                    && (envelope.ScheduledTime ?? DateTimeOffset.MinValue) < dueSoon);
            },
            "a scheduled FinishDrive is still due");

    [Then("the Command is rejected during a Break, with no events")]
    internal async Task ThenRejectedDuringABreak(object command)
    {
        await GivenTheSession([.. AnnAndBobJoined(), .. BobDrives(), .. BobsDriveEndsInABreak()]);

        await WhenPosted(command, As("bob"));

        ThenResponseIs(409);
        ThenRefusedWith("A Break runs.");
        await ThenTheSessionGained();
    }

    private async Task<IReadOnlyList<object>> StreamEventsAsync()
    {
        await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
        var events = await query.Events.FetchStreamAsync(_sessionId);
        return events.Select(static e => e.Data).ToList();
    }

    private object[] AnnAndBobJoined() =>
        [
            new SessionCreated(_sessionId, "Mob on the timer", "tuigether", "main", "ann", ServerFixture.Now),
            new ParticipantJoined(_sessionId, "ann", "Fox", ServerFixture.Now),
            new SessionStarted(_sessionId, "ann", ServerFixture.Now),
            new ParticipantJoined(_sessionId, "bob", "Owl", ServerFixture.Now),
        ];

    // A Drive that started 10 minutes before the server clock and has 15 minutes left.
    private object[] BobDrives() =>
        [
            new DriverChanged(_sessionId, "bob", "ann", ServerFixture.Now - TimeSpan.FromMinutes(10)),
            new DriveStarted(_sessionId, "bob", ServerFixture.Now + TimeSpan.FromMinutes(15), "ann", ServerFixture.Now - TimeSpan.FromMinutes(10)),
        ];

    private object[] BobsDriveEndsInABreak() =>
        [
            new DriveSkipped(_sessionId, "bob", BreakStartedAt),
            new BreakStarted(_sessionId, BreakEndsAt, "bob", BreakStartedAt),
        ];

    private DriverChanged DriverChanged(string driver, string user) => new(_sessionId, driver, user, ServerFixture.Now);

    private static DriveHistoryEntry Entry(DriveHistoryKind kind, string driver, string user, DateTimeOffset at) =>
        new(kind, driver, user, at);

    private SessionDriveHistory History(params DriveHistoryEntry[] entries) => new(_sessionId) { Entries = [.. entries] };
}
