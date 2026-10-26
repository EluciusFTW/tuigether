using Marten.Events;
using Tuigether.Contracts;

namespace Tuigether.Server.Shared.Masking;

/// <summary>
/// The masking rules for the personal data in the events: user names and free text.
/// <c>IDocumentStore.Advanced.ApplyEventDataMasking</c> applies them. The snapshots keep the old values until a rebuild
/// of their projections. The rules change only the event data. The user name column of each event keeps its value.
/// </summary>
public static class PersonalDataMasking
{
    public const string Mask = "****";

    public static void AddRules(IEventStoreOptions events)
    {
        events.AddMaskingRuleForProtectedInformation<SessionCreated>(static e =>
            e with { Title = Mask, Repository = Mask, Branch = Mask, User = Mask });
        events.AddMaskingRuleForProtectedInformation<SessionDeleted>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<SessionStarted>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<SessionFinished>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<SessionBranchChanged>(static e => e with { Branch = Mask, User = Mask });
        events.AddMaskingRuleForProtectedInformation<WipPushed>(static e => e with { User = Mask });

        // The Client takes the avatar from free text: the TUIGETHER_AVATAR variable.
        events.AddMaskingRuleForProtectedInformation<ParticipantJoined>(static e => e with { User = Mask, Avatar = Mask });
        events.AddMaskingRuleForProtectedInformation<ParticipantLeft>(static e => e with { User = Mask });

        events.AddMaskingRuleForProtectedInformation<DriverChanged>(static e => e with { Driver = MaskUnlessNull(e.Driver), User = Mask });
        events.AddMaskingRuleForProtectedInformation<DriveStarted>(static e => e with { Driver = Mask, User = Mask });
        events.AddMaskingRuleForProtectedInformation<DrivePaused>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<DriveResumed>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<DriveSwitched>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<DriveSkipped>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<BreakStarted>(static e => e with { User = MaskUnlessNull(e.User) });
        events.AddMaskingRuleForProtectedInformation<BreakSkipped>(static e => e with { User = Mask });

        events.AddMaskingRuleForProtectedInformation<NotesEditingStarted>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<NotesEdited>(static e => e with { Text = Mask, User = Mask });
        events.AddMaskingRuleForProtectedInformation<NotesEditingEnded>(static e => e with { User = Mask });

        events.AddMaskingRuleForProtectedInformation<GoalEditingStarted>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<GoalChanged>(static e => e with { Text = Mask, User = Mask });
        events.AddMaskingRuleForProtectedInformation<GoalEditingEnded>(static e => e with { User = Mask });

        events.AddMaskingRuleForProtectedInformation<TodoAdded>(static e => e with { Text = Mask, User = Mask });
        events.AddMaskingRuleForProtectedInformation<TodoTextChanged>(static e => e with { Text = Mask, User = Mask });
        events.AddMaskingRuleForProtectedInformation<TodoCompleted>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<TodoReopened>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<TodoMoved>(static e => e with { User = Mask });
        events.AddMaskingRuleForProtectedInformation<TodoRemoved>(static e => e with { User = Mask });

        events.AddMaskingRuleForProtectedInformation<NoteAdded>(static e => e with { Text = Mask, User = Mask });
        events.AddMaskingRuleForProtectedInformation<NoteChanged>(static e => e with { Text = Mask, User = Mask });
        events.AddMaskingRuleForProtectedInformation<NoteRemoved>(static e => e with { User = Mask });
    }

    // Null stands for "no Driver" or "no Participant", which names nobody.
    private static string? MaskUnlessNull(string? value) => value is null ? null : Mask;
}
