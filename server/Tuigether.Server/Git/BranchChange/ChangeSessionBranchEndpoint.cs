using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Git.BranchChange;

/// <summary>
/// Invokes <see cref="ChangeSessionBranchHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class ChangeSessionBranchEndpoint
{
    [WolverinePost(CommandRoutes.ChangeSessionBranch)]
    public static Task<IResult> Post(
        ChangeSessionBranch command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new ChangeSessionBranchBy(command, userName), cancellation);
}

/// <inheritdoc cref="Driving.Rotate.RotateDriverBy"/>
public sealed record ChangeSessionBranchBy(ChangeSessionBranch Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class ChangeSessionBranchHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        ChangeSessionBranchBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => ChangeSessionBranchRules.Change(current, request.Command, request.User, clock.GetUtcNow()));
}
