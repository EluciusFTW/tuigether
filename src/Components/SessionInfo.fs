module SessionInfo

open System
open Elmish
open Firebase.Database
open Spectre.Console
open Spectre.Tui
open Keymap
open Locking
open SpectreTuff
open SpectreTuff.Layout
open SpectreTuff.Widgets

// Branch names follow the house convention `<prefix>/EWK-<ticket>-<name>`, so the
// popup walks the parts one question at a time and only then creates anything.
type BranchPopupStage =
  // What the new branch grows out of is asked first. Carries the candidates it
  // offers, gathered once when the popup opens rather than on every render.
  | ChoosingBase of bases: (string * Git.BranchBase) list * selected: int
  | EditingTicket of error: string option
  | ChoosingPrefix of selected: int
  | EditingName of error: string option
  | Submitting
  | CreateFailed of error: string

type BranchPopup = {
  Ticket: string
  Prefix: string
  Name: string
  // Answered in the ChoosingBase stage and kept for the create itself, so a retry
  // after a failure uses the same base.
  Base: Git.BranchBase
  Stage: BranchPopupStage
}

let private branchPrefixes = [
  "feature"
  "task"
  "fix"
  "technical"
  "maintenance"
  "experimental"
  "hotfix"
]

// The convention: `feature/EWK-1234-some-name`. Whitespace in the typed name becomes
// dashes, since a ref cannot hold spaces.
let private branchNameOf (popup: BranchPopup) =
  let slug = String.Join("-", popup.Name.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries))
  sprintf "%s/EWK-%s-%s" popup.Prefix popup.Ticket slug

type SyncPopupStage =
  | RunningSync
  | SyncDiverged of message: string
  | DiscardingLocal
  | SyncFailed of error: string

// The branch picker's live state: the full list as loaded, plus the filter the
// user has typed and where the cursor sits in the filtered view.
type BranchChoice = {
  All: Git.BranchRef list
  Filter: string
  Selected: int
}

module BranchChoice =
  let visible (choice: BranchChoice) =
    match choice.Filter with
    | "" -> choice.All
    | filter ->
      choice.All
      |> List.filter (fun b -> b.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))

type SwitchPopupStage =
  | LoadingBranches
  | ChoosingBranch of BranchChoice
  // The spec asks before touching a dirty tree rather than stashing behind the
  // user's back, so the choice gets its own stage.
  | ConfirmDirty of branch: Git.BranchRef * changes: int
  | SwitchingTo of branch: Git.BranchRef
  | SwitchFailed of error: string
  // The switch landed but something still needs saying: a conflicted stash pop, a
  // stash left on the stack, or a pull that could not fast-forward.
  | SwitchedWithNotes of notes: string list

type InputMode =
  | Normal
  | Insert
  | GoalPopup
  | BranchPopup of BranchPopup
  | SyncPopup of SyncPopupStage
  | SwitchPopup of SwitchPopupStage

type Model = {
  Client: FirebaseClient
  SessionId: string
  User: string
  StartedAt: int64
  GoalContent: string
  GoalSaveToken: int
  InsertActivityToken: int
  InputMode: InputMode
  // Live goal editor, present only while editing (Insert or GoalPopup). It owns
  // the caret; GoalContent is synced from it on each edit and stays the source
  // of truth for persistence and non-editing display.
  Editor: TextBoxWidget option
  Lock: Locking.Lock option
  GitBranch: string
  LocalGitBranch: string
  GitRepo: string
  LocalRepo: string
  SessionTitle: string
  LastSeenWipAt: int64
}

type Msg =
  | EnterInsert
  | ExitInsert
  | Edit of TextEditing.EditAction
  | MaybeSaveGoal of int
  | MaybeAutoExitInsert of int
  | SessionDataUpdated of Session.Data
  | StateSaved
  | BeginCreateBranch
  | BranchTypeChar of char
  | BranchTypeText of string
  | BranchTypeBackspace
  | ConfirmBranch
  | BranchSelectUp
  | BranchSelectDown
  | DismissBranchPopup
  | BranchCreateCompleted of name: string * Result<unit, string>
  | BeginSwitchBranch
  | BranchesLoaded of Result<Git.BranchRef list, string>
  | SwitchFilterChar of char
  | SwitchFilterText of string
  | SwitchFilterBackspace
  | SwitchSelectUp
  | SwitchSelectDown
  | ChooseBranch
  | SwitchWithPolicy of Git.DirtyPolicy
  | SwitchCompleted of name: string * Result<Git.SwitchOutcome, Git.SwitchFailure>
  | DismissSwitchPopup
  | BeginSync
  | BeginWipSync
  | SyncCompleted of Result<Git.SyncResult, string>
  | DiscardLocalAndPull
  | DiscardCompleted of Result<unit, string>
  | WipSyncCompleted of Result<unit, string>
  | DismissSyncPopup
  | MaybeShowGoalPopup
  | CloseGoalPopup

let private goalDebounceMs = 300
let private autoExitInsertMs = 30_000

let private nowMs () : int64 =
  Clock.nowMs ()

let private isLockedByOther (model: Model) =
  Locking.heldByOther (nowMs ()) model.User model.Lock

let isHoldingLock (model: Model) =
  match model.InputMode, model.Lock with
  | Insert, Some l
  | GoalPopup, Some l -> l.Owner = model.User
  | _ -> false

let private branchFromData (data: Session.Data) =
  match isNull data.GitBranch || data.GitBranch = "" with
  | true -> "(unknown)"
  | false -> data.GitBranch

let private repoFromData (data: Session.Data) =
  match isNull data.GitRepo with
  | true -> ""
  | false -> data.GitRepo

let private isRepoOK (model: Model) =
  match model.LocalRepo, model.GitRepo with
  | "", _ -> false
  | _, "" -> true
  | local, session -> local = session

let private lockFromData (data: Session.Data) =
  match isNull data.GoalLockOwner || data.GoalLockOwner = "" with
  | true -> None
  | false ->
    Some {
      Owner = data.GoalLockOwner
      LockedAt = data.GoalLockedAt
    }

let init (client: FirebaseClient) (sessionId: string) (user: string) (sessionData: Session.Data) = {
  Client = client
  SessionId = sessionId
  User = user
  StartedAt = sessionData.StartedAt
  GoalContent =
    match isNull sessionData.Goal with
    | true -> ""
    | false -> sessionData.Goal
  GoalSaveToken = 0
  InsertActivityToken = 0
  InputMode = Normal
  Editor = None
  Lock = lockFromData sessionData
  GitBranch = branchFromData sessionData
  LocalGitBranch = Git.readCurrentBranch ()
  GitRepo = repoFromData sessionData
  LocalRepo = Git.readRepoName ()
  SessionTitle =
    match isNull sessionData.Title with
    | true -> ""
    | false -> sessionData.Title
  LastSeenWipAt = sessionData.LastWipPushAt
}

let private normalBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.dynamic (CharKey 'i') (fun model ->
    match isLockedByOther model with
    | true ->
      let owner =
        model.Lock
        |> Option.map (fun l -> l.Owner)
        |> Option.defaultValue "another user"

      {
        Description = sprintf "locked by %s" owner
        Message = Some EnterInsert
      }
    | false -> {
        Description = "edit goal"
        Message = Some EnterInsert
      })
  KeyBinding.dynamic (CharKey 'n') (fun model ->
    match isRepoOK model with
    | true -> {
        Description = "new branch"
        Message = Some BeginCreateBranch
      }
    | false -> {
        Description = "new branch (wrong repo)"
        Message = None
      })
  KeyBinding.dynamic (CharKey 'c') (fun model ->
    match isRepoOK model with
    | true -> {
        Description = "change branch"
        Message = Some BeginSwitchBranch
      }
    | false -> {
        Description = "change branch (wrong repo)"
        Message = None
      })
  KeyBinding.dynamic (CharKey 'S') (fun model ->
    match isRepoOK model with
    | false -> {
        Description = "sync branch (wrong repo)"
        Message = None
      }
    | true ->
      let help =
        match model.LocalGitBranch = model.GitBranch with
        | true -> "pull"
        | false -> "sync branch"

      {
        Description = help
        Message = Some BeginSync
      })
  KeyBinding.dynamic (CharKey 'w') (fun model ->
    match isRepoOK model && model.LocalGitBranch = model.GitBranch with
    | true -> {
        Description = "WIP sync"
        Message = Some BeginWipSync
      }
    | false -> {
        Description = "WIP sync (unavailable)"
        Message = None
      })
]

let private insertModeBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.Escape "exit insert" ExitInsert
]

let private branchChoiceBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.UpArrow "up" BranchSelectUp
  KeyBinding.createSpecial ConsoleKey.DownArrow "down" BranchSelectDown
  KeyBinding.createSpecial ConsoleKey.Enter "choose" ConfirmBranch
  KeyBinding.createSpecial ConsoleKey.Escape "cancel" DismissBranchPopup
]

let private branchFailedBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.Enter "retry" ConfirmBranch
  KeyBinding.createSpecial ConsoleKey.Escape "dismiss" DismissBranchPopup
]

let private switchDirtyBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.create 's' "stash & carry over" (SwitchWithPolicy Git.StashAndCarry)
  KeyBinding.create 'l' "stash & leave behind" (SwitchWithPolicy Git.StashAndLeave)
  KeyBinding.createSpecial ConsoleKey.Escape "abort" DismissSwitchPopup
]

let private switchDoneBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.Escape "dismiss" DismissSwitchPopup
  |> KeyBinding.orKey (SpecialKey ConsoleKey.Enter)
]

let private syncFailedBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.Escape "dismiss" DismissSyncPopup
  |> KeyBinding.orKey (SpecialKey ConsoleKey.Enter)
]

// Discard is destructive, so it's bound to an explicit 'd' — never Enter, which
// is too easy to hit reflexively. Esc/Enter both cancel (safe default).
let private syncDivergedBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.create 'd' "DISCARD local, take origin" DiscardLocalAndPull
  KeyBinding.createSpecial ConsoleKey.Escape "cancel" DismissSyncPopup
  |> KeyBinding.orKey (SpecialKey ConsoleKey.Enter)
]

let private goalPopupBindings: KeyBinding<Model, Msg> list = [
  KeyBinding.createSpecial ConsoleKey.Enter "save & close" CloseGoalPopup
  KeyBinding.createSpecial ConsoleKey.Escape "dismiss" CloseGoalPopup
]

let private emptyBindings: KeyBinding<Model, Msg> list = []

let handleKey (key: ConsoleKeyInfo) (model: Model) : Msg option =
  match model.InputMode with
  | Insert ->
    match key.Key with
    | ConsoleKey.Escape -> Some ExitInsert
    | _ -> TextEditing.keyToAction true key |> Option.map Edit
  | Normal -> KeyBinding.handleKey normalBindings key model
  | GoalPopup ->
    // Single-line: Enter and Escape both close.
    match key.Key with
    | ConsoleKey.Escape -> Some CloseGoalPopup
    | ConsoleKey.Enter -> Some CloseGoalPopup
    | _ -> TextEditing.keyToAction false key |> Option.map Edit
  | BranchPopup { Stage = EditingTicket _ }
  | BranchPopup { Stage = EditingName _ } ->
    match key.Key with
    | ConsoleKey.Escape -> Some DismissBranchPopup
    | ConsoleKey.Enter -> Some ConfirmBranch
    | ConsoleKey.Backspace -> Some BranchTypeBackspace
    | _ when key.KeyChar <> '\000' -> Some(BranchTypeChar key.KeyChar)
    | _ -> None
  | BranchPopup { Stage = ChoosingBase _ }
  | BranchPopup { Stage = ChoosingPrefix _ } -> KeyBinding.handleKey branchChoiceBindings key model
  | BranchPopup { Stage = Submitting } -> None
  | BranchPopup { Stage = CreateFailed _ } -> KeyBinding.handleKey branchFailedBindings key model
  | SyncPopup RunningSync
  | SyncPopup DiscardingLocal -> None
  | SyncPopup(SyncDiverged _) -> KeyBinding.handleKey syncDivergedBindings key model
  | SyncPopup(SyncFailed _) -> KeyBinding.handleKey syncFailedBindings key model
  | SwitchPopup LoadingBranches
  | SwitchPopup(SwitchingTo _) -> None
  | SwitchPopup(ChoosingBranch _) ->
    match key.Key with
    | ConsoleKey.Escape -> Some DismissSwitchPopup
    | ConsoleKey.Enter -> Some ChooseBranch
    | ConsoleKey.UpArrow -> Some SwitchSelectUp
    | ConsoleKey.DownArrow -> Some SwitchSelectDown
    | ConsoleKey.Backspace -> Some SwitchFilterBackspace
    // Everything else printable narrows the list; branch names are the only text
    // being typed here, so there is no separate insert mode.
    | _ when key.KeyChar <> '\000' && not (Char.IsControl key.KeyChar) -> Some(SwitchFilterChar key.KeyChar)
    | _ -> None
  | SwitchPopup(ConfirmDirty _) -> KeyBinding.handleKey switchDirtyBindings key model
  | SwitchPopup(SwitchFailed _)
  | SwitchPopup(SwitchedWithNotes _) -> KeyBinding.handleKey switchDoneBindings key model

let handlePaste (text: string) (model: Model) : Msg option =
  match model.InputMode with
  | Insert -> Some(Edit(TextEditing.pasteAction true text))
  | GoalPopup -> Some(Edit(TextEditing.pasteAction false text))
  | BranchPopup { Stage = EditingTicket _ }
  | BranchPopup { Stage = EditingName _ } -> Some(BranchTypeText text)
  | SwitchPopup(ChoosingBranch _) -> Some(SwitchFilterText text)
  | _ -> None

let capturesInput (model: Model) =
  match model.InputMode with
  | Normal -> false
  | _ -> true

let keyMap (model: Model) =
  let bindings =
    match model.InputMode with
    | Normal -> normalBindings
    | Insert -> insertModeBindings
    | GoalPopup -> goalPopupBindings
    | SyncPopup RunningSync
    | SyncPopup DiscardingLocal -> emptyBindings
    | SyncPopup(SyncDiverged _) -> syncDivergedBindings
    | SyncPopup(SyncFailed _) -> syncFailedBindings
    // The branch and switch dialogs are work-area overlays drawn by SessionView,
    // not part of this panel — so this panel's key row stays quiet while they own
    // input, the way it does for the drive-log overlay. Their keys live in the
    // binding lists that `handleKey` dispatches from; they are simply not
    // advertised here, where they would describe a dialog drawn somewhere else.
    | BranchPopup _
    | SwitchPopup _ -> emptyBindings

  KeyBinding.toKeyMap bindings model

let private saveGoalCmd (model: Model) : Cmd<Msg> =
  Cmd.OfAsync.perform
    (fun () -> Firebase.Sessions.saveGoal model.Client model.SessionId model.GoalContent)
    ()
    (fun () -> StateSaved)

// Debounce goal writes: each typed character bumps a token and schedules a
// MaybeSaveGoal for that token after a short idle delay. The actual save only
// fires if the token still matches the latest one — so a burst of fast
// keystrokes collapses into a single Firebase write at the end.
let private scheduleGoalSave (token: int) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> async { do! Async.Sleep goalDebounceMs }) () (fun () -> MaybeSaveGoal token)

// Auto-exit Insert mode after autoExitInsertMs of no typing activity. Every
// keystroke bumps the activity token and schedules a fresh check; only the
// scheduled check whose token still matches actually exits.
let private scheduleAutoExit (token: int) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> async { do! Async.Sleep autoExitInsertMs }) () (fun () -> MaybeAutoExitInsert token)

let private saveGoalLockCmd (model: Model) : Cmd<Msg> =
  match model.Lock with
  | Some lock ->
    Cmd.OfAsync.perform
      (fun () -> Firebase.Sessions.saveGoalLock model.Client model.SessionId lock.Owner lock.LockedAt)
      ()
      (fun () -> StateSaved)
  | None -> []

let private releaseGoalLockCmd (model: Model) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> Firebase.Sessions.releaseGoalLock model.Client model.SessionId) () (fun () ->
    StateSaved)

// Where a new branch can grow out of: the branch checked out now, the repo's default
// branch, and the newest release branches. Duplicates of the current branch are left
// out — picking them would mean the same commit.
let private branchBases (model: Model) : (string * Git.BranchBase) list =
  let current = model.LocalGitBranch

  [
    yield sprintf "%s (current)" current, Git.FromHead

    match Git.readDefaultBranch () with
    | Some defaultBranch when defaultBranch <> current ->
      yield sprintf "%s (default)" defaultBranch, Git.FromBranch defaultBranch
    | _ -> ()

    for release in Git.listReleaseBranches 3 do
      match release <> current with
      | true -> yield release, Git.FromBranch release
      | false -> ()
  ]

// Typing goes to whichever part of the branch name the popup is currently asking
// for, and clears the stage's validation error as it goes.
let private editBranchText (transform: string -> string) (model: Model) =
  match model.InputMode with
  | BranchPopup({ Stage = EditingTicket _ } as popup) -> {
      model with
          InputMode =
            BranchPopup {
              popup with
                  Ticket = transform popup.Ticket
                  Stage = EditingTicket None
            }
    }
  | BranchPopup({ Stage = EditingName _ } as popup) -> {
      model with
          InputMode =
            BranchPopup {
              popup with
                  Name = transform popup.Name
                  Stage = EditingName None
            }
    }
  | _ -> model

let private createBranchCmd (baseRef: Git.BranchBase) (name: string) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> Git.createAndPushBranch baseRef name) () (fun result ->
    BranchCreateCompleted(name, result))

let private syncCmd (onSessionBranch: bool) (sessionBranch: string) : Cmd<Msg> =
  match onSessionBranch with
  | true -> Cmd.OfAsync.perform Git.syncCurrentBranch () SyncCompleted
  | false ->
    // Checkout only ff's, never diverges.
    Cmd.OfAsync.perform (fun () -> Git.fetchAndCheckout sessionBranch) () (fun result ->
      SyncCompleted(Result.map (fun () -> Git.Synced) result))

let private resetToUpstreamCmd () : Cmd<Msg> =
  Cmd.OfAsync.perform Git.resetToUpstream () DiscardCompleted

let private wipSyncCmd (title: string) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> Git.wipSync title) () WipSyncCompleted

let private saveWipPushCmd (model: Model) (timestamp: int64) : Cmd<Msg> =
  Cmd.OfAsync.perform
    (fun () -> Firebase.Sessions.saveWipPush model.Client model.SessionId model.User timestamp)
    ()
    (fun () -> StateSaved)

let private saveGitBranchCmd (model: Model) (branch: string) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> Firebase.Sessions.saveGitBranch model.Client model.SessionId branch) () (fun () ->
    StateSaved)

let private listBranchesCmd () : Cmd<Msg> =
  Cmd.OfAsync.perform Git.listBranches () BranchesLoaded

let private switchBranchCmd (policy: Git.DirtyPolicy option) (branch: Git.BranchRef) : Cmd<Msg> =
  Cmd.OfAsync.perform (fun () -> Git.switchToBranch policy branch) () (fun result ->
    SwitchCompleted(branch.Name, result))

// Wraps around, like the other list pickers in the app.
let private moveBranchSelection (step: int) (model: Model) =
  let moved count selected =
    (selected + step + count) % count

  match model.InputMode with
  | BranchPopup({
                  Stage = ChoosingBase(bases, selected)
                } as popup) -> {
      model with
          InputMode =
            BranchPopup {
              popup with
                  Stage = ChoosingBase(bases, moved (List.length bases) selected)
            }
    }
  | BranchPopup({ Stage = ChoosingPrefix selected } as popup) -> {
      model with
          InputMode =
            BranchPopup {
              popup with
                  Stage = ChoosingPrefix(moved (List.length branchPrefixes) selected)
            }
    }
  | _ -> model

// Wraps around the filtered view, matching the other list pickers in the app.
let private moveSwitchSelection (step: int) (model: Model) =
  match model.InputMode with
  | SwitchPopup(ChoosingBranch choice) ->
    match BranchChoice.visible choice |> List.length with
    | 0 -> model
    | count -> {
        model with
            InputMode =
              SwitchPopup(
                ChoosingBranch {
                  choice with
                      Selected = (choice.Selected + step + count) % count
                }
              )
      }
  | _ -> model

// Any edit to the filter resets the cursor to the top, so the highlighted row is
// always one the new filter actually matches.
let private withFilter (filter: string) (model: Model) =
  match model.InputMode with
  | SwitchPopup(ChoosingBranch choice) -> {
      model with
          InputMode =
            SwitchPopup(
              ChoosingBranch {
                choice with
                    Filter = filter
                    Selected = 0
              }
            )
    }
  | _ -> model

let private goalLook = Look.fromColor Color.Yellow |> Look.withDecorations [ Decoration.Italic ]

let update msg model =
  match msg with
  | EnterInsert ->
    match isLockedByOther model with
    | true -> model, []
    | false ->
      let activityToken = model.InsertActivityToken + 1

      let lock = {
        Owner = model.User
        LockedAt = nowMs ()
      }

      // Build the multi-line editor from the current goal, caret at the end.
      let editor =
        textBox model.GoalContent
        |> withMode TextBoxMode.MultiLine
        |> TextBoxes.withLook goalLook
        |> focused

      editor.MoveToEnd()

      let updated = {
        model with
            InputMode = Insert
            InsertActivityToken = activityToken
            Lock = Some lock
            Editor = Some editor
      }

      updated, Cmd.batch [ saveGoalLockCmd updated; scheduleAutoExit activityToken ]
  | ExitInsert ->
    match model.InputMode with
    | Insert ->
      // Bump the token so any in-flight debounced save is cancelled, then flush
      // the current content immediately so other users see the final edit.
      let bumped = model.GoalSaveToken + 1

      let syncedContent =
        match model.Editor with
        | Some editor -> editor.Text
        | None -> model.GoalContent

      let updated = {
        model with
            InputMode = Normal
            GoalContent = syncedContent
            GoalSaveToken = bumped
            Editor = None
            Lock = None
      }

      updated, Cmd.batch [ saveGoalCmd updated; releaseGoalLockCmd updated ]
    | _ -> model, []
  | Edit action ->
    match model.Editor with
    | None -> model, []
    | Some editor ->
      TextEditing.apply action editor
      let activityToken = model.InsertActivityToken + 1

      // Text edits sync back for persistence and schedule a debounced save; pure
      // caret moves only refresh the idle auto-exit timer.
      match TextEditing.isMutation action with
      | true ->
        let bumped = model.GoalSaveToken + 1

        let updated = {
          model with
              GoalContent = editor.Text
              GoalSaveToken = bumped
              InsertActivityToken = activityToken
        }

        updated, Cmd.batch [ scheduleGoalSave bumped; scheduleAutoExit activityToken ]
      | false ->
        {
          model with
              InsertActivityToken = activityToken
        },
        scheduleAutoExit activityToken
  | MaybeSaveGoal token ->
    match token = model.GoalSaveToken with
    | true ->
      // Refresh the lock timestamp on every save so the holder doesn't appear
      // stale to other clients while they're actively typing.
      let refreshedLock =
        match model.Lock with
        | Some l when l.Owner = model.User -> Some { l with LockedAt = nowMs () }
        | other -> other

      let updated = { model with Lock = refreshedLock }
      updated, Cmd.batch [ saveGoalCmd updated; saveGoalLockCmd updated ]
    | false -> model, []
  | MaybeAutoExitInsert token ->
    match model.InputMode = Insert && token = model.InsertActivityToken with
    | true -> model, Cmd.ofMsg ExitInsert
    | false -> model, []
  | SessionDataUpdated data ->
    // While the user is actively typing, ignore the goal echo from the remote —
    // applying it would clobber characters typed since the in-flight save was
    // dispatched.
    let goalContent =
      match model.InputMode with
      | Insert
      | GoalPopup -> model.GoalContent
      | _ ->
        match isNull data.Goal with
        | true -> ""
        | false -> data.Goal

    let newGitBranch = branchFromData data
    let newGitRepo = repoFromData data

    let repoOK =
      match model.LocalRepo, newGitRepo with
      | "", _ -> false
      | _, "" -> true
      | local, session -> local = session

    let isRemoteWipPush =
      data.LastWipPushAt > model.LastSeenWipAt
      && not (isNull data.LastWipPushBy)
      && data.LastWipPushBy <> model.User

    let shouldAutoPull =
      isRemoteWipPush
      && model.InputMode = Normal
      && repoOK
      && model.LocalGitBranch = newGitBranch

    let updated = {
      model with
          StartedAt = data.StartedAt
          GoalContent = goalContent
          Lock = lockFromData data
          GitBranch = newGitBranch
          GitRepo = newGitRepo
          SessionTitle =
            match isNull data.Title with
            | true -> ""
            | false -> data.Title
          LastSeenWipAt = data.LastWipPushAt
          InputMode =
            match shouldAutoPull with
            | true -> SyncPopup RunningSync
            | false -> model.InputMode
    }

    let cmd =
      match shouldAutoPull with
      | true -> syncCmd true newGitBranch
      | false -> []

    updated, cmd
  | StateSaved -> model, []
  | BeginCreateBranch ->
    match model.InputMode, isRepoOK model with
    | Normal, true ->
      let bases = branchBases model

      // With only one candidate — the branch already checked out — there is nothing
      // to ask, so go straight to the ticket number.
      let stage =
        match bases with
        | _ :: _ :: _ -> ChoosingBase(bases, 0)
        | _ -> EditingTicket None

      {
        model with
            InputMode =
              BranchPopup {
                Ticket = ""
                Prefix = ""
                Name = ""
                Base = Git.FromHead
                Stage = stage
              }
      },
      []
    | _ -> model, []
  | BranchTypeChar c -> editBranchText (fun current -> current + string c) model, []
  | BranchTypeText text ->
    // Both fields are single-line; drop any pasted newlines.
    let cleaned = text.Replace("\r", "").Replace("\n", "")
    editBranchText (fun current -> current + cleaned) model, []
  | BranchTypeBackspace -> editBranchText Str.dropLast model, []
  | ConfirmBranch ->
    match model.InputMode with
    | BranchPopup({
                    Stage = ChoosingBase(bases, selected)
                  } as popup) ->
      match bases |> List.tryItem selected with
      | None -> model, []
      | Some(_, baseRef) ->
        {
          model with
              InputMode =
                BranchPopup {
                  popup with
                      Base = baseRef
                      Stage = EditingTicket None
                }
        },
        []
    | BranchPopup({ Stage = ChoosingPrefix selected } as popup) ->
      match branchPrefixes |> List.tryItem selected with
      | None -> model, []
      | Some prefix ->
        {
          model with
              InputMode =
                BranchPopup {
                  popup with
                      Prefix = prefix
                      Stage = EditingName None
                }
        },
        []
    | BranchPopup({ Stage = EditingTicket _ } as popup) ->
      let trimmed = popup.Ticket.Trim()

      let error =
        match trimmed with
        | "" -> Some "Ticket number required"
        | _ when trimmed |> Seq.forall Char.IsDigit |> not -> Some "Digits only"
        | _ -> None

      match error with
      | Some _ ->
        {
          model with
              InputMode =
                BranchPopup {
                  popup with
                      Stage = EditingTicket error
                }
        },
        []
      | None ->
        {
          model with
              InputMode =
                BranchPopup {
                  popup with
                      Ticket = trimmed
                      Stage = ChoosingPrefix 0
                }
        },
        []
    | BranchPopup({ Stage = EditingName _ } as popup) ->
      let trimmed = popup.Name.Trim()

      match trimmed with
      | "" ->
        {
          model with
              InputMode =
                BranchPopup {
                  popup with
                      Stage = EditingName(Some "Name required")
                }
        },
        []
      | _ ->
        let named = { popup with Name = trimmed }

        {
          model with
              InputMode = BranchPopup { named with Stage = Submitting }
        },
        createBranchCmd popup.Base (branchNameOf named)
    // Retry after a failure: same parts, same branch.
    | BranchPopup({ Stage = CreateFailed _ } as popup) ->
      {
        model with
            InputMode = BranchPopup { popup with Stage = Submitting }
      },
      createBranchCmd popup.Base (branchNameOf popup)
    | _ -> model, []
  | BranchSelectUp -> moveBranchSelection -1 model, []
  | BranchSelectDown -> moveBranchSelection 1 model, []
  | DismissBranchPopup ->
    match model.InputMode with
    | BranchPopup _ -> { model with InputMode = Normal }, []
    | _ -> model, []
  | BranchCreateCompleted(name, Ok()) ->
    let updated = {
      model with
          InputMode = Normal
          GitBranch = name
          LocalGitBranch = Git.readCurrentBranch ()
    }

    updated, saveGitBranchCmd updated name
  | BranchCreateCompleted(_, Error err) ->
    // Keep the popup's parts as they are — a retry has to rebuild the same name.
    let inputMode =
      match model.InputMode with
      | BranchPopup popup -> BranchPopup { popup with Stage = CreateFailed err }
      | other -> other

    {
      model with
          InputMode = inputMode
          LocalGitBranch = Git.readCurrentBranch ()
    },
    []
  | BeginSwitchBranch ->
    match model.InputMode, isRepoOK model with
    | Normal, true ->
      {
        model with
            InputMode = SwitchPopup LoadingBranches
      },
      listBranchesCmd ()
    | _ -> model, []
  | BranchesLoaded result ->
    match model.InputMode with
    | SwitchPopup LoadingBranches ->
      match result with
      | Error err ->
        {
          model with
              InputMode = SwitchPopup(SwitchFailed err)
        },
        []
      | Ok [] ->
        {
          model with
              InputMode = SwitchPopup(SwitchFailed "no branches found")
        },
        []
      | Ok branches ->
        // Open on the session branch when it is in the list, so a reflexive Enter is
        // a no-op rather than a switch the user did not pick.
        let selected =
          branches
          |> List.tryFindIndex (fun branch -> branch.Name = model.GitBranch)
          |> Option.defaultValue 0

        {
          model with
              InputMode =
                SwitchPopup(
                  ChoosingBranch {
                    All = branches
                    Filter = ""
                    Selected = selected
                  }
                )
        },
        []
    | _ -> model, []
  | SwitchFilterChar c ->
    match model.InputMode with
    | SwitchPopup(ChoosingBranch choice) -> withFilter (choice.Filter + string c) model, []
    | _ -> model, []
  | SwitchFilterText text ->
    match model.InputMode with
    | SwitchPopup(ChoosingBranch choice) ->
      // The filter is one line; drop any pasted newlines.
      let cleaned = text.Replace("\r", "").Replace("\n", "")
      withFilter (choice.Filter + cleaned) model, []
    | _ -> model, []
  | SwitchFilterBackspace ->
    match model.InputMode with
    | SwitchPopup(ChoosingBranch choice) -> withFilter (Str.dropLast choice.Filter) model, []
    | _ -> model, []
  | SwitchSelectUp -> moveSwitchSelection -1 model, []
  | SwitchSelectDown -> moveSwitchSelection 1 model, []
  | ChooseBranch ->
    match model.InputMode with
    | SwitchPopup(ChoosingBranch choice) ->
      match BranchChoice.visible choice |> List.tryItem choice.Selected with
      | None -> model, []
      // Try the plain checkout first, whatever the working tree looks like: git is
      // happy to carry local changes across as long as none of them are in the way.
      // Only if it refuses do we come back and ask about stashing.
      | Some branch ->
        {
          model with
              InputMode = SwitchPopup(SwitchingTo branch)
        },
        switchBranchCmd None branch
    | _ -> model, []
  | SwitchWithPolicy policy ->
    match model.InputMode with
    | SwitchPopup(ConfirmDirty(branch, _)) ->
      {
        model with
            InputMode = SwitchPopup(SwitchingTo branch)
      },
      switchBranchCmd (Some policy) branch
    | _ -> model, []
  | SwitchCompleted(_, Error failure) ->
    let stage =
      match failure.BlockedByLocalChanges, model.InputMode with
      | true, SwitchPopup(SwitchingTo branch) -> ConfirmDirty(branch, Git.dirtyFileCount ())
      | _ -> SwitchFailed failure.Message

    {
      model with
          InputMode = SwitchPopup stage
          LocalGitBranch = Git.readCurrentBranch ()
    },
    []
  | SwitchCompleted(name, Ok outcome) ->
    let notes = [
      match outcome.PopConflict with
      | true -> yield "stash pop conflicted — resolve the files; the stash entry is still on the stack"
      | false -> ()

      match outcome.Stashed && not outcome.Carried with
      | true -> yield "your changes are waiting in the stash — pop them when you are ready"
      | false -> ()

      match outcome.PullError with
      | Some err -> yield sprintf "could not fast-forward after the switch: %s" err
      | None -> ()
    ]

    let updated = {
      model with
          GitBranch = name
          LocalGitBranch = Git.readCurrentBranch ()
          InputMode =
            match notes with
            | [] -> Normal
            | _ -> SwitchPopup(SwitchedWithNotes notes)
    }

    // The checkout succeeded either way, so the session branch moves even when
    // there are notes to show.
    updated, saveGitBranchCmd updated name
  | DismissSwitchPopup ->
    match model.InputMode with
    | SwitchPopup _ -> { model with InputMode = Normal }, []
    | _ -> model, []
  | BeginSync ->
    match model.InputMode, isRepoOK model with
    | Normal, true ->
      let onSessionBranch = model.LocalGitBranch = model.GitBranch

      {
        model with
            InputMode = SyncPopup RunningSync
      },
      syncCmd onSessionBranch model.GitBranch
    | _ -> model, []
  | BeginWipSync ->
    match model.InputMode, isRepoOK model && model.LocalGitBranch = model.GitBranch with
    | Normal, true ->
      {
        model with
            InputMode = SyncPopup RunningSync
      },
      wipSyncCmd model.SessionTitle
    | _ -> model, []
  | SyncCompleted(Ok Git.Synced) ->
    {
      model with
          InputMode = Normal
          LocalGitBranch = Git.readCurrentBranch ()
    },
    []
  | SyncCompleted(Ok(Git.Diverged(ahead, behind))) ->
    let message = sprintf "Diverged from origin (rebased/amended): %d local commit(s), %d on origin." ahead behind

    {
      model with
          InputMode = SyncPopup(SyncDiverged message)
          LocalGitBranch = Git.readCurrentBranch ()
    },
    []
  | SyncCompleted(Error err) ->
    {
      model with
          InputMode = SyncPopup(SyncFailed err)
          LocalGitBranch = Git.readCurrentBranch ()
    },
    []
  | DiscardLocalAndPull ->
    match model.InputMode with
    | SyncPopup(SyncDiverged _) ->
      {
        model with
            InputMode = SyncPopup DiscardingLocal
      },
      resetToUpstreamCmd ()
    | _ -> model, []
  | DiscardCompleted(Ok()) ->
    {
      model with
          InputMode = Normal
          LocalGitBranch = Git.readCurrentBranch ()
    },
    []
  | DiscardCompleted(Error err) ->
    {
      model with
          InputMode = SyncPopup(SyncFailed err)
          LocalGitBranch = Git.readCurrentBranch ()
    },
    []
  | WipSyncCompleted(Ok()) ->
    let timestamp = nowMs ()

    let updated = {
      model with
          InputMode = Normal
          LocalGitBranch = Git.readCurrentBranch ()
          LastSeenWipAt = timestamp
    }

    updated, saveWipPushCmd updated timestamp
  | WipSyncCompleted(Error err) ->
    {
      model with
          InputMode = SyncPopup(SyncFailed err)
          LocalGitBranch = Git.readCurrentBranch ()
    },
    []
  | DismissSyncPopup ->
    match model.InputMode with
    | SyncPopup _ -> { model with InputMode = Normal }, []
    | _ -> model, []
  | MaybeShowGoalPopup ->
    match model.InputMode, model.GoalContent.Trim() = "", isLockedByOther model with
    | Normal, true, false ->
      let lock = {
        Owner = model.User
        LockedAt = nowMs ()
      }

      // Single-line editor for the popup (goal is empty when this opens).
      let editor =
        textBox model.GoalContent
        |> withMode TextBoxMode.SingleLine
        |> withPlaceholder "what are you working on?"
        |> focused

      editor.MoveToEnd()

      let updated = {
        model with
            InputMode = GoalPopup
            Lock = Some lock
            Editor = Some editor
      }

      updated, saveGoalLockCmd updated
    | _ -> model, []
  | CloseGoalPopup ->
    match model.InputMode with
    | GoalPopup ->
      let bumped = model.GoalSaveToken + 1

      let syncedContent =
        match model.Editor with
        | Some editor -> editor.Text
        | None -> model.GoalContent

      let updated = {
        model with
            InputMode = Normal
            GoalContent = syncedContent
            GoalSaveToken = bumped
            Editor = None
            Lock = None
      }

      updated, Cmd.batch [ saveGoalCmd updated; releaseGoalLockCmd updated ]
    | _ -> model, []

let subscriptions (_model: Model) = []

let private infoLayout =
  layout "session-info"
  |> splitHorizontally [|
    layout "goal"
    layout "repo" |> withFixedSize (Some 1)
    layout "branch" |> withFixedSize (Some 1)
    layout "started" |> withFixedSize (Some 1)
  |]

let private popupInnerLayout =
  layout "popup-inner"
  |> splitHorizontally [| layout "input" |> withFixedSize (Some 1); layout "status" |]

let private hintLine (text: string) : IWidget =
  paragraph [ Text.line [ Text.styledSpan (Nullable(Style Color.Grey)) text ] ] :> IWidget

let private baseName (model: Model) =
  function
  | Git.FromHead -> model.LocalGitBranch
  | Git.FromBranch branch -> branch

// Key hints spelled out in a dialog body, one per row and formatted the way the
// footer help bar formats them: a bold [Key] followed by its description.
let private keyHintLines (bindings: KeyBinding<Model, Msg> list) (model: Model) =
  KeyBinding.helpEntries bindings model
  |> List.map (fun (key, description) ->
    Text.line [
      Text.styledSpan (Nullable(Style(Color.Grey, decoration = Decoration.Bold))) (sprintf "  [%s]" key)
      Text.styledSpan (Nullable(Style Color.Grey)) (sprintf ":%s" description)
    ])

// A plain row in the branch-creation wizard's choice lists. The list widget keeps a
// gutter for the chevron, so the label needs no marker of its own.
type private ChoiceItem(label: string) =
  interface IListWidgetItem with
    member _.CreateText(isSelected) =
      let style =
        match isSelected with
        | true -> Style(Color.Black, Color.Green)
        | false -> Style Color.Green

      Text(LineExtensions.FromString(label, style))

let private choiceList (selected: int) (labels: string list) : IWidget =
  list (labels |> List.map ChoiceItem)
  |> withSelectedIndex (Some selected)
  |> withHighlightSymbol (LineExtensions.FromString("> ", Style Color.Green))
  |> wrapAround
  :> IWidget

// Border, the input row and the status rows below it — the prefix list is the only
// stage that needs more than a couple of those.
let private branchPopupRows =
  function
  | ChoosingBase(bases, _) -> 3 + List.length bases
  | ChoosingPrefix _ -> 3 + List.length branchPrefixes
  | _ -> 6

let private renderBranchPopup (model: Model) (popup: BranchPopup) : IWidget =
  let field (text: string) (placeholder: string) : IWidget =
    textBox text
    |> withMode TextBoxMode.SingleLine
    |> withPlaceholder placeholder
    |> focused
    |> withCursorAtEnd
    :> IWidget

  let inputWidget: IWidget =
    match popup.Stage with
    | ChoosingBase _ -> ofString "  Branch off:" :> IWidget
    | EditingTicket _ -> field popup.Ticket "ticket number…"
    // Both earlier answers, so the list is picked with the whole name in view.
    | ChoosingPrefix _ ->
      ofString (sprintf "  off %s · EWK-%s · prefix:" (baseName model popup.Base) popup.Ticket) :> IWidget
    | EditingName _ -> field popup.Name "name…"
    | Submitting -> ofString (sprintf "  Creating %s…" (branchNameOf popup)) :> IWidget
    | CreateFailed _ -> ofString (sprintf "  %s" (branchNameOf popup)) :> IWidget

  // What has been answered so far, so the parts already chosen stay in sight while
  // the next one is typed.
  let answered =
    match popup.Stage with
    | EditingTicket _ -> sprintf "  off %s · EWK-…" (baseName model popup.Base)
    | EditingName _ -> sprintf "  %s/EWK-%s-…" popup.Prefix popup.Ticket
    | _ -> ""

  let statusWidget: IWidget =
    match popup.Stage with
    | EditingTicket(Some err)
    | EditingName(Some err) -> paragraph [ Text.line [ Text.styledSpan (Nullable(Style Color.Red)) err ] ] :> IWidget
    | EditingTicket None
    | EditingName None -> hintLine answered
    | ChoosingBase(bases, selected) -> choiceList selected (bases |> List.map fst)
    | ChoosingPrefix selected -> choiceList selected branchPrefixes
    | Submitting -> ofString "" :> IWidget
    | CreateFailed err -> paragraph [ Text.line [ Text.styledSpan (Nullable(Style Color.Red)) err ] ] :> IWidget

  let inner =
    { new IWidget with
        member _.Render(innerCtx) =
          let port = getPort innerCtx.Viewport popupInnerLayout
          innerCtx.Render(inputWidget, port "input")
          innerCtx.Render(statusWidget, port "status")
    }

  box (Look.fromColor Color.Green)
  |> withTitle "New branch"
  |> withInnerWidget inner
  :> IWidget

let private renderGoalPopup (inputWidget: IWidget) : IWidget =
  let inner =
    { new IWidget with
        member _.Render(innerCtx) =
          let port = getPort innerCtx.Viewport popupInnerLayout
          innerCtx.Render(inputWidget, port "input")
          innerCtx.Render(ofString "" :> IWidget, port "status")
    }

  box (Look.fromColor Color.Green)
  |> withTitle "Session goal"
  |> withInnerWidget inner
  :> IWidget

// Picker rows: remote-only branches are dimmed and tagged so it is obvious a
// checkout will create a local tracking branch; the branch already checked out is
// marked the way `git branch` marks it.
type private BranchListItem(branch: Git.BranchRef, isCurrent: bool) =
  interface IListWidgetItem with
    member _.CreateText(isSelected) =
      let marker =
        match isCurrent with
        | true -> "* "
        | false -> "  "

      let label =
        match branch.IsLocal with
        | true -> marker + branch.Name
        | false -> marker + branch.Name + "  (origin)"

      let color =
        match branch.IsLocal with
        | true -> Color.Green
        | false -> Color.Grey

      let style =
        match isSelected with
        | true -> Style(Color.Black, color)
        | false -> Style(color)

      Text(LineExtensions.FromString(label, style))

let private switchPopupInnerLayout =
  layout "switch-inner"
  |> splitHorizontally [| layout "filter" |> withFixedSize (Some 1); layout "list" |> withRatio 1 |]

let private errorLine (text: string) : IWidget =
  paragraph [ Text.line [ Text.styledSpan (Nullable(Style Color.Red)) text ] ] :> IWidget

// Wraps `text` to `width` and renders it as indented, styled lines. A Paragraph
// line holds a single span, and the built-in wrapper only breaks *between* spans —
// so without this a long git error hard-breaks mid-word at the frame.
let private wrappedLines (color: Color) (width: int) (text: string) =
  Str.wrap (width - 2) text
  |> List.map (fun line -> Text.line [ Text.styledSpan (Nullable(Style color)) ("  " + line) ])

// Returns the popup body plus the number of rows it wants, so the caller can size
// the popup against the space it actually has.
let private switchPopupBody (model: Model) (stage: SwitchPopupStage) (width: int) : IWidget * int * Color =
  match stage with
  | LoadingBranches -> ofString "  Loading branches…" :> IWidget, 4, Color.Green
  | SwitchingTo branch -> ofString (sprintf "  Switching to %s…" branch.Name) :> IWidget, 4, Color.Green
  | SwitchFailed err ->
    let lines = wrappedLines Color.Red width err
    paragraph lines :> IWidget, List.length lines + 3, Color.Red
  | SwitchedWithNotes notes ->
    let lines = notes |> List.collect (wrappedLines Color.Yellow width)
    paragraph lines :> IWidget, List.length lines + 3, Color.Yellow
  | ConfirmDirty(branch, changes) ->
    let message = wrappedLines Color.Red width (sprintf "%d uncommitted change(s) in the way of %s" changes branch.Name)

    let keys = keyHintLines switchDirtyBindings model

    let lines = message @ [ Text.line [] ] @ keys
    paragraph lines :> IWidget, List.length lines + 3, Color.Red
  | ChoosingBranch choice ->
    let visible = BranchChoice.visible choice

    let items =
      visible
      |> List.map (fun branch -> BranchListItem(branch, branch.Name = model.LocalGitBranch))

    let filterWidget =
      match choice.Filter with
      | "" -> hintLine "  type to filter"
      | filter -> ofString (sprintf "  filter: %s" filter) :> IWidget

    let listWidget =
      match items with
      | [] -> hintLine "  no branch matches"
      | _ ->
        list items
        |> withSelectedIndex (Some choice.Selected)
        |> withHighlightSymbol (LineExtensions.FromString("> ", Style Color.Green))
        |> wrapAround
        :> IWidget

    let body =
      { new IWidget with
          member _.Render(innerCtx) =
            let port = getPort innerCtx.Viewport switchPopupInnerLayout
            innerCtx.Render(filterWidget, port "filter")
            innerCtx.Render(listWidget, port "list")
      }

    // Border and filter row on top of one row per branch. Arrow keys and Enter need
    // no spelling out in a list this plain; the footer still carries them.
    body, List.length items + 4, Color.Green

// A PopupWidget is clamped to its host viewport, and the info panel is only eight
// rows tall — so SessionView renders these as work-area overlays instead, where the
// branch lists have room to breathe.
let branchOverlay (model: Model) : IWidget option =
  match model.InputMode with
  | SwitchPopup stage ->
    Some(
      { new IWidget with
          member _.Render(ctx) =
            // Body is built here rather than up front, because wrapping needs the
            // width the overlay actually gets.
            let width = min (ctx.Viewport.Width - 4) 72
            let body, wanted, frameColor = switchPopupBody model stage (width - 2)
            let height = min (ctx.Viewport.Height - 2) wanted

            let framed =
              box (Look.fromColor frameColor)
              |> withTitle "Change branch"
              |> withInnerWidget body
              :> IWidget

            ctx.Render(popup width height |> withPopupContent framed :> IWidget)
      }
    )
  | BranchPopup branchState ->
    Some(
      { new IWidget with
          member _.Render(ctx) =
            let width = min (ctx.Viewport.Width - 4) 60
            let height = min (ctx.Viewport.Height - 2) (branchPopupRows branchState.Stage)

            ctx.Render(popup width height |> withPopupContent (renderBranchPopup model branchState) :> IWidget)
      }
    )
  | _ -> None

let private renderSyncPopup (stage: SyncPopupStage) (target: string) : IWidget =
  let body: IWidget =
    match stage with
    | RunningSync -> ofString (sprintf "  Syncing %s…" target) :> IWidget
    | DiscardingLocal -> ofString "  Discarding local changes…" :> IWidget
    | SyncDiverged message -> paragraph [ Text.line [ Text.styledSpan (Nullable(Style Color.Red)) message ] ] :> IWidget
    | SyncFailed err -> paragraph [ Text.line [ Text.styledSpan (Nullable(Style Color.Red)) err ] ] :> IWidget

  // Frame red for problem states, green for in-progress.
  let frameColor =
    match stage with
    | SyncDiverged _
    | SyncFailed _ -> Color.Red
    | RunningSync
    | DiscardingLocal -> Color.Green

  box (Look.fromColor frameColor)
  |> withTitle "Sync branch"
  |> withInnerWidget body
  :> IWidget

let widget (model: Model) : IWidget =
  { new IWidget with
      member _.Render(ctx) =
        let port = getPort ctx.Viewport infoLayout

        let goalWidget =
          match model.InputMode, model.Editor with
          | Insert, Some editor -> editor :> IWidget
          | _ ->
            textBox model.GoalContent
            |> withMode TextBoxMode.MultiLine
            |> TextBoxes.withLook goalLook
            |> unfocused
            :> IWidget

        ctx.Render(goalWidget, View.padWith (View.padding 1 0 0 0) (port "goal"))

        let sessionRepoDisplay =
          match model.GitRepo with
          | "" -> "(unknown)"
          | name -> name

        let repoLine =
          match model.LocalRepo, model.GitRepo with
          | "", _ -> sprintf "  Repo:    %s [red](NO REPOSITORY)[/]" sessionRepoDisplay
          | _, "" -> sprintf "  Repo:    %s" sessionRepoDisplay
          | local, session when local = session -> sprintf "  Repo:    %s" sessionRepoDisplay
          | local, _ -> sprintf "  Repo:    %s [red](%s)[/]" sessionRepoDisplay local

        ctx.Render(ofMarkup repoLine :> IWidget, port "repo")

        let branchLine =
          match isRepoOK model with
          | false -> ""
          | true ->
            match model.LocalGitBranch = "" || model.LocalGitBranch = model.GitBranch with
            | true -> sprintf "  Branch:  %s" model.GitBranch
            | false -> sprintf "  Branch:  %s [red](%s)[/]" model.GitBranch model.LocalGitBranch

        ctx.Render(ofMarkup branchLine :> IWidget, port "branch")

        let startedAt = DateTimeOffset.FromUnixTimeMilliseconds(model.StartedAt).ToString("yyyy-MM-dd HH:mm:ss")
        ctx.Render(ofString (sprintf "  Started: %s" startedAt) :> IWidget, port "started")

        match model.InputMode with
        | SyncPopup stage ->
          ctx.Render(popup 60 5 |> withPopupContent (renderSyncPopup stage model.GitBranch) :> IWidget)
        | GoalPopup ->
          let input =
            match model.Editor with
            | Some editor -> editor :> IWidget
            | None ->
              textBox model.GoalContent
              |> withMode TextBoxMode.SingleLine
              |> withPlaceholder "what are you working on?"
              |> focused
              |> withCursorAtEnd
              :> IWidget

          ctx.Render(popup 60 5 |> withPopupContent (renderGoalPopup input) :> IWidget)
        // The branch dialogs are drawn by SessionView as work-area overlays, since a
        // popup nested here would be clamped to this panel's eight rows.
        | BranchPopup _
        | SwitchPopup _
        | Normal
        | Insert -> ()
  }
