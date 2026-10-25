using JasperFx.Events;
using Tuigether.Server.Domain;

namespace Tuigether.Server.Specs;

[BobcatFeature("Keeping the todo list")]
public sealed class KeepingTheTodoList(ServerFixture server) : ServerSpec(server)
{
    private readonly Guid _sessionId = Guid.CreateVersion7();
    private long _givenVersion;

    [Fact]
    public async Task an_add_with_a_new_id_appends_the_todo()
    {
        await GivenTheSession([Created(), Added("t1", "Write the spec")]);

        await WhenPosted(new AddTodo(_sessionId, "t2", "Ship it"), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(Added("t2", "Ship it"));
        await ThenReadModel<SessionTodos>(
            _sessionId,
            new SessionTodos(_sessionId) { Items = [new("t1", "Write the spec", false), new("t2", "Ship it", false)] });
    }

    [Fact]
    public async Task an_add_with_an_existing_id_is_rejected()
    {
        await GivenTheSession([Created(), Added("t1", "Write the spec")]);

        await WhenPosted(new AddTodo(_sessionId, "t1", "Ship it"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("A todo with this id exists.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_text_change_of_a_todo_stores_the_text()
    {
        await GivenTheSession([Created(), Added("t1", "Write the spec")]);

        await WhenPosted(new ChangeTodoText(_sessionId, "t1", "Write the specs"), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new TodoTextChanged(_sessionId, "t1", "Write the specs", "ann", ServerFixture.Now));
        await ThenReadModel<SessionTodos>(
            _sessionId,
            new SessionTodos(_sessionId) { Items = [new("t1", "Write the specs", false)] });
    }

    [Fact]
    public async Task a_text_change_of_a_missing_todo_is_rejected()
    {
        await GivenTheSession([Created(), Added("t1", "Write the spec")]);

        await WhenPosted(new ChangeTodoText(_sessionId, "t2", "Ship it"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The todo does not exist.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_complete_of_an_open_todo_completes_it()
    {
        await GivenTheSession([Created(), Added("t1", "Write the spec")]);

        await WhenPosted(new CompleteTodo(_sessionId, "t1"), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new TodoCompleted(_sessionId, "t1", "ann", ServerFixture.Now));
        await ThenReadModel<SessionTodos>(
            _sessionId,
            new SessionTodos(_sessionId) { Items = [new("t1", "Write the spec", true)] });
    }

    [Fact]
    public async Task a_complete_of_a_completed_todo_is_rejected()
    {
        await GivenTheSession([Created(), Added("t1", "Write the spec"), Completed("t1")]);

        await WhenPosted(new CompleteTodo(_sessionId, "t1"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The todo is completed already.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_complete_of_a_missing_todo_is_rejected()
    {
        await GivenTheSession([Created()]);

        await WhenPosted(new CompleteTodo(_sessionId, "t1"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The todo does not exist.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_reopen_of_a_completed_todo_reopens_it()
    {
        await GivenTheSession([Created(), Added("t1", "Write the spec"), Completed("t1")]);

        await WhenPosted(new ReopenTodo(_sessionId, "t1"), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new TodoReopened(_sessionId, "t1", "ann", ServerFixture.Now));
        await ThenReadModel<SessionTodos>(
            _sessionId,
            new SessionTodos(_sessionId) { Items = [new("t1", "Write the spec", false)] });
    }

    [Fact]
    public async Task a_reopen_of_an_open_todo_is_rejected()
    {
        await GivenTheSession([Created(), Added("t1", "Write the spec")]);

        await WhenPosted(new ReopenTodo(_sessionId, "t1"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The todo is open already.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_move_changes_the_order_of_the_todos()
    {
        await GivenTheSession([Created(), Added("t1", "One"), Added("t2", "Two"), Added("t3", "Three")]);

        await WhenPosted(new MoveTodo(_sessionId, "t1", 2), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new TodoMoved(_sessionId, "t1", 2, "ann", ServerFixture.Now));
        await ThenReadModel<SessionTodos>(
            _sessionId,
            new SessionTodos(_sessionId) { Items = [new("t2", "Two", false), new("t3", "Three", false), new("t1", "One", false)] });
    }

    [Fact]
    public async Task a_move_up_changes_the_order_of_the_todos()
    {
        await GivenTheSession([Created(), Added("t1", "One"), Added("t2", "Two"), Added("t3", "Three")]);

        await WhenPosted(new MoveTodo(_sessionId, "t3", 1), As("ann"));

        ThenResponseIs(204);
        await ThenReadModel<SessionTodos>(
            _sessionId,
            new SessionTodos(_sessionId) { Items = [new("t1", "One", false), new("t3", "Three", false), new("t2", "Two", false)] });
    }

    [Fact]
    public async Task a_move_after_the_last_position_is_rejected()
    {
        await GivenTheSession([Created(), Added("t1", "One"), Added("t2", "Two")]);

        await WhenPosted(new MoveTodo(_sessionId, "t1", 2), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The position 2 is not in the range 0 to 1.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_move_before_the_first_position_is_rejected()
    {
        await GivenTheSession([Created(), Added("t1", "One"), Added("t2", "Two")]);

        await WhenPosted(new MoveTodo(_sessionId, "t2", -1), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The position -1 is not in the range 0 to 1.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_move_of_a_missing_todo_is_rejected()
    {
        await GivenTheSession([Created(), Added("t1", "One")]);

        await WhenPosted(new MoveTodo(_sessionId, "t2", 0), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The todo does not exist.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_remove_takes_the_todo_out_of_the_list()
    {
        await GivenTheSession([Created(), Added("t1", "One"), Added("t2", "Two")]);

        await WhenPosted(new RemoveTodo(_sessionId, "t1"), As("ann"));

        ThenResponseIs(204);
        await ThenTheSessionGained(new TodoRemoved(_sessionId, "t1", "ann", ServerFixture.Now));
        await ThenReadModel<SessionTodos>(_sessionId, new SessionTodos(_sessionId) { Items = [new("t2", "Two", false)] });
    }

    [Fact]
    public async Task a_remove_of_a_missing_todo_is_rejected()
    {
        await GivenTheSession([Created(), Added("t1", "One"), Removed("t1")]);

        await WhenPosted(new RemoveTodo(_sessionId, "t1"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The todo does not exist.");
        await ThenTheSessionGained();
    }

    [Fact]
    public async Task a_Command_on_a_deleted_Session_is_rejected()
    {
        await GivenTheSession([Created(), new SessionDeleted(_sessionId, "ann", ServerFixture.Now), new Archived("Session deleted")]);

        await WhenPosted(new AddTodo(_sessionId, "t1", "One"), As("ann"));

        ThenResponseIs(409);
        ThenRefusedWith("The Session is deleted.");
    }

    [Fact]
    public async Task a_Client_that_opens_the_Session_gets_the_todos()
    {
        await GivenEvents<Session>(_sessionId, [Created(), Added("t1", "One"), Added("t2", "Two"), Completed("t2")]);

        var bob = Own(await HubClient.ConnectAsync(Fixture.Host, ServerFixture.ApiKey, CurrentProtocol, "bob"));
        await WhenActing("bob opens the Session", _ => bob.SendAsync(new OpenSession(_sessionId, "Owl")));

        ThenTheActSucceeded();
        await ThenEventuallyPushed(
            () => bob.Received<SessionTodos>().Contains(
                new SessionTodos(_sessionId) { Items = [new("t1", "One", false), new("t2", "Two", true)] }),
            "bob's Client did not get the todos");
    }

    [Fact]
    public async Task a_Client_with_the_Session_open_gets_each_change()
    {
        await GivenEvents<Session>(_sessionId, [Created(), Added("t1", "One")]);
        var bob = Own(await HubClient.ConnectAsync(Fixture.Host, ServerFixture.ApiKey, CurrentProtocol, "bob"));
        await bob.SendAsync(new OpenSession(_sessionId, "Owl"));

        await WhenActing("ann adds a todo", _ => PostAsync(new AddTodo(_sessionId, "t2", "Two"), "ann"));

        ThenTheActSucceeded();
        await ThenEventuallyPushed(
            () => bob.Received<SessionTodos>().Contains(
                new SessionTodos(_sessionId) { Items = [new("t1", "One", false), new("t2", "Two", false)] }),
            "bob's Client did not get the added todo");
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

    private SessionCreated Created() => new(_sessionId, "Mob on the todos", "tuigether", "main", "ann", ServerFixture.Now);

    private TodoAdded Added(string todoId, string text) => new(_sessionId, todoId, text, "ann", ServerFixture.Now);

    private TodoCompleted Completed(string todoId) => new(_sessionId, todoId, "ann", ServerFixture.Now);

    private TodoRemoved Removed(string todoId) => new(_sessionId, todoId, "ann", ServerFixture.Now);
}
