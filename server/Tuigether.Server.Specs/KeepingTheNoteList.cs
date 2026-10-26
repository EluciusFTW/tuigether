using JasperFx.Events;
using Tuigether.Server.Domain;

namespace Tuigether.Server.Specs;

[BobcatFeature("Keeping the note list")]
public sealed class KeepingTheNoteList(ServerFixture server) : ServerSpec(server)
{
    private readonly Guid _sessionId = Guid.CreateVersion7();
    private long _givenVersion;

    [Fact]
    public async Task an_add_with_a_new_id_appends_the_note()
    {
        await GivenTheSession([Created(), Added("n1", "Port 5101")]);

        await WhenPosted(new AddNote(_sessionId, "n2", "Port 5102"), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(Added("n2", "Port 5102"));
        await ThenReadModel<SessionNoteList>(
            _sessionId,
            new SessionNoteList(_sessionId) { Items = [new("n1", "Port 5101"), new("n2", "Port 5102")] });
    }

    [Fact]
    public async Task an_add_with_an_existing_id_is_rejected()
    {
        await GivenTheSession([Created(), Added("n1", "Port 5101")]);

        await WhenPosted(new AddNote(_sessionId, "n1", "Port 5102"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("A note with this id exists.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_change_of_a_note_stores_the_text()
    {
        await GivenTheSession([Created(), Added("n1", "Port 5101"), Added("n2", "Port 5102")]);

        await WhenPosted(new ChangeNote(_sessionId, "n1", "Port 5111"), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new NoteChanged(_sessionId, "n1", "Port 5111", "ann", ServerFixture.Now));
        await ThenReadModel<SessionNoteList>(
            _sessionId,
            new SessionNoteList(_sessionId) { Items = [new("n1", "Port 5111"), new("n2", "Port 5102")] });
    }

    [Fact]
    public async Task a_change_of_a_missing_note_is_rejected()
    {
        await GivenTheSession([Created(), Added("n1", "Port 5101")]);

        await WhenPosted(new ChangeNote(_sessionId, "n2", "Port 5102"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The note does not exist.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_remove_takes_the_note_out_of_the_list()
    {
        await GivenTheSession([Created(), Added("n1", "Port 5101"), Added("n2", "Port 5102")]);

        await WhenPosted(new RemoveNote(_sessionId, "n1"), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new NoteRemoved(_sessionId, "n1", "ann", ServerFixture.Now));
        await ThenReadModel<SessionNoteList>(_sessionId, new SessionNoteList(_sessionId) { Items = [new("n2", "Port 5102")] });
    }

    [Fact]
    public async Task a_remove_of_a_missing_note_is_rejected()
    {
        await GivenTheSession([Created(), Added("n1", "Port 5101"), Removed("n1")]);

        await WhenPosted(new RemoveNote(_sessionId, "n1"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The note does not exist.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task an_add_after_a_remove_appends_the_note_at_the_end()
    {
        await GivenTheSession([Created(), Added("n1", "One"), Added("n2", "Two"), Removed("n1")]);

        await WhenPosted(new AddNote(_sessionId, "n3", "Three"), As("ann"));

        ThenResponseIs(204);
        await ThenReadModel<SessionNoteList>(
            _sessionId,
            new SessionNoteList(_sessionId) { Items = [new("n2", "Two"), new("n3", "Three")] });
    }

    [Fact]
    public async Task a_Command_on_a_deleted_Session_is_rejected()
    {
        await GivenTheSession([Created(), new SessionDeleted(_sessionId, "ann", ServerFixture.Now), new Archived("Session deleted")]);

        await WhenPosted(new AddNote(_sessionId, "n1", "One"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The Session is deleted.");
        await ThenTheDatabaseHoldsOnlyTheGivenEvents(_givenVersion);
    }

    [Fact]
    public async Task a_Client_that_opens_the_Session_gets_the_note_list()
    {
        await GivenEvents<Session>(_sessionId, [Created(), Added("n1", "One"), Added("n2", "Two")]);

        var bob = Own(await HubClient.ConnectAsync(Fixture.Host, ServerFixture.ApiKey, CurrentProtocol, "bob"));
        await WhenActing("bob opens the Session", _ => bob.SendAsync(new OpenSession(_sessionId, "Owl")));

        ThenTheActSucceeded();
        await ThenEventuallyPushed(
            () => bob.Received<SessionNoteList>().Contains(
                new SessionNoteList(_sessionId) { Items = [new("n1", "One"), new("n2", "Two")] }),
            "bob's Client did not get the note list");
    }

    [Fact]
    public async Task a_Client_with_the_Session_open_gets_each_change()
    {
        await GivenEvents<Session>(_sessionId, [Created(), Added("n1", "One")]);
        var bob = Own(await HubClient.ConnectAsync(Fixture.Host, ServerFixture.ApiKey, CurrentProtocol, "bob"));
        await bob.SendAsync(new OpenSession(_sessionId, "Owl"));

        await WhenActing("ann adds a note", _ => PostAsync(new AddNote(_sessionId, "n2", "Two"), "ann"));

        ThenTheActSucceeded();
        await ThenEventuallyPushed(
            () => bob.Received<SessionNoteList>().Contains(
                new SessionNoteList(_sessionId) { Items = [new("n1", "One"), new("n2", "Two")] }),
            "bob's Client did not get the added note");
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

    private SessionCreated Created() => new(_sessionId, "Mob on the note list", "tuigether", "main", "ann", ServerFixture.Now);

    private NoteAdded Added(string noteId, string text) => new(_sessionId, noteId, text, "ann", ServerFixture.Now);

    private NoteRemoved Removed(string noteId) => new(_sessionId, noteId, "ann", ServerFixture.Now);
}
