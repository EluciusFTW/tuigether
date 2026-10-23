using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Tuigether.Server.Sessions;

namespace Tuigether.Server.Specs;

[BobcatFeature("Deleting a Session")]
public sealed class DeletingASession(ServerFixture server) : ServerSpec(server)
{
    [Fact]
    public async Task a_Session_is_deleted_and_its_stream_is_archived()
    {
        var sessionId = Guid.CreateVersion7();
        await GivenEvents<Session>(sessionId, Created(sessionId));

        await WhenPosted(new DeleteSession(sessionId), As("bob"));

        ThenResponseIs(204);
        ThenEvents(new SessionDeleted(sessionId, "bob", ServerFixture.Now), new Archived("Session deleted"));
        await ThenTheSessionStreamIsArchived(sessionId);
        await ThenNoReadModel<SessionSummary>(sessionId);
    }

    [Fact]
    public async Task a_Command_on_a_deleted_Session_is_rejected()
    {
        var sessionId = Guid.CreateVersion7();
        await GivenEvents<Session>(
            sessionId,
            Created(sessionId),
            new SessionDeleted(sessionId, "bob", ServerFixture.Now),
            new Archived("Session deleted"));

        await WhenPosted(new DeleteSession(sessionId), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The Session is deleted.");
        ThenNoEvents();
    }

    [Fact]
    public async Task a_Command_on_an_unknown_Session_is_not_found()
    {
        await WhenPosted(new DeleteSession(Guid.CreateVersion7()), As("ann"));

        ThenResponseIs(404);
        ThenNoEvents();
    }

    [Then("the stream of the Session is archived")]
    internal async Task ThenTheSessionStreamIsArchived(Guid sessionId)
    {
        await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
        var state = await query.Events.FetchStreamStateAsync(sessionId);
        SpecAssert.Check("archived", state?.IsArchived, true);
    }

    private static SessionCreated Created(Guid sessionId) =>
        new(sessionId, "Mob on the timer", "tuigether", "main", "ann", ServerFixture.Now);
}
