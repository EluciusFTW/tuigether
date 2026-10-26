using Marten;
using Microsoft.Extensions.DependencyInjection;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Masking;

namespace Tuigether.Server.Specs;

[BobcatFeature("Masking personal data")]
public sealed class MaskingPersonalData(ServerFixture server) : ServerSpec(server)
{
    private const string Mask = PersonalDataMasking.Mask;

    private const string PersonalData = "ann@example.org";

    // The Client creates these ids. They name no person and hold no text of a person.
    private static readonly string[] NoPersonalData = ["TodoId", "NoteId"];

    private static readonly DateTimeOffset Now = ServerFixture.Now;

    private readonly Guid _sessionId = Guid.CreateVersion7();

    // The spec store has the masking rules and no projection, so that a Session stream can hold each event type once,
    // in any order.
    [Fact]
    public async Task the_masking_rules_cover_every_string_field_of_every_event()
    {
        var token = TestContext.Current.CancellationToken;
        var samples = EventTypes().Select(type => Sample(type, _sessionId)).ToArray();
        samples.ShouldNotBeEmpty();
        await using var store = DocumentStore.For(options =>
        {
            options.Connection(Fixture.ConnectionString);
            options.DatabaseSchemaName = "masking_rules";
            PersonalDataMasking.AddRules(options.Events);
        });
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(_sessionId, samples);
            await session.SaveChangesAsync(token);
        }

        await WhenActing(
            "the masking rules run on the Session stream",
            _ => store.Advanced.ApplyEventDataMasking(masking => masking.IncludeStream(_sessionId), token));

        ThenTheActSucceeded();
        await using var query = store.QuerySession();
        var masked = await query.Events.FetchStreamAsync(_sessionId, token: token);
        masked
            .SelectMany(static e => PersonalFields(e.Data))
            .Where(static field => field.Value == PersonalData)
            .Select(static field => field.Name)
            .ShouldBeEmpty("These event fields keep their personal data. Add a masking rule, or exempt the field in this spec.");
    }

    [Fact]
    public async Task a_delete_masks_the_events_and_the_aggregate_of_the_Session()
    {
        await GivenEvents<Session>(
            _sessionId,
            [
                new SessionCreated(_sessionId, "Mob with ann", "ann/tuigether", "ann/masking", "ann", Now),
                new ParticipantJoined(_sessionId, "ann", "Owl", Now),
                new SessionStarted(_sessionId, "ann", Now),
                new ParticipantJoined(_sessionId, "bob", "Fox", Now),
                new DriverChanged(_sessionId, "ann", "bob", Now),
                new DriveStarted(_sessionId, "ann", Now.AddMinutes(10), "bob", Now),
                new NotesEditingStarted(_sessionId, "ann", Now),
                new NotesEdited(_sessionId, "Call ann", "ann", Now),
                new GoalEditingStarted(_sessionId, "bob", Now),
                new GoalChanged(_sessionId, "Ship the fix of ann", "bob", Now),
                new TodoAdded(_sessionId, "t1", "Ask ann", "ann", Now),
                new NoteAdded(_sessionId, "n1", "Port of ann", "ann", Now),
                new SessionBranchChanged(_sessionId, "ann/erasure", "ann", Now),
                new WipPushed(_sessionId, "ann", Now),
            ]);

        await WhenPosted(new DeleteSession(_sessionId), As("ann"));

        ThenResponseIs(204);

        // The tracked request waits for the masking of the deleted Session.
        var events = await SessionEventsAsync(_sessionId);
        events
            .Where(static e => e.Data.GetType().Assembly == typeof(SessionCreated).Assembly)
            .SelectMany(static e => PersonalFields(e.Data))
            .Where(static field => field.Value != Mask)
            .Select(static field => field.Name)
            .ShouldBeEmpty("These event fields of the deleted Session keep their personal data.");
        events.Single(static e => e.Data is SessionDeleted).UserName.ShouldBe(Mask);
        events.Where(static e => e.UserName is not null).ShouldAllBe(static e => e.UserName == Mask);
        await ThenTheSessionAggregateHoldsOnlyItsIdAndTheDeletedFlag();
    }

    [Then("the Session aggregate holds only its id and the deleted flag")]
    internal async Task ThenTheSessionAggregateHoldsOnlyItsIdAndTheDeletedFlag()
    {
        await using var query = Services.GetRequiredService<IDocumentStore>().QuerySession();
        var session = await query.LoadAsync<Session>(_sessionId, TestContext.Current.CancellationToken);
        session.ShouldBeEquivalentTo(new Session(_sessionId, IsDeleted: true));
    }

    // Each event has the stream id SessionId and the time At. No Command and no snapshot has both.
    private static IEnumerable<Type> EventTypes() =>
        typeof(SessionCreated).Assembly
            .GetExportedTypes()
            .Where(static type =>
                type.GetProperty("SessionId")?.PropertyType == typeof(Guid)
                && type.GetProperty("At")?.PropertyType == typeof(DateTimeOffset));

    // An event is a positional record, so its one public constructor takes every field.
    private static object Sample(Type eventType, Guid sessionId)
    {
        var constructor = eventType.GetConstructors().Single();
        return constructor.Invoke(constructor.GetParameters().Select(parameter => SampleValue(parameter.ParameterType, sessionId)).ToArray());
    }

    private static object? SampleValue(Type type, Guid sessionId) =>
        type switch
        {
            _ when type == typeof(string) => PersonalData,
            _ when type == typeof(Guid) => sessionId,
            { IsValueType: true } => Activator.CreateInstance(type),
            _ => null,
        };

    // The string fields of an event that can hold personal data.
    private static IEnumerable<(string Name, string? Value)> PersonalFields(object data) =>
        data.GetType()
            .GetProperties()
            .Where(static property => property.PropertyType == typeof(string) && !NoPersonalData.Contains(property.Name))
            .Select(property => ($"{data.GetType().Name}.{property.Name}", (string?)property.GetValue(data)));
}
