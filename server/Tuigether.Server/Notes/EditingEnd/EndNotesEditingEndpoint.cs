using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Notes.EditingEnd;

/// <summary>
/// Invokes <see cref="EndNotesEditingHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class EndNotesEditingEndpoint
{
    [WolverinePost(CommandRoutes.EndNotesEditing)]
    public static Task<IResult> Post(EndNotesEditing command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class EndNotesEditingHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        EndNotesEditing command,
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

        var user = SessionCommand.UserOf(envelope);
        if (session.NotesEditLockHolder != user)
        {
            return SessionCommand.Reject($"{user} does not hold the Edit lock of the Notes.");
        }

        var now = clock.GetUtcNow();
        return session.NotesText == command.Text
            ? SessionCommand.Accept(session, [new NotesEditingEnded(session.Id, user, now)])
            : SessionCommand.Accept(session, [new NotesEdited(session.Id, command.Text, user, now), new NotesEditingEnded(session.Id, user, now)]);
    }
}
