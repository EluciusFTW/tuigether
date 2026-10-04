# tuigether
_Make pair-programming seamless_

> [!WARNING]
> This TUI is very early stages, experimental and builds upon the very early experimental-stage [SpectreTuff](https://github.com/EluciusFTW/SpectreTuff), which builds upon [Spectre.Tui](https://github.com/spectreconsole/spectre.tui), which also still is under construction.
> Also, currently a good chunk of the code is vibe-coded during a 24h hackathon. It will be cleaned up in the near future


## Features

This is a very rough sketch of what the app can do currently.

### Current features
- Create / open / join sessions
- Driver concept, integrated timer, journey log of past drives
- Notes (freetext, lists), ToDo lists — items are editable, with cursor movement, word wrap and paste (`Ctrl+V`)
- System notifications
- Git integration: session repository and branch, create and switch branches, syncing between the participants
- Keymap overview (`?`)

### Planned features
- Login / authentication (:D)
- Project > Session hierarchy
- Metadata / Connection to ticketing systems

+ ... anything we encounter in the daily usage of this app that makes remote pair/mob programming more seamless!

## Installing tuigether

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). The scripts build a
single-file, framework-dependent executable and copy it onto your `PATH` as `tuigether`.

**Linux / macOS:**

```bash
./scripts/install.sh             # installs to ~/.local/bin
./scripts/install.sh /custom/dir # or a custom directory
```

**Windows (PowerShell):**

```powershell
scripts\install.ps1                      # installs to %LOCALAPPDATA%\Programs\tuigether
scripts\install.ps1 -InstallDir C:\tools # or a custom directory
```

If the chosen directory is not already on your `PATH`, the script prints how to add it.

## Configuration

On first run tuigether creates a template config file and exits:

- **Linux / macOS:** `~/.config/tuigether/config.json`
- **Windows:** `%APPDATA%\tuigether\config.json`

```json
{
  "firebaseUrl": "",
  "firebaseSecret": "",
  "tuigetherUser": "",
  "notificationsEnabled": true
}
```

`notificationsEnabled` turns system notifications on or off.

### Environment variables

Environment variables take precedence over the config file. The required values must be set in at least one of the two.

| Variable | Required | Default | Description |
| --- | --- | --- | --- |
| `FIREBASE_URL` | yes | — | Firebase Realtime Database URL the app connects to. |
| `FIREBASE_SECRET` | yes | — | Firebase auth secret. |
| `TUIGETHER_USER` | yes | — | Identifier shown to other participants in a session. |
| `TUIGETHER_AVATAR` | no | random pick | Preferred avatar name; falls back to random if unset or unknown. |
| `TUIGETHER_LOG_DIR` | no | `./logs` | Directory where daily log files are written. |
| `TUIGETHER_LOG_RETENTION_DAYS` | no | `14` | Days of log history to keep. Older files are deleted on startup. `0` keeps today only. |

For architecture notes and a deeper look at the git integration, see [src/README.md](src/README.md).

## License
Copyright © Guy Buss, Daniel Muckelbauer

tuigether is provided as-is under the MIT license.
See the LICENSE.md file included in the repository.
