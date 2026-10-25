using Microsoft.AspNetCore.Mvc;
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
    public static Task<IResult> Post(
        AddNote command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new AddNoteBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.Rotate.RotateDriverBy"/>
public sealed record AddNoteBy(AddNote Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class AddNoteHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        AddNoteBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => AddNoteRules.Add(current, request.Command, request.User, clock.GetUtcNow()));
}
