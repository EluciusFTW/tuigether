using Microsoft.AspNetCore.Mvc;
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
    public static Task<IResult> Post(
        RemoveNote command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new RemoveNoteBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.Rotate.RotateDriverBy"/>
public sealed record RemoveNoteBy(RemoveNote Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class RemoveNoteHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        RemoveNoteBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => RemoveNoteRules.Remove(current, request.Command, request.User, clock.GetUtcNow()));
}
