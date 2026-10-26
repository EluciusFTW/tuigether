using Marten;
using Microsoft.Extensions.DependencyInjection;
using Tuigether.Server.Domain;

namespace Tuigether.Server.Specs;

[BobcatFeature("Editing the Notes")]
public sealed class EditingTheNotes(ServerFixture server) : ServerSpec(server)
{
    private readonly Guid _sessionId = Guid.CreateVersion7();
    private long _givenVersion;

    [Fact]
    public async Task an_acquire_of_the_free_Edit_lock_gives_the_caller_the_Edit_lock()
    {
        await GivenTheSession(AnnAndBobJoined());

        await WhenPosted(new AcquireNotesEditLock(_sessionId), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new NotesEditingStarted(_sessionId, "ann", ServerFixture.Now));
        await ThenReadModel<SessionNotes>(_sessionId, new SessionNotes(_sessionId, "", "ann"));
    }

    [Fact]
    public async Task an_acquire_while_another_Participant_holds_the_Edit_lock_is_rejected()
    {
        await GivenTheSession([.. AnnAndBobJoined(), Started("bob")]);

        await WhenPosted(new AcquireNotesEditLock(_sessionId), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("bob edits the Notes.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task an_acquire_by_the_holder_of_the_Edit_lock_appends_nothing()
    {
        await GivenTheSession([.. AnnAndBobJoined(), Started("ann")]);

        await WhenPosted(new AcquireNotesEditLock(_sessionId), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task an_acquire_by_a_User_who_is_no_Participant_is_rejected()
    {
        await GivenTheSession(AnnAndBobJoined());

        await WhenPosted(new AcquireNotesEditLock(_sessionId), As("cid"));

        ThenResponseIs(409);
        ThenRefusedWith("cid is not a Participant of the Session.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task two_concurrent_acquires_give_one_acceptance_and_one_rejection()
    {
        await GivenTheSession(AnnAndBobJoined());
        var statuses = new List<int>();

        await WhenActing(
            "ann and bob acquire the Edit lock at the same moment",
            async _ =>
            {
                var responses = await Task.WhenAll(
                    PostAsync(new AcquireNotesEditLock(_sessionId), "ann"),
                    PostAsync(new AcquireNotesEditLock(_sessionId), "bob"));
                statuses.AddRange(responses.Select(static response => response.Context.Response.StatusCode).Order());
            });

        ThenTheActSucceeded();
        statuses.ShouldBe([204, 409]);
        (await StreamEventsAsync()).OfType<NotesEditingStarted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task an_end_with_a_changed_text_stores_the_text_and_frees_the_Edit_lock()
    {
        await GivenTheSession([.. AnnAndBobJoined(), Started("ann")]);

        await WhenPosted(new EndNotesEditing(_sessionId, "Try the outbox"), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(
            new NotesEdited(_sessionId, "Try the outbox", "ann", ServerFixture.Now),
            new NotesEditingEnded(_sessionId, "ann", ServerFixture.Now));
        await ThenReadModel<SessionNotes>(_sessionId, new SessionNotes(_sessionId, "Try the outbox", null));
    }

    [Fact]
    public async Task an_end_with_the_same_text_only_frees_the_Edit_lock()
    {
        await GivenTheSession(
            [
                .. AnnAndBobJoined(),
                Started("ann"),
                new NotesEdited(_sessionId, "Try the outbox", "ann", ServerFixture.Now),
                new NotesEditingEnded(_sessionId, "ann", ServerFixture.Now),
                Started("ann"),
            ]);

        await WhenPosted(new EndNotesEditing(_sessionId, "Try the outbox"), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new NotesEditingEnded(_sessionId, "ann", ServerFixture.Now));
        await ThenReadModel<SessionNotes>(_sessionId, new SessionNotes(_sessionId, "Try the outbox", null));
    }

    [Fact]
    public async Task an_end_by_a_Participant_who_does_not_hold_the_Edit_lock_is_rejected()
    {
        await GivenTheSession([.. AnnAndBobJoined(), Started("bob")]);

        await WhenPosted(new EndNotesEditing(_sessionId, "Try the outbox"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("ann does not hold the Edit lock of the Notes.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task the_holder_leaves_and_the_Edit_lock_is_free()
    {
        await GivenEvents<Session>(_sessionId, Created());
        var ann = await GivenAClientWithTheSessionOpen("ann", "Fox");
        var bob = await GivenAClientWithTheSessionOpen("bob", "Owl");
        await GivenPosted(new AcquireNotesEditLock(_sessionId), "ann");
        await ThenEventuallyPushed(
            () => bob.Received<SessionNotes>().Contains(new(_sessionId, "", "ann")),
            "bob's Client did not get the Notes with the Edit lock of ann");

        await WhenActing("the holder ann closes the Session", _ => ann.SendAsync(new CloseSession(_sessionId)));

        ThenTheActSucceeded();
        await ThenEventuallyPushed(
            () => bob.Received<SessionNotes>().LastOrDefault() == new SessionNotes(_sessionId, "", null),
            "bob's Client did not get the Notes with a free Edit lock");
        (await StreamEventsAsync()).SkipWhile(static e => e is not ParticipantLeft).ShouldBe(
            [
                new ParticipantLeft(_sessionId, "ann", ServerFixture.Now),
                new NotesEditingEnded(_sessionId, "ann", ServerFixture.Now),
            ]);
    }

    [Fact]
    public async Task a_Client_that_opens_the_Session_gets_the_Notes()
    {
        await GivenEvents<Session>(
            _sessionId,
            [
                .. AnnAndBobJoined(),
                Started("ann"),
                new NotesEdited(_sessionId, "Try the outbox", "ann", ServerFixture.Now),
            ]);

        var bob = await GivenAClientWithTheSessionOpen("bob", "Owl");

        await ThenEventuallyPushed(
            () => bob.Received<SessionNotes>().Contains(new(_sessionId, "Try the outbox", "ann")),
            "bob's Client did not get the Notes");
    }

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

    [Given("the User posts the Command")]
    internal async Task GivenPosted(object command, string user)
    {
        var response = await PostAsync(command, user);
        response.Context.Response.StatusCode.ShouldBe(204);
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

    private async Task<IReadOnlyList<object>> StreamEventsAsync()
    {
        await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
        var events = await query.Events.FetchStreamAsync(_sessionId);
        return events.Select(static e => e.Data).ToList();
    }

    private NotesEditingStarted Started(string user) => new(_sessionId, user, ServerFixture.Now);

    private SessionCreated Created() => new(_sessionId, "Mob on the Notes", "tuigether", "main", "ann", ServerFixture.Now);

    private object[] AnnAndBobJoined() =>
        [
            Created(),
            new ParticipantJoined(_sessionId, "ann", "Fox", ServerFixture.Now),
            new SessionStarted(_sessionId, "ann", ServerFixture.Now),
            new ParticipantJoined(_sessionId, "bob", "Owl", ServerFixture.Now),
        ];
}
