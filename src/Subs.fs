module Subs

open System

// Re-tag a child component's subscriptions so its events dispatch as the parent's
// message type. Shared by every component that hosts children (Journey, SessionView,
// Application) — keeps the subscription-forwarding wiring identical and in one place.
let map (wrap: 'child -> 'parent) (subs: (string list * (('child -> unit) -> IDisposable)) list) =
  subs
  |> List.map (fun (key, start) -> key, (fun (dispatch: 'parent -> unit) -> start (wrap >> dispatch)))
