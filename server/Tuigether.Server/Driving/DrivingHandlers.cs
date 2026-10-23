using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Push;
using Tuigether.Server.Sessions;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Driving;

/// <summary>
/// The Drive Commands. Each endpoint invokes the handler of its Command, see <see cref="SessionCommand"/>.
/// </summary>
public static class DrivingEndpoints
{
    [WolverinePost(CommandRoutes.RotateDriver)]
    public static Task<IResult> PostRotateDriver(
        RotateDriver command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new RotateDriverBy(command, userName), cancellation);

    [WolverinePost(CommandRoutes.PauseDrive)]
    public static Task<IResult> PostPauseDrive(
        PauseDrive command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new PauseDriveBy(command, userName), cancellation);

    [WolverinePost(CommandRoutes.ResumeDrive)]
    public static Task<IResult> PostResumeDrive(
        ResumeDrive command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new ResumeDriveBy(command, userName), cancellation);
}

/// <param name="User">The user name of the request.</param>
public sealed record RotateDriverBy(RotateDriver Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

/// <inheritdoc cref="RotateDriverBy"/>
public sealed record PauseDriveBy(PauseDrive Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

/// <inheritdoc cref="RotateDriverBy"/>
public sealed record ResumeDriveBy(ResumeDrive Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class RotateDriverHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        RotateDriverBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => DrivingRules.Rotate(current, request.Command, request.User, clock.GetUtcNow()));
}

public static class PauseDriveHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        PauseDriveBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => DrivingRules.Pause(current, request.User, clock.GetUtcNow()));
}

public static class ResumeDriveHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        ResumeDriveBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => DrivingRules.Resume(current, request.User, clock.GetUtcNow()));
}
