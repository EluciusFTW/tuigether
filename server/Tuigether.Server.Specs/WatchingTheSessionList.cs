using Alba;
using Tuigether.Server.Sessions;

namespace Tuigether.Server.Specs;

[BobcatFeature("Watching the Session list")]
public sealed class WatchingTheSessionList(ServerFixture server) : ServerSpec(server)
{
    [Fact]
    public async Task a_Client_that_joins_gets_all_current_Sessions()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await GivenEvents<Session>(first, Created(first, "Mob on the timer"));
        await GivenEvents<Session>(second, Created(second, "Pairing on the parser"));
        var client = await GivenAClient(Fixture.Host);

        await WhenActing("the Client joins the Session list", _ => client.SendAsync(new JoinSessionList()));

        ThenTheActSucceeded();
        await ThenTheClientGetsTheSessionList(client, Summary(first, "Mob on the timer"), Summary(second, "Pairing on the parser"));
    }

    [Fact]
    public async Task a_created_Session_reaches_a_Client_on_another_node()
    {
        var otherNode = await GivenAnotherNode();
        var clientOnThisNode = await GivenAClientOnTheSessionList(Fixture.Host);
        var clientOnOtherNode = await GivenAClientOnTheSessionList(otherNode);

        await WhenPosted(new CreateSession("Mob on the timer", "tuigether", "main"), As("ann"));

        ThenResponseIs(201);
        var summary = Summary(TheStartedStream<Session>(), "Mob on the timer");
        await ThenTheClientGets(clientOnThisNode, summary);
        await ThenTheClientGets(clientOnOtherNode, summary);
    }

    [Fact]
    public async Task a_deleted_Session_leaves_the_Session_list()
    {
        var sessionId = Guid.CreateVersion7();
        await GivenEvents<Session>(sessionId, Created(sessionId, "Mob on the timer"));
        var client = await GivenAClientOnTheSessionList(Fixture.Host);

        await WhenPosted(new DeleteSession(sessionId), As("bob"));

        ThenResponseIs(204);
        await ThenTheClientGets(client, new SessionRemoved(sessionId));
    }

    [Given("another node runs on the same database")]
    internal Task<IAlbaHost> GivenAnotherNode() => StartNodeAsync();

    [Given("a Client is connected to the node")]
    internal async Task<HubClient> GivenAClient(IAlbaHost node) =>
        Own(await HubClient.ConnectAsync(node, ServerFixture.ApiKey, CurrentProtocol, "ann"));

    [Given("a Client has joined the Session list of the node")]
    internal async Task<HubClient> GivenAClientOnTheSessionList(IAlbaHost node)
    {
        var client = await GivenAClient(node);
        await client.SendAsync(new JoinSessionList());

        // The node pushes changes only after the join, so the spec waits for the answer to the join.
        await ThenEventuallyPushed(() => client.Received<SessionList>().Count > 0, "the Client got no Session list");
        return client;
    }

    [Then("the Client gets the Session list")]
    internal Task ThenTheClientGetsTheSessionList(HubClient client, params SessionSummary[] expected)
    {
        var sorted = expected.OrderBy(static summary => summary.Id).ToList();
        return ThenEventuallyPushed(
            () => client.Received<SessionList>().Any(list => list.Sessions.OrderBy(static summary => summary.Id).SequenceEqual(sorted)),
            $"the Client got no Session list with exactly: {string.Join(", ", sorted)}");
    }

    [Then("the Client gets the message")]
    internal Task ThenTheClientGets<T>(HubClient client, T expected) =>
        ThenEventuallyPushed(() => client.Received<T>().Contains(expected), $"the Client did not get {expected}");

    private static SessionCreated Created(Guid sessionId, string title) =>
        new(sessionId, title, "tuigether", "main", "ann", ServerFixture.Now);

    private static SessionSummary Summary(Guid sessionId, string title) =>
        new(sessionId, title, SessionStatus.Created, "ann", "tuigether", "main", ServerFixture.Now);
}
