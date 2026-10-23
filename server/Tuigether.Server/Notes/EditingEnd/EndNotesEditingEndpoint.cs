using Microsoft.AspNetCore.Mvc;
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
    public static Task<IResult> Post(
        EndNotesEditing command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new EndNotesEditingBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.Rotate.RotateDriverBy"/>
public sealed record EndNotesEditingBy(EndNotesEditing Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class EndNotesEditingHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        EndNotesEditingBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => EndNotesEditingRules.End(current, request.Command.Text, request.User, clock.GetUtcNow()));
}
