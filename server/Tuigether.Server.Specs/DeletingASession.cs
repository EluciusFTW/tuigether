using Alba;
using JasperFx.Events;
using Marten;
using Marten.Events.Archiving;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Tuigether.Server.Sessions;

namespace Tuigether.Server.Specs;

[BobcatFeature("Deleting a Session")]
public sealed class DeletingASession(ServerFixture server) : ServerSpec(server)
{
    private readonly Guid _sessionId = Guid.CreateVersion7();
    private long _givenVersion;

    [Fact]
    public async Task a_Session_is_deleted_and_its_stream_is_archived()
    {
        await GivenTheSession([Created()]);

        await WhenPosted(new DeleteSession(_sessionId), As("bob"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new SessionDeleted(_sessionId, "bob", ServerFixture.Now), new Archived("Session deleted"));
        await ThenTheSessionStreamIsArchived();
        await ThenNoReadModel<SessionSummary>(_sessionId);
    }

    [Fact]
    public async Task a_Command_on_a_deleted_Session_is_rejected()
    {
        await GivenTheSession([Created(), new SessionDeleted(_sessionId, "bob", ServerFixture.Now), new Archived("Session deleted")]);

        await WhenPosted(new DeleteSession(_sessionId), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The Session is deleted.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_Command_on_an_unknown_Session_is_not_found()
    {
        await WhenPosted(new DeleteSession(Guid.CreateVersion7()), As("ann"));

        ThenResponseIs(404);
        ThenRefusedWith("The Session does not exist.");
        await ThenTheDatabaseHoldsNoEvents();
    }

    [Fact]
    public async Task a_delete_during_a_rotation_ends_the_Session()
    {
        await GivenTheSession(
            [
                Created(),
                new ParticipantJoined(_sessionId, "ann", "Fox", ServerFixture.Now),
                new SessionStarted(_sessionId, "ann", ServerFixture.Now),
            ]);
        IScenarioResult[] responses = [];

        await WhenActing(
            "bob deletes the Session while ann rotates the Driver",
            async _ => responses = await Task.WhenAll(
                PostAsync(new DeleteSession(_sessionId), "bob"),
                PostAsync(new RotateDriver(_sessionId, null, "ann", TimeSpan.FromMinutes(25)), "ann")));

        ThenTheActSucceeded();
        var (delete, rotate) = (responses[0], responses[1]);
        delete.Context.Response.StatusCode.ShouldBe(204);
        var rotation = rotate.Context.Response.StatusCode switch
        {
            409 => $"409 {(await rotate.ReadAsJsonAsync<ProblemDetails>()).ShouldNotBeNull().Detail}",
            var status => $"{status}",
        };
        rotation.ShouldBeOneOf("204", "409 The Session is deleted.");

        var events = await StreamEventsAsync();
        events.TakeLast(2).Select(static e => e.Data).ToList().ShouldBeEquivalentTo(
            new List<object> { new SessionDeleted(_sessionId, "bob", ServerFixture.Now), new Archived("Session deleted") });
    }

    [Given("the Session has these events")]
    internal async Task GivenTheSession(object[] events)
    {
        await GivenEvents<Session>(_sessionId, events);
        _givenVersion = events.Length;
    }

    // See "Bobcat and an invoked handler" in docs/agents/csharp.md.
    [Then("the Session stream gained exactly these events")]
    internal async Task ThenTheSessionGained(params object[] events)
    {
        var gained = (await StreamEventsAsync()).Where(e => e.Version > _givenVersion).Select(static e => e.Data).ToList();
        gained.ShouldBeEquivalentTo(events.ToList());
    }

    [Then("the stream of the Session is archived")]
    internal async Task ThenTheSessionStreamIsArchived()
    {
        await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
        var state = await query.Events.FetchStreamStateAsync(_sessionId);
        SpecAssert.Check("archived", state?.IsArchived, true);
    }

    private async Task<IReadOnlyList<IEvent>> StreamEventsAsync()
    {
        await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
        return await query.Events.QueryAllRawEvents()
            .Where(e => e.StreamId == _sessionId && e.MaybeArchived())
            .OrderBy(static e => e.Version)
            .ToListAsync();
    }

    private SessionCreated Created() => new(_sessionId, "Mob on the timer", "tuigether", "main", "ann", ServerFixture.Now);
}
