using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.NoteList.Remove;

/// <summary>
/// Invokes <see cref="RemoveNoteHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class RemoveNoteEndpoint
{
    [WolverinePost(CommandRoutes.RemoveNote)]
    public static Task<IResult> Post(RemoveNote command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class RemoveNoteHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        RemoveNote command,
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
        return SessionCommand.Accept(session, [new NoteRemoved(session.Id, command.NoteId, user, now)]);
    }
}
