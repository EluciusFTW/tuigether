module DriveLog

open System
open Elmish
open Firebase.Database
open Spectre.Console
open Spectre.Tui
open SpectreTuff
open SpectreTuff.Layout
open SpectreTuff.Widgets

// Read-only view over the append-only drive history. Visibility is owned by
// SessionView (a toggled overlay); this module owns the data, the segment
// reconstruction, and the rendering of the overlay's inner content.

type Persistence = { Client: FirebaseClient; SessionId: string }

type Model = {
  Events: Session.DriveEvent list
  SelectedIndex: int
  Persistence: Persistence
}

type Msg =
  | RemoteLoaded of System.Collections.Generic.Dictionary<string, Session.DriveEvent> option
  | Up
  | Down

let init (client: FirebaseClient) (sessionId: string) = {
  Events = []
  SelectedIndex = 0
  Persistence = { Client = client; SessionId = sessionId }
}

let update msg model =
  match msg with
  | RemoteLoaded(Some dict) ->
    let events =
      match isNull (dict :> obj) with
      | true -> []
      | false ->
        dict
        |> Seq.sortBy (fun kvp -> kvp.Key) // push keys sort chronologically
        |> Seq.map (fun kvp -> kvp.Value)
        |> Seq.filter (fun e -> not (isNull (e :> obj)))
        |> Seq.toList

    // Follow the tail so the newest / live drive is in view on each new event.
    {
      model with
          Events = events
          SelectedIndex = max 0 (events.Length - 1)
    },
    []
  | RemoteLoaded None -> { model with Events = []; SelectedIndex = 0 }, []
  | Up ->
    {
      model with
          SelectedIndex = max 0 (model.SelectedIndex - 1)
    },
    []
  | Down ->
    {
      model with
          SelectedIndex = min (max 0 (model.Events.Length - 1)) (model.SelectedIndex + 1)
    },
    []

let subscriptions (model: Model) =
  Firebase.History.subscription model.Persistence.Client model.Persistence.SessionId RemoteLoaded

// ─── Segment reconstruction ──────────────────────────────────────────────────

type private Segment = {
  Driver: string
  StartAt: int64
  EndAt: int64 option // None = still driving (live)
  Outcome: string
  ActiveMs: int64 // net driving time = gross span minus paused intervals
  CurrentlyPaused: bool
}

type private OpenSeg = {
  Driver: string
  StartAt: int64
  PausedAccum: int64 // paused time already tallied within this segment
  PauseStart: int64 option // start of an in-progress pause, if any
}

// Walk events in order keeping a single open segment. `Started` opens one (closing
// any dangling open defensively as `switched`); `Paused`/`Resumed` accumulate paused
// time without ending the segment; every other event closes it with its type as the
// outcome. The still-open segment at the end is the live drive. Net driving time
// subtracts paused intervals (a pause open at close/now counts up to that instant, so
// the displayed duration freezes while paused).
let private reconstruct (nowMs: int64) (events: Session.DriveEvent list) : Segment list =
  let mutable acc = []
  let mutable openSeg: OpenSeg option = None

  let pausedTotal (o: OpenSeg) (upTo: int64) =
    o.PausedAccum
    + (match o.PauseStart with
       | Some p -> max 0L (upTo - p)
       | None -> 0L)

  let close (o: OpenSeg) (endAt: int64) (outcome: string) = {
    Driver = o.Driver
    StartAt = o.StartAt
    EndAt = Some endAt
    Outcome = outcome
    ActiveMs = max 0L (endAt - o.StartAt - pausedTotal o endAt)
    CurrentlyPaused = false
  }

  for ev in events do
    let t = ev.At

    match Session.DriveEventType.fromString ev.Type with
    | Session.DriveEventType.Started ->
      match openSeg with
      | Some o -> acc <- close o t "switched" :: acc
      | None -> ()

      openSeg <-
        Some {
          Driver = ev.Driver
          StartAt = t
          PausedAccum = 0L
          PauseStart = None
        }
    | Session.DriveEventType.Paused ->
      match openSeg with
      | Some o when o.PauseStart.IsNone -> openSeg <- Some { o with PauseStart = Some t }
      | _ -> ()
    | Session.DriveEventType.Resumed ->
      match openSeg with
      | Some o ->
        match o.PauseStart with
        | Some p ->
          openSeg <-
            Some {
              o with
                  PausedAccum = o.PausedAccum + max 0L (t - p)
                  PauseStart = None
            }
        | None -> ()
      | None -> ()
    | other ->
      match openSeg with
      | Some o ->
        acc <- close o t ((Session.DriveEventType.toString other).ToLower()) :: acc
        openSeg <- None
      | None -> ()

  match openSeg with
  | Some o ->
    let currentlyPaused = o.PauseStart.IsSome

    acc <-
      {
        Driver = o.Driver
        StartAt = o.StartAt
        EndAt = None
        Outcome = (if currentlyPaused then "paused" else "live")
        ActiveMs = max 0L (nowMs - o.StartAt - pausedTotal o nowMs)
        CurrentlyPaused = currentlyPaused
      }
      :: acc
  | None -> ()

  List.rev acc

// ─── Rendering ───────────────────────────────────────────────────────────────

let private formatTimeOfDay (ms: int64) =
  DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm")

let private formatDuration (millis: int64) =
  let totalSeconds = max 0L (millis / 1000L)
  let hours = totalSeconds / 3600L
  let minutes = (totalSeconds % 3600L) / 60L

  match hours, minutes with
  | 0L, 0L -> "<1m"
  | 0L, _ -> sprintf "%dm" minutes
  | _ -> sprintf "%dh %dm" hours minutes

let private outcomeColor (outcome: string) =
  match outcome with
  | "live" -> Color.Green
  | "paused" -> Color.Yellow
  | "finished" -> Color.Grey
  | "stopped" -> Color.Yellow
  | "skipped" -> Color.Orange1
  | _ -> Color.Aqua

type private SegmentListItem(text: string, color: Color) =
  interface IListWidgetItem with
    member _.CreateText(_isSelected) =
      Text(LineExtensions.FromString(text, Style color))

let private innerLayout =
  layout "drivelog"
  |> splitHorizontally [|
    layout "list"
    layout "totals" |> withFixedSize (Some 2)
    layout "hint" |> withFixedSize (Some 1)
  |]

let widget (model: Model) : IWidget =
  let nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
  let segments = reconstruct nowMs model.Events

  let listWidget: IWidget =
    match segments with
    | [] -> ofString "No drives yet." :> IWidget
    | _ ->
      let items =
        segments
        |> List.map (fun seg ->
          let endStr =
            match seg.EndAt with
            | Some e -> formatTimeOfDay e
            | None -> "…"

          let text =
            sprintf
              "%-16s %s–%-5s %7s   %s"
              seg.Driver
              (formatTimeOfDay seg.StartAt)
              endStr
              (formatDuration seg.ActiveMs)
              seg.Outcome

          SegmentListItem(text, outcomeColor seg.Outcome))

      list items
      |> withSelectedIndex (Some model.SelectedIndex)
      |> withHighlightSymbol (LineExtensions.FromString("> ", Style Color.White))
      :> IWidget

  let totalsWidget: IWidget =
    let totals =
      segments
      |> List.groupBy (fun s -> s.Driver)
      |> List.map (fun (driver, segs) -> driver, segs |> List.sumBy (fun s -> s.ActiveMs))
      |> List.sortByDescending snd

    let totalsText =
      match totals with
      | [] -> ""
      | _ ->
        totals
        |> List.map (fun (driver, ms) -> sprintf "%s %s" driver (formatDuration ms))
        |> String.concat "  ·  "

    paragraph [
      Text.line [ Text.styledSpan (Nullable(Style Color.Grey)) (String.replicate 40 "─") ]
      Text.line [ Text.span (sprintf "Totals:  %s" totalsText) ]
    ]
    |> withOverflow Overflow.Ellipsis
    :> IWidget

  let hintWidget: IWidget =
    paragraph [ Text.line [ Text.styledSpan (Nullable(Style Color.Grey)) "[↑/↓] scroll   [esc] close" ] ]
    |> withOverflow Overflow.Ellipsis
    :> IWidget

  { new IWidget with
      member _.Render(ctx) =
        let port = getPort ctx.Viewport innerLayout
        ctx.Render(listWidget, port "list")
        ctx.Render(totalsWidget, port "totals")
        ctx.Render(hintWidget, port "hint")
  }
