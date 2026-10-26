using Alba;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Tuigether.Server.Domain;
using Tuigether.Server.Presence;
using Wolverine.Runtime;

namespace Tuigether.Server.Specs;

[BobcatFeature("Opening a Session")]
public sealed class OpeningASession(ServerFixture server) : ServerSpec(server)
{
    // The leave check runs at the end of the grace period, on the next poll of the scheduled jobs.
    private static readonly TimeSpan AfterTheGracePeriod = ServerFixture.LeaveGracePeriod + TimeSpan.FromSeconds(3);

    // The sweep runs every 5 seconds, on the next poll of the scheduled jobs.
    private static readonly TimeSpan AfterTheNextSweep = TimeSpan.FromSeconds(7);

    private readonly Guid _sessionId = Guid.CreateVersion7();

    [Fact]
    public async Task the_first_join_appends_ParticipantJoined_and_SessionStarted()
    {
        await GivenEvents<Session>(_sessionId, Created());
        var client = await GivenAClient(Fixture.Host, "ann");

        await WhenActing("ann opens the Session", _ => client.SendAsync(new OpenSession(_sessionId, "Fox")));

        ThenTheActSucceeded();
        await ThenTheStreamIs(Created(), Joined("ann", "Fox"), Started("ann"));
        await ThenTheClientGets(client, Data(SessionStatus.Started));
        await ThenTheClientGets(client, Participants(new Participant("ann", "Fox")));
        await ThenReadModel<SessionSummary>(_sessionId, Summary(SessionStatus.Started, "ann"));
    }

    [Fact]
    public async Task a_second_Client_of_the_same_User_appends_no_second_ParticipantJoined()
    {
        await GivenEvents<Session>(_sessionId, Created());
        await GivenAClientWithTheSessionOpen(Fixture.Host, "ann", "Fox");
        var second = await GivenAClient(Fixture.Host, "ann");

        await WhenActing("ann opens the Session in a second Client", _ => second.SendAsync(new OpenSession(_sessionId, "Fox")));

        ThenTheActSucceeded();
        await ThenTheClientGets(second, Participants(new Participant("ann", "Fox")));
        await ThenTheStreamIs(Created(), Joined("ann", "Fox"), Started("ann"));
    }

    [Fact]
    public async Task a_clean_close_appends_ParticipantLeft_after_the_grace_period()
    {
        await GivenEvents<Session>(_sessionId, Created());
        var ann = await GivenAClientWithTheSessionOpen(Fixture.Host, "ann", "Fox");
        await GivenAClientWithTheSessionOpen(Fixture.Host, "bob", "Owl");

        await WhenActing("ann closes the Session", _ => ann.SendAsync(new CloseSession(_sessionId)));

        ThenTheActSucceeded();
        await ThenTheStreamStays(
            ServerFixture.LeaveGracePeriod / 2,
            Created(),
            Joined("ann", "Fox"),
            Started("ann"),
            Joined("bob", "Owl"));
        await ThenTheStreamIs(Created(), Joined("ann", "Fox"), Started("ann"), Joined("bob", "Owl"), Left("ann"));
    }

    [Fact]
    public async Task a_closed_connection_closes_its_Session()
    {
        await GivenEvents<Session>(_sessionId, Created());
        var ann = await GivenAClientWithTheSessionOpen(Fixture.Host, "ann", "Fox");

        await WhenActing("ann's Client closes its connection", _ => ann.DisposeAsync().AsTask());

        ThenTheActSucceeded();
        await ThenTheStreamIs(Created(), Joined("ann", "Fox"), Started("ann"), Left("ann"), Finished("ann"));
    }

    [Fact]
    public async Task a_reopen_within_the_grace_period_appends_no_ParticipantLeft()
    {
        await GivenEvents<Session>(_sessionId, Created());
        var ann = await GivenAClientWithTheSessionOpen(Fixture.Host, "ann", "Fox");

        await WhenActing(
            "ann closes the Session and opens it again",
            async _ =>
            {
                await ann.SendAsync(new CloseSession(_sessionId));
                await ann.SendAsync(new OpenSession(_sessionId, "Fox"));
            });

        ThenTheActSucceeded();
        await ThenTheStreamStays(AfterTheGracePeriod, Created(), Joined("ann", "Fox"), Started("ann"));
    }

    [Fact]
    public async Task a_Finished_Session_stays_Finished_on_a_join()
    {
        await GivenEvents<Session>(_sessionId, Created(), Joined("ann", "Fox"), Started("ann"), Left("ann"), Finished("ann"));
        var bob = await GivenAClient(Fixture.Host, "bob");

        await WhenActing("bob opens the Session", _ => bob.SendAsync(new OpenSession(_sessionId, "Owl")));

        ThenTheActSucceeded();
        await ThenTheClientGets(bob, Participants(new Participant("bob", "Owl")));
        await ThenTheClientGets(bob, Data(SessionStatus.Finished));
        await ThenTheStreamIs(
            Created(),
            Joined("ann", "Fox"),
            Started("ann"),
            Left("ann"),
            Finished("ann"),
            Joined("bob", "Owl"));
    }

    [Fact]
    public async Task the_last_two_Participants_who_leave_at_the_same_moment_give_one_SessionFinished()
    {
        await GivenEvents<Session>(_sessionId, Created());
        var ann = await GivenAClientWithTheSessionOpen(Fixture.Host, "ann", "Fox");
        var bob = await GivenAClientWithTheSessionOpen(Fixture.Host, "bob", "Owl");

        await WhenActing(
            "ann and bob close the Session at the same moment",
            _ => Task.WhenAll(ann.SendAsync(new CloseSession(_sessionId)), bob.SendAsync(new CloseSession(_sessionId))));

        ThenTheActSucceeded();
        await ThenTheStreamStays(
            AfterTheGracePeriod,
            static events => events.OfType<ParticipantLeft>().Count() == 2,
            static events => events.OfType<SessionFinished>().Count() == 1 && events[^1] is SessionFinished,
            "both Participants left, and the stream ends with exactly one SessionFinished");
    }

    [Fact]
    public async Task a_Participant_who_joins_on_another_node_shows_at_a_Client_of_this_node()
    {
        await GivenEvents<Session>(_sessionId, Created());
        var otherNode = await StartNodeAsync();
        var ann = await GivenAClientWithTheSessionOpen(Fixture.Host, "ann", "Fox");
        var bob = await GivenAClient(otherNode, "bob");

        await WhenActing("bob opens the Session on the other node", _ => bob.SendAsync(new OpenSession(_sessionId, "Owl")));

        ThenTheActSucceeded();
        await ThenTheClientGets(ann, Participants(new Participant("ann", "Fox"), new Participant("bob", "Owl")));
    }

    [Fact]
    public async Task the_sweep_removes_the_Participants_of_a_crashed_node()
    {
        await GivenEvents<Session>(_sessionId, Created(), Joined("ann", "Fox"), Started("ann"));
        var crashedNode = Guid.CreateVersion7();
        await GivenANodeWithOpenClients(crashedNode, "ann");

        await WhenActing("the node of ann's Client crashes", _ => CrashAsync(crashedNode));

        ThenTheActSucceeded();
        await ThenTheStreamIs(Created(), Joined("ann", "Fox"), Started("ann"), Left("ann"), Finished("ann"));
        await ThenTheNodeLeftNoDocuments(crashedNode);
    }

    [Fact]
    public async Task the_sweep_keeps_the_Clients_of_a_live_node()
    {
        await GivenEvents<Session>(_sessionId, Created(), Joined("bob", "Owl"), Started("bob"));
        var crashedNode = Guid.CreateVersion7();
        await GivenANodeWithOpenClients(crashedNode, "bob");
        await GivenAClientWithTheSessionOpen(Fixture.Host, "ann", "Fox");
        await GivenTheNodeHasAHeartbeat(Fixture.Host);

        await WhenActing("the node of bob's Client crashes", _ => CrashAsync(crashedNode));

        ThenTheActSucceeded();
        await ThenTheStreamStays(
            AfterTheNextSweep,
            Created(),
            Joined("bob", "Owl"),
            Started("bob"),
            Joined("ann", "Fox"),
            Left("bob"));
    }

    [Fact]
    public async Task two_live_nodes_give_one_ParticipantLeft_per_User()
    {
        await GivenEvents<Session>(_sessionId, Created(), Joined("ann", "Fox"), Started("ann"), Joined("bob", "Owl"));
        await StartNodeAsync();
        var crashedNode = Guid.CreateVersion7();
        await GivenANodeWithOpenClients(crashedNode, "ann", "ann", "bob");

        await WhenActing("the node of the Clients crashes", _ => CrashAsync(crashedNode));

        ThenTheActSucceeded();
        await ThenTheStreamStays(
            AfterTheNextSweep,
            static events => events.OfType<ParticipantLeft>().Count() == 2,
            static events =>
                events.OfType<ParticipantLeft>().Select(static left => left.User).Order().SequenceEqual(["ann", "bob"])
                && events.OfType<SessionFinished>().Count() == 1,
            "ann and bob left once each, and the Session finished once");
    }

    [Given("a Client of the User is connected to the node")]
    internal async Task<HubClient> GivenAClient(IAlbaHost node, string user) =>
        Own(await HubClient.ConnectAsync(node, ServerFixture.ApiKey, CurrentProtocol, user));

    [Given("a Client of the User has the Session open")]
    internal async Task<HubClient> GivenAClientWithTheSessionOpen(IAlbaHost node, string user, string avatar)
    {
        var client = await GivenAClient(node, user);
        await client.SendAsync(new OpenSession(_sessionId, avatar));

        // The node pushes the Participants after the join commits, so the spec waits for the User in them.
        await ThenEventuallyPushed(
            () => client.Received<SessionParticipants>().Any(participants => participants.Participants.Any(p => p.User == user)),
            $"the Client of {user} got no Participants with {user}");
        return client;
    }

    /// <summary>
    /// A node with a fresh heartbeat holds one open Client of the Session per entry in <paramref name="users"/>. The
    /// node exists only as documents, so it closes none of its Clients.
    /// </summary>
    [Given("a node has Clients of the Users with the Session open")]
    internal async Task GivenANodeWithOpenClients(Guid node, params string[] users)
    {
        await using var documents = Services.GetRequiredService<IDocumentStore>().LightweightSession();
        documents.Store(new NodeHeartbeat(node, ServerFixture.Now));
        documents.Store([.. users.Select((user, index) => new ConnectedClient($"{node}-{index}", node, user, _sessionId))]);
        await documents.SaveChangesAsync();
    }

    // Every spec starts on an empty database, so a live node has no heartbeat until its next one.
    [Given("the node has a heartbeat")]
    internal Task GivenTheNodeHasAHeartbeat(IAlbaHost node) =>
        ThenEventually(
            async () =>
            {
                await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
                return await query.LoadAsync<NodeHeartbeat>(node.Services.GetRequiredService<IWolverineRuntime>().Options.UniqueNodeId) is not null;
            },
            "the node wrote no heartbeat");

    /// <summary>
    /// The spec clock stands still, so the crash sets the last heartbeat of the node to a time beyond the timeout.
    /// </summary>
    private async Task CrashAsync(Guid node)
    {
        await using var documents = Services.GetRequiredService<IDocumentStore>().LightweightSession();
        documents.Store(new NodeHeartbeat(node, ServerFixture.Now - ServerFixture.HeartbeatTimeout - TimeSpan.FromSeconds(1)));
        await documents.SaveChangesAsync();
    }

    [Then("the database holds no Client and no heartbeat of the node")]
    internal async Task ThenTheNodeLeftNoDocuments(Guid node)
    {
        await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
        SpecAssert.Check("Clients of the node", await query.Query<ConnectedClient>().CountAsync(client => client.NodeId == node), 0);
        SpecAssert.Check("heartbeat of the node", await query.LoadAsync<NodeHeartbeat>(node), null);
    }

    [Then("the Client gets the snapshot")]
    internal Task ThenTheClientGets<T>(HubClient client, T expected) =>
        ThenEventuallyPushed(() => client.Received<T>().Contains(expected), $"the Client did not get {expected}");

    [Then("the stream of the Session holds exactly these events")]
    internal Task ThenTheStreamIs(params object[] expected) =>
        ThenEventually(
            events => events.SequenceEqual(expected),
            $"the stream does not hold exactly: {string.Join(", ", expected)}");

    [Then("the stream of the Session still holds exactly these events after a time")]
    internal Task ThenTheStreamStays(TimeSpan wait, params object[] expected) =>
        ThenTheStreamStays(
            wait,
            events => events.SequenceEqual(expected),
            events => events.SequenceEqual(expected),
            $"the stream does not hold exactly: {string.Join(", ", expected)}");

    /// <summary>
    /// Waits until the stream matches <paramref name="arrived"/>, waits <paramref name="wait"/> more, and checks
    /// <paramref name="stays"/>.
    /// </summary>
    private async Task ThenTheStreamStays(
        TimeSpan wait,
        Func<IReadOnlyList<object>, bool> arrived,
        Func<IReadOnlyList<object>, bool> stays,
        string because)
    {
        await ThenEventually(arrived, because);
        await Task.Delay(wait);
        var events = await StreamEventsAsync();
        SpecAssert.Fact(stays(events), $"{because}. The stream holds: {string.Join(", ", events)}");
    }

    private Task ThenEventually(Func<IReadOnlyList<object>, bool> arrived, string because) =>
        ThenEventually(async () => arrived(await StreamEventsAsync()), because);

    private async Task<IReadOnlyList<object>> StreamEventsAsync()
    {
        await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
        var events = await query.Events.FetchStreamAsync(_sessionId);
        return events.Select(static e => e.Data).ToList();
    }

    private SessionCreated Created() => new(_sessionId, "Mob on the timer", "tuigether", "main", "ann", ServerFixture.Now);

    private ParticipantJoined Joined(string user, string avatar) => new(_sessionId, user, avatar, ServerFixture.Now);

    private ParticipantLeft Left(string user) => new(_sessionId, user, ServerFixture.Now);

    private SessionStarted Started(string user) => new(_sessionId, user, ServerFixture.Now);

    private SessionFinished Finished(string user) => new(_sessionId, user, ServerFixture.Now);

    private SessionData Data(SessionStatus status) =>
        new(_sessionId, "Mob on the timer", status, "ann", "tuigether", "main", ServerFixture.Now, Driver: null);

    private SessionParticipants Participants(params Participant[] participants) =>
        new(_sessionId) { Participants = [.. participants] };

    private SessionSummary Summary(SessionStatus status, params string[] participants) =>
        new(_sessionId, "Mob on the timer", status, "ann", "tuigether", "main", ServerFixture.Now) { Participants = [.. participants] };
}
