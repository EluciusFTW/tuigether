using Microsoft.AspNetCore.Mvc;
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
    public static Task<IResult> Post(
        ChangeNote command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new ChangeNoteBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.Rotate.RotateDriverBy"/>
public sealed record ChangeNoteBy(ChangeNote Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class ChangeNoteHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        ChangeNoteBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => ChangeNoteRules.Change(current, request.Command, request.User, clock.GetUtcNow()));
}
