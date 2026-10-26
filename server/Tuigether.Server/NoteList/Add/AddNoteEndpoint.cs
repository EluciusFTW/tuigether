using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.NoteList.Add;

/// <summary>
/// Invokes <see cref="AddNoteHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class AddNoteEndpoint
{
    [WolverinePost(CommandRoutes.AddNote)]
    public static Task<IResult> Post(AddNote command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class AddNoteHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        AddNote command,
        [WriteAggregate(Required = false)] Session? session,
        Envelope envelope,
        TimeProvider clock)
    {
        if (session is null)
        {
            return SessionCommand.NotFound();
        }

        if (session.IsDeleted)
        {
            return SessionCommand.Reject(SessionRules.DeletedReason);
        }

        if (session.NoteIds.Contains(command.NoteId))
        {
            return SessionCommand.Reject("A note with this id exists.");
        }

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();
        return SessionCommand.Accept(session, [new NoteAdded(session.Id, command.NoteId, command.Text, user, now)]);
    }
}
