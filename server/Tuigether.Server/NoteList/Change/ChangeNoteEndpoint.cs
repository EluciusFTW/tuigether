using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.NoteList.Change;

/// <summary>
/// Invokes <see cref="ChangeNoteHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class ChangeNoteEndpoint
{
    [WolverinePost(CommandRoutes.ChangeNote)]
    public static Task<IResult> Post(ChangeNote command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class ChangeNoteHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        ChangeNote command,
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

        if (!session.NoteIds.Contains(command.NoteId))
        {
            return SessionCommand.Reject("The note does not exist.");
        }

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();
        return SessionCommand.Accept(session, [new NoteChanged(session.Id, command.NoteId, command.Text, user, now)]);
    }
}
