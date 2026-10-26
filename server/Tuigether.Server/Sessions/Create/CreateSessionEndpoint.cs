using Tuigether.Contracts;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Commands;
using Tuigether.Server.Shared.Push;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Tuigether.Server.Sessions.Create;

/// <summary>
/// Invokes <see cref="CreateSessionHandler"/>, see <see cref="SessionCommand"/>.
/// </summary>
public static class CreateSessionEndpoint
{
    [WolverinePost(CommandRoutes.CreateSession)]
    public static Task<IResult> Post(CreateSession command, IMessageBus bus, CancellationToken cancellation) =>
        SessionCommand.InvokeAsync(bus, command, cancellation);
}

public static class CreateSessionHandler
{
    public static (CommandOutcome, IMartenOp, BroadcastSessionChanged?) Handle(
        CreateSession command,
        Envelope envelope,
        TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
        {
            return (new CommandOutcome.Rejected("A Session needs a title."), MartenOps.Nothing(), null);
        }

        var created = new SessionCreated(
            Guid.CreateVersion7(),
            command.Title,
            command.Repository,
            command.Branch,
            SessionCommand.UserOf(envelope),
            clock.GetUtcNow());

        return (
            new CommandOutcome.Created(created.SessionId),
            MartenOps.StartStream<Session>(created.SessionId, created),
            new BroadcastSessionChanged(created.SessionId));
    }
}
