using Microsoft.AspNetCore.Mvc;
using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Git.WipPush;

/// <summary>
/// Invokes <see cref="RecordWipPushHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class RecordWipPushEndpoint
{
    [WolverinePost(CommandRoutes.RecordWipPush)]
    public static Task<IResult> Post(
        RecordWipPush command,
        [FromHeader(Name = RequestHeaders.UserName)] string userName,
        IMessageBus bus,
        CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, new RecordWipPushBy(command, userName), cancellation);
}

/// <inheritdoc cref="Sessions.Create.CreateSessionBy"/>
public sealed record RecordWipPushBy(RecordWipPush Command, string User)
{
    public Guid SessionId => Command.SessionId;
}

public static class RecordWipPushHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?) Handle(
        RecordWipPushBy request,
        [WriteAggregate(Required = false)] Session? session,
        TimeProvider clock) =>
        SessionCommand.Decide(session, current => RecordWipPushRules.Record(current, request.User, clock.GetUtcNow()));
}
