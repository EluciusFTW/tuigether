using Tuigether.Server.Sessions;

namespace Tuigether.Server.Specs;

[BobcatFeature("Creating a Session")]
public sealed class CreatingASession(ServerFixture server) : ServerSpec(server)
{
    [Fact]
    public async Task a_Session_with_a_title_is_created()
    {
        var response = await WhenPosted<CreateSessionResponse>(new CreateSession("Mob on the timer", "tuigether", "main"), As("ann"));

        ThenResponseIs(201);
        var sessionId = TheStartedStream<Session>();
        ThenMatches(response, new CreateSessionResponse(sessionId));
        ThenMatches(sessionId.Version, 7);
        ThenEvents(new SessionCreated(sessionId, "Mob on the timer", "tuigether", "main", "ann", ServerFixture.Now));
        await ThenReadModel<SessionSummary>(
            sessionId,
            new SessionSummary(sessionId, "Mob on the timer", SessionStatus.Created, "ann", "tuigether", "main", ServerFixture.Now));
    }

    [Fact]
    public async Task a_Session_with_a_blank_title_is_rejected()
    {
        await WhenPosted(new CreateSession("   ", "tuigether", "main"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("A Session needs a title.");
        ThenNoEvents();
    }
}
