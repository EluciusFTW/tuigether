using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Driving.BreakSkip;

/// <summary>
/// Invokes <see cref="SkipBreakHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class SkipBreakEndpoint
{
    [WolverinePost(CommandRoutes.SkipBreak)]
    public static Task<IResult> Post(SkipBreak command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class SkipBreakHandler
{
    public static (CommandOutcome, Events, BroadcastSessionChanged?, OutgoingMessages) Handle(
        SkipBreak command,
        [WriteAggregate(Required = false)] Session? session,
        Envelope envelope,
        TimeProvider clock)
    {
        if (session is null)
        {
            return SessionCommand.NotFound();
        }

        if (session.IsDeleted)
        {
            return SessionCommand.Reject(SessionRules.DeletedReason);
        }

        if (session.Timer != TimerPhase.Break)
        {
            return SessionCommand.Reject("No Break runs.");
        }

        var user = SessionCommand.UserOf(envelope);
        var now = clock.GetUtcNow();
        return SessionCommand.Accept(session, [new BreakSkipped(session.Id, user, now)]);
    }
}
