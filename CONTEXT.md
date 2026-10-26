# tuigether

tuigether is a terminal app for remote pair and mob programming. This glossary defines the words that the team uses
for a shared programming session.

## Language

### Sessions and people

**Session**:
One shared pair or mob programming meeting, with a title, a goal and a status (Created, Started, Finished).
_Avoid_: Room, meeting

**User**:
A person, known by the user name that the person sets in the tuigether config.
_Avoid_: Account, member

**Participant**:
A User who is present in a Session. A Participant stays present while at least one of their Clients is open in the
Session.
_Avoid_: Connected user, attendee

**Client**:
One running tuigether app. One User can run several Clients, also in the same Session.
_Avoid_: Connection, instance, terminal

### Driving

**Driver**:
The Participant who types during a Drive. The other Participants navigate.
_Avoid_: Active user, typist, pilot

**Drive**:
One timed work interval of one Driver. A Drive can be paused, resumed, stopped, skipped or switched, or it finishes
on time.
_Avoid_: Pomodoro, round, turn, work phase

**Break**:
A pause between two Drives that all Participants of a Session share.
_Avoid_: Pause (a paused Drive is not a Break)

### Shared editing

**Edit lock**:
The right of one Participant to edit the Notes or the Goal of a Session. While one Participant holds it, the others
can read but not edit.
_Avoid_: Pen, claim
