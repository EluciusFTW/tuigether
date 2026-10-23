# Components

This doc tells you how a tuigether UI component is built and wired. Read it before you add or change a panel, a key
binding, or the panel wiring in `SessionView`. The architecture overview is in
[`src/README.md`](../../src/README.md).

## Shape

A component is one F# module in `src/Components/`. Copy `TodoList.fs` for the structure: `InputMode`, `Model`,
`Msg`, the input members (`handleKey`, `handlePaste`, `capturesInput`, `keyMap`), `init`, `update`,
`subscriptions`, `widget`. Copy `SessionList.handleKey` for the key handling (see [Keys](#keys)).

- `init` takes the backend from `Dependencies.Backend` and returns only the `Model`. The component's subscription on
  the backend seam does the first load, and its result arrives as `RemoteStateLoaded`. See
  [Backend seam](sync.md#backend-seam).
- `update` returns `Model * Cmd<Msg>`. A component that must tell its parent something returns
  `Model * Cmd<Msg> * OutMsg option` instead, as `SessionList` (`OpenSession`) and `SessionView` (`LeaveSession`)
  do. The parent matches the `OutMsg`.
- A write is a `Cmd.OfAsync.perform` that calls one intention of the component's seam group, for example
  `model.Backend.Todo.Add`, and returns `StateSaved`. Remote changes come back through the subscription, so
  `StateSaved` changes nothing. The component computes the values that the intention proposes, for example the new
  item id from `PushId.generate`.
- An editing `InputMode` case carries a live `TextBoxWidget`. `TextEditing.apply` mutates it in place, so the model
  keeps the same instance. Read the text back from the widget on confirm. `TextEditing.isMutation` tells you if an
  action needs a save.

## Keys

The current form: one `KeyBinding<Model, Msg> list` per input mode drives both behaviour and help text.
`KeyBinding.handleKey bindings key model` decodes the key, and `KeyBinding.toKeyMap bindings model` builds the help
bar and the `?` modal. A `KeyBinding.dynamic` binding with `Message = None` is disabled and hidden from help.
`KeyBinding.orKey` adds an alternative key to a binding.

The older form is in `TodoList`, `NoteList` and `Notes`: a hand-written `match` in `handleKey` next to a binding list
that only feeds the help. The two copies already differ: `TodoList.handleKey` accepts `j` and `k`, and its help does
not show them. When you change keys in one of these components, change both copies or move it to the current form.

An editing mode matches `Escape` and `Enter` first, then hands every other key to `TextEditing.keyToAction`.

A panel sees a key only after these handlers pass on it, in this order:

1. `Application`: while the `?` modal is open, it takes every key.
2. `SessionView`: while the journey log overlay is open, it takes every key.
3. A panel that captures input (`capturesInput` is `true`) gets every key next. `Ctrl+V` arrives as `handlePaste`.
4. `Application` global keys: `q`, `l`, `?`.
5. `SessionView` keys: `1`–`5` (focus), `Tab`, `Shift+Tab`, `Backspace`.
6. The focused panel. Panel 5 (Journey) uses `JourneyKeys`: `s`, `f`, `n`, `j`.

A Normal-mode panel key that is also in step 4 or 5 never reaches the panel.

## Add a panel to SessionView

`SessionView.fs` addresses panels by focus number. Each `match model.Focus with` ends in `| _ -> None`, so a missed
place compiles and loses the key, the paste, or the render at run time. Do all of these steps:

1. `src/Tuigether.fsproj`: add the new file above `Components/SessionView.fs`.
2. `Model`: add the child model field. `init`: call the child's `init`.
3. `Msg`: add `<Child>Msg of <Child>.Msg`. `update`: run the child's `update` and wrap its command with `Cmd.map`.
4. `subscriptions`: append `<Child>.subscriptions model.<Child> |> Subs.map <Child>Msg`.
5. Focus count: change `panelCount`, the modulus in the `Tab` binding (`model.Focus % 5 + 1`), and the `Shift+Tab`
   formula in `handleKey` (`(model.Focus + 3) % 5 + 1`).
6. Add the new focus number to `capturesInput`, to both focus matches in `handleKey`, to `handlePaste`, and to
   `keymapSections`.
7. `widget`: render the panel in a `focusableBox` with its number and focus state, and give it a slot in the layouts
   above `widget`.

Proof: build, then a human presses `Tab` through every panel and uses one key in each.
