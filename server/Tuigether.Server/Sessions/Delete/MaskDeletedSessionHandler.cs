using Marten;
using Marten.Events.Archiving;
using Tuigether.Server.Domain;
using Tuigether.Server.Shared.Masking;

namespace Tuigether.Server.Sessions.Delete;

/// <summary>
/// The events of a deleted Session stay in the archive for anonymous statistics. A second run finds nothing left to
/// mask, so a retry is safe.
/// </summary>
public static class MaskDeletedSessionHandler
{
    public static async Task HandleAsync(
        MaskDeletedSession message,
        IDocumentStore store,
        IDocumentSession session,
        CancellationToken cancellation)
    {
        var sessionId = message.SessionId;

        // The stream is archived, and IncludeStream reads no archived event.
        await store.Advanced.ApplyEventDataMasking(
            masking => masking.IncludeEvents(e => e.StreamId == sessionId && e.MaybeArchived()),
            cancellation);

        // See PersonalDataMasking for the user name column.
        session.QueueSqlCommand(
            $"update {store.Options.Events.DatabaseSchemaName}.mt_events set user_name = ? where stream_id = ? and user_name is not null",
            PersonalDataMasking.Mask,
            sessionId);

        // A projection rebuild skips archived events too. The Command handlers need only IsDeleted.
        session.Store(new Session(sessionId, IsDeleted: true));
    }
}
