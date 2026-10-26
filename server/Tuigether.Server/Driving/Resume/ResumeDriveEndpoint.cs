using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Driving.Resume;

/// <summary>
/// Invokes <see cref="ResumeDriveHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class ResumeDriveEndpoint
{
    [WolverinePost(CommandRoutes.ResumeDrive)]
    public static Task<IResult> Post(
        ResumeDrive command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new ResumeDriveBy(command, userName), cancellation);
}

/// <inheritdoc cref="Sessions.Create.CreateSessionBy"/>
public sealed record ResumeDriveBy(ResumeDrive Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class ResumeDriveHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        ResumeDriveBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var (outcome, events, broadcast) = SessionCommand.Decide(session, current => ResumeDriveRules.Resume(current, request.User, now));
        return (outcome, events, broadcast, DrivingRules.ScheduleEnds(events, now));
    }
}
