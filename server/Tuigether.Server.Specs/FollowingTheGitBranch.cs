using JasperFx.Events;
using Tuigether.Server.Domain;

namespace Tuigether.Server.Specs;

[BobcatFeature("Following the git branch")]
public sealed class FollowingTheGitBranch(ServerFixture server) : ServerSpec(server)
{
    private readonly Guid _sessionId = Guid.CreateVersion7();
    private long _givenVersion;

    [Fact]
    public async Task a_branch_change_shows_in_the_Session_data()
    {
        await GivenTheSession([Created()]);

        await WhenPosted(new ChangeSessionBranch(_sessionId, "feature/EWK-14-git"), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new SessionBranchChanged(_sessionId, "feature/EWK-14-git", "ann", ServerFixture.Now));
        await ThenReadModel<SessionData>(_sessionId, Data() with { Branch = "feature/EWK-14-git" });
        await ThenReadModel<SessionSummary>(_sessionId, Summary() with { Branch = "feature/EWK-14-git" });
    }

    [Fact]
    public async Task a_WIP_push_shows_in_the_Session_data()
    {
        await GivenTheSession([Created(), new WipPushed(_sessionId, "ann", ServerFixture.Now.AddMinutes(-5))]);

        await WhenPosted(new RecordWipPush(_sessionId), As("bob"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new WipPushed(_sessionId, "bob", ServerFixture.Now));
        await ThenReadModel<SessionData>(_sessionId, Data() with { LastWipPushBy = "bob", LastWipPushAt = ServerFixture.Now });
        await ThenReadModel<SessionSummary>(_sessionId, Summary() with { LastWipPushBy = "bob", LastWipPushAt = ServerFixture.Now });
    }

    [Fact]
    public async Task a_WIP_push_on_a_deleted_Session_is_rejected()
    {
        await GivenTheSession([Created(), new SessionDeleted(_sessionId, "ann", ServerFixture.Now), new Archived("Session deleted")]);

        await WhenPosted(new RecordWipPush(_sessionId), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The Session is deleted.");
    }

    [Fact]
    public async Task a_Client_with_the_Session_open_gets_the_WIP_push()
    {
        await GivenEvents<Session>(_sessionId, [Created()]);
        var bob = Own(await HubClient.ConnectAsync(Fixture.Host, ServerFixture.ApiKey, CurrentProtocol, "bob"));
        await bob.SendAsync(new OpenSession(_sessionId, "Owl"));

        await WhenActing("ann records a WIP push", _ => PostAsync(new RecordWipPush(_sessionId), "ann"));

        ThenTheActSucceeded();
        await ThenEventuallyPushed(
            () => bob.Received<SessionData>().Any(static data => data.LastWipPushBy == "ann"),
            "bob's Client did not get the WIP push");
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

    private SessionCreated Created() => new(_sessionId, "Mob on git", "tuigether", "main", "ann", ServerFixture.Now);

    private SessionData Data() => new(_sessionId, "Mob on git", SessionStatus.Created, "ann", "tuigether", "main", ServerFixture.Now, Driver: null);

    private SessionSummary Summary() => new(_sessionId, "Mob on git", SessionStatus.Created, "ann", "tuigether", "main", ServerFixture.Now);
}
