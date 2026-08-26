module Git

open System
open System.Diagnostics
open System.Text.RegularExpressions

let private runGitArgs (args: string list) : Result<string, string> =
  try
    let psi = ProcessStartInfo("git")

    for a in args do
      psi.ArgumentList.Add(a)

    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    let proc = Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()

    match proc.ExitCode = 0 with
    | true -> Ok(stdout.Trim())
    | false ->
      let combined = (stderr + stdout).Trim()

      match combined with
      | "" -> Error(sprintf "git exited with code %d" proc.ExitCode)
      | text -> Error text
  with ex ->
    Error ex.Message

let private runGit (args: string) : Result<string, string> =
  try
    let psi = ProcessStartInfo("git", args)
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    let proc = Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()

    match proc.ExitCode = 0 with
    | true -> Ok(stdout.Trim())
    | false ->
      let combined = (stderr + stdout).Trim()

      match combined with
      | "" -> Error(sprintf "git exited with code %d" proc.ExitCode)
      | text -> Error text
  with ex ->
    Error ex.Message

let readCurrentBranch () =
  match runGit "branch --show-current" with
  | Ok branch when branch <> "" -> branch
  | _ -> ""

let private stripGitSuffix (text: string) =
  match text.EndsWith(".git") with
  | true -> text.Substring(0, text.Length - 4)
  | false -> text

let private lastPathSegment (text: string) =
  match text.LastIndexOfAny([| '/'; ':' |]) with
  | -1 -> text
  | i -> text.Substring(i + 1)

let readRepoName () =
  let fromRemote =
    match runGit "config --get remote.origin.url" with
    | Ok url when url <> "" -> url.TrimEnd('/') |> stripGitSuffix |> lastPathSegment
    | _ -> ""

  match fromRemote with
  | "" ->
    match runGit "rev-parse --show-toplevel" with
    | Ok path when path <> "" -> System.IO.Path.GetFileName(path.TrimEnd('/'))
    | _ -> ""
  | name -> name

type BranchRef = {
  Name: string
  // Remote-only branches (on origin but not checked out here) are offered too, so a
  // session can be pointed at a branch a teammate just pushed.
  IsLocal: bool
}

let private originPrefix = "origin/"

let listBranches () : Async<Result<BranchRef list, string>> =
  async {
    return
      match
        runGitArgs [
          "for-each-ref"
          "--format=%(refname:short)"
          "refs/heads"
          "refs/remotes/origin"
        ]
      with
      | Error e -> Error e
      | Ok output ->
        let names =
          output.Split('\n')
          |> Array.map (fun line -> line.Trim())
          |> Array.filter (fun line -> line <> "")
          |> Array.toList

        let locals = names |> List.filter (fun name -> not (name.StartsWith originPrefix))
        let localSet = Set.ofList locals

        let remoteOnly =
          names
          |> List.filter (fun name -> name.StartsWith originPrefix)
          |> List.map (fun name -> name.Substring originPrefix.Length)
          |> List.filter (fun name -> name <> "HEAD" && not (localSet.Contains name))
          |> List.distinct

        Ok(
          (locals |> List.map (fun name -> { Name = name; IsLocal = true }))
          @ (remoteOnly |> List.map (fun name -> { Name = name; IsLocal = false }))
        )
  }

let private refExists (reference: string) =
  match runGitArgs [ "rev-parse"; "--verify"; "--quiet"; reference ] with
  | Ok _ -> true
  | Error _ -> false

// origin/HEAD is only set in a repo that was cloned — `git init` plus a push leaves
// it absent — so fall back to whichever conventional name the repo actually has, and
// to None when it has neither, so callers can skip offering a base that is not there.
let readDefaultBranch () : string option =
  match runGit "symbolic-ref --short refs/remotes/origin/HEAD" with
  | Ok head when head.StartsWith originPrefix -> Some(head.Substring originPrefix.Length)
  | _ ->
    [ "main"; "master" ]
    |> List.tryFind (fun name -> refExists name || refExists (originPrefix + name))

// Release branches follow `release/<major>.<minor>.<patch>`. Newest means highest
// version, not most recently touched, so the list does not shuffle when someone
// pushes a fix to an older release.
let private releasePattern = Regex(@"^release/(\d+)\.(\d+)\.(\d+)$")

let listReleaseBranches (count: int) : string list =
  let version (name: string) =
    let matched = releasePattern.Match name

    match matched.Success with
    | false -> None
    | true ->
      match
        Int32.TryParse matched.Groups[1].Value,
        Int32.TryParse matched.Groups[2].Value,
        Int32.TryParse matched.Groups[3].Value
      with
      | (true, major), (true, minor), (true, patch) -> Some(name, (major, minor, patch))
      | _ -> None

  match
    runGitArgs [
      "for-each-ref"
      "--format=%(refname:short)"
      "refs/heads/release"
      "refs/remotes/origin/release"
    ]
  with
  | Error _ -> []
  | Ok output ->
    output.Split('\n')
    |> Array.map (fun line -> line.Trim())
    |> Array.map (fun name ->
      match name.StartsWith originPrefix with
      | true -> name.Substring originPrefix.Length
      | false -> name)
    |> Array.distinct
    |> Array.choose version
    |> Array.sortByDescending snd
    |> Array.truncate count
    |> Array.map fst
    |> Array.toList

let fetchAndCheckout (name: string) : Async<Result<unit, string>> =
  async {
    return
      match runGit (sprintf "fetch origin %s" name) with
      | Error e -> Error e
      | Ok _ ->
        match runGit (sprintf "checkout %s" name) with
        | Ok _ -> Ok()
        | Error e -> Error e
  }

let private aheadBehind () : Result<int * int, string> =
  match runGit "rev-list --left-right --count @{upstream}...HEAD" with
  | Error e -> Error e
  | Ok output ->
    let parts = output.Split([| '\t'; ' ' |], StringSplitOptions.RemoveEmptyEntries)

    match parts.Length, parts with
    | 2, [| behindText; aheadText |] ->
      match Int32.TryParse behindText, Int32.TryParse aheadText with
      | (true, behind), (true, ahead) -> Ok(ahead, behind)
      | _ -> Error(sprintf "unexpected rev-list output: %s" output)
    | _ -> Error(sprintf "unexpected rev-list output: %s" output)

let private statusLines () =
  match runGit "status --porcelain" with
  | Ok output ->
    output.Split('\n')
    |> Array.map (fun line -> line.Trim())
    |> Array.filter (fun line -> line <> "")
    |> Array.toList
  | Error _ -> []

let isWorkingTreeDirty () =
  statusLines () |> List.isEmpty |> not

let dirtyFileCount () =
  statusLines () |> List.length

let wipSync (title: string) : Async<Result<unit, string>> =
  async {
    return
      match runGit "fetch" with
      | Error e -> Error e
      | Ok _ ->
        match aheadBehind () with
        | Error e -> Error e
        | Ok(_, behind) when behind > 0 -> Error "behind origin — sync first"
        | Ok(ahead, _) ->
          let dirty = isWorkingTreeDirty ()

          let commitStep =
            match dirty with
            | false -> Ok ""
            | true ->
              match runGit "add -A" with
              | Error e -> Error e
              | Ok _ -> runGitArgs [ "commit"; "-m"; sprintf "WIP: %s" title ]

          match commitStep with
          | Error e -> Error e
          | Ok _ ->
            match not dirty && ahead = 0 with
            | true -> Error "nothing to sync"
            | false ->
              match runGit "push" with
              | Ok _ -> Ok()
              | Error e -> Error e
  }

type SyncResult =
  | Synced
  // No ff path — origin rebased/amended. Counts for the prompt.
  | Diverged of ahead: int * behind: int

let syncCurrentBranch () : Async<Result<SyncResult, string>> =
  async {
    return
      match runGit "fetch" with
      | Error e -> Error e
      | Ok _ ->
        match aheadBehind () with
        | Error e -> Error e
        | Ok(ahead, behind) ->
          match ahead, behind with
          | _, 0 when ahead > 0 ->
            match runGit "push" with
            | Ok _ -> Ok Synced
            | Error e -> Error e
          | 0, _ when behind > 0 ->
            // --ff-only: no merge commit. Non-ff fails clean, tree untouched.
            match runGit "pull --ff-only" with
            | Ok _ -> Ok Synced
            | Error e -> Error e
          | 0, 0 -> Ok Synced
          | _ -> Ok(Diverged(ahead, behind))
  }

// Destructive: drops local commits + working changes. Confirm first.
let resetToUpstream () : Async<Result<unit, string>> =
  async {
    return
      match runGit "fetch" with
      | Error e -> Error e
      | Ok _ ->
        match runGit "reset --hard @{upstream}" with
        | Ok _ -> Ok()
        | Error e -> Error e
  }

// ─── Switching to the session branch ─────────────────────────────────────────

type DirtyPolicy =
  | StashAndCarry
  | StashAndLeave

type SwitchOutcome = {
  Stashed: bool
  Carried: bool
  // The switch itself succeeded; `git stash pop` hit a conflict the user must resolve.
  PopConflict: bool
  // A ff-only pull that failed after the checkout. Reported, not fatal.
  PullError: string option
}

let private hasUpstream () =
  match runGit "rev-parse --abbrev-ref --symbolic-full-name @{upstream}" with
  | Ok upstream -> upstream <> ""
  | Error _ -> false

type SwitchFailure = {
  Message: string
  // git refused the checkout itself because local changes would be clobbered — the
  // one failure where stashing first actually helps, so the caller can offer that
  // instead of treating it as fatal.
  BlockedByLocalChanges: bool
}

let private plainFailure (message: string) = {
  Message = message
  BlockedByLocalChanges = false
}

// git's own wording when a checkout would overwrite something. Anything else it
// refuses for (a bad ref, a broken index) stashing would not fix.
let private isBlockedByLocalChanges (message: string) =
  message.Contains("would be overwritten by", StringComparison.OrdinalIgnoreCase)
  || message.Contains("commit your changes or stash them", StringComparison.OrdinalIgnoreCase)

// `policy = None` means "do not touch the working tree" — the checkout is simply
// attempted. git allows one with local changes as long as none of them are in the
// way (untracked files usually are not), so it decides, and only its refusal sends
// the caller back to the user to ask about stashing.
let switchToBranch (policy: DirtyPolicy option) (branch: BranchRef) : Async<Result<SwitchOutcome, SwitchFailure>> =
  async {
    return
      match runGit "fetch" with
      | Error e -> Error(plainFailure e)
      | Ok _ ->
        let stashStep =
          match policy, isWorkingTreeDirty () with
          | None, _
          | _, false -> Ok false
          | Some _, true ->
            // -u so untracked files travel with the switch too.
            match
              runGitArgs [
                "stash"
                "push"
                "-u"
                "-m"
                sprintf "tuigether: switching to %s" branch.Name
              ]
            with
            | Error e -> Error(plainFailure e)
            | Ok _ -> Ok true

        match stashStep with
        | Error e -> Error e
        | Ok stashed ->
          let checkoutArgs =
            match branch.IsLocal with
            | true -> [ "checkout"; branch.Name ]
            | false -> [ "checkout"; "-t"; originPrefix + branch.Name ]

          match runGitArgs checkoutArgs with
          | Error e ->
            // Put the tree back the way we found it before reporting the failure.
            match stashed with
            | true -> runGit "stash pop" |> ignore
            | false -> ()

            Error {
              Message = e
              // Only worth offering a stash if we have not already tried one.
              BlockedByLocalChanges = not stashed && isBlockedByLocalChanges e
            }
          | Ok _ ->
            let pullError =
              match hasUpstream () with
              | false -> None
              | true ->
                match runGit "pull --ff-only" with
                | Ok _ -> None
                | Error e -> Some e

            match stashed, policy with
            | true, Some StashAndCarry ->
              let popConflict =
                match runGit "stash pop" with
                | Ok _ -> false
                | Error _ -> true

              Ok {
                Stashed = true
                Carried = true
                PopConflict = popConflict
                PullError = pullError
              }
            | stashed, _ ->
              Ok {
                Stashed = stashed
                Carried = false
                PopConflict = false
                PullError = pullError
              }
  }

type BranchBase =
  // Exactly where the working tree stands now, unpushed commits included.
  | FromHead
  // Whatever origin has for that branch, so a new branch off the default one does
  // not inherit a stale local copy of it.
  | FromBranch of branch: string

let createAndPushBranch (baseRef: BranchBase) (name: string) : Async<Result<unit, string>> =
  async {
    let resolved =
      match baseRef with
      | FromHead -> "HEAD"
      | FromBranch branch ->
        // Best-effort refresh so we branch off origin's latest; offline just falls
        // back to the local ref.
        let _ = runGitArgs [ "fetch"; "origin"; branch ]

        match refExists (originPrefix + branch) with
        | true -> originPrefix + branch
        | false -> branch

    return
      match runGitArgs [ "checkout"; "-b"; name; resolved ] with
      | Error e -> Error e
      | Ok _ ->
        match runGitArgs [ "push"; "-u"; "origin"; name ] with
        | Ok _ -> Ok()
        | Error e -> Error e
  }
