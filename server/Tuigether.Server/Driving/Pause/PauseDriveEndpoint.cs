using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Driving.Pause;

/// <summary>
/// Invokes <see cref="PauseDriveHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class PauseDriveEndpoint
{
    [WolverinePost(CommandRoutes.PauseDrive)]
    public static Task<IResult> Post(
        PauseDrive command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new PauseDriveBy(command, userName), cancellation);
}

/// <inheritdoc cref="Rotate.RotateDriverBy"/>
public sealed record PauseDriveBy(PauseDrive Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class PauseDriveHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        PauseDriveBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => PauseDriveRules.Pause(current, request.User, clock.GetUtcNow()));
}
