using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Notes.LockAcquire;

/// <summary>
/// Invokes <see cref="AcquireNotesEditLockHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class AcquireNotesEditLockEndpoint
{
    [WolverinePost(CommandRoutes.AcquireNotesEditLock)]
    public static Task<IResult> Post(
        AcquireNotesEditLock command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new AcquireNotesEditLockBy(command, userName), cancellation);
}

/// <inheritdoc cref="Sessions.Create.CreateSessionBy"/>
public sealed record AcquireNotesEditLockBy(AcquireNotesEditLock Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class AcquireNotesEditLockHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        AcquireNotesEditLockBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => AcquireNotesEditLockRules.Acquire(current, request.User, clock.GetUtcNow()));
}
