module Config

open System
open System.IO
open System.Text.Json

[<CLIMutable>]
type ConfigFile = {
  Backend: string
  FirebaseUrl: string
  FirebaseSecret: string
  ServerUrl: string
  ServerApiKey: string
  TuigetherUser: string
  NotificationsEnabled: Nullable<bool>
}

type FirebaseSettings = { Url: string; Secret: string }

type ServerSettings = { Url: Uri; ApiKey: string }

type BackendSettings =
  | FirebaseMode of FirebaseSettings
  | ServerMode of ServerSettings

type Settings = {
  Backend: BackendSettings
  TuigetherUser: string
  NotificationsEnabled: bool
}

let private configPath () =
  Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ApplicationData, "tuigether", "config.json")

let private templateJson =
  """{
  "backend": "firebase",
  "firebaseUrl": "",
  "firebaseSecret": "",
  "serverUrl": "",
  "serverApiKey": "",
  "tuigetherUser": "",
  "notificationsEnabled": true
}
"""

let private writeTemplate (path: string) =
  Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
  File.WriteAllText(path, templateJson)

let private tryReadFile (path: string) =
  match File.Exists path with
  | false -> None
  | true ->
    let json = File.ReadAllText path
    let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
    Some(JsonSerializer.Deserialize<ConfigFile>(json, options))

let private resolve (environmentName: string) (fileValue: string) =
  match Environment.GetEnvironmentVariable environmentName with
  | value when not (String.IsNullOrWhiteSpace value) -> Some value
  | _ ->
    match fileValue with
    | value when not (String.IsNullOrWhiteSpace value) -> Some value
    | _ -> None

type private Setting = { Key: string; Environment: string }

let private backendSetting = {
  Key = "backend"
  Environment = "TUIGETHER_BACKEND"
}

let private firebaseUrlSetting = {
  Key = "firebaseUrl"
  Environment = "FIREBASE_URL"
}

let private firebaseSecretSetting = {
  Key = "firebaseSecret"
  Environment = "FIREBASE_SECRET"
}

let private serverUrlSetting = {
  Key = "serverUrl"
  Environment = "TUIGETHER_SERVER_URL"
}

let private serverApiKeySetting = {
  Key = "serverApiKey"
  Environment = "TUIGETHER_SERVER_API_KEY"
}

let private userSetting = {
  Key = "tuigetherUser"
  Environment = "TUIGETHER_USER"
}

let private describe (setting: Setting) =
  sprintf "%s (%s)" setting.Key setting.Environment

// Error carries one problem text per setting.
let private required (setting: Setting) (fileValue: string) : Result<string, string list> =
  match resolve setting.Environment fileValue with
  | Some value -> Ok value
  | None -> Error [ sprintf "%s is missing" (describe setting) ]

let private both (first: Result<'a, string list>) (second: Result<'b, string list>) =
  match first, second with
  | Ok a, Ok b -> Ok(a, b)
  | Error a, Error b -> Error(a @ b)
  | Error a, Ok _
  | Ok _, Error a -> Error a

let private serverUrl (value: string) : Result<Uri, string list> =
  match Uri.TryCreate(value, UriKind.Absolute) with
  | true, uri when uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps -> Ok uri
  | _ ->
    Error [
      sprintf "%s is not an http or https URL: %s" (describe serverUrlSetting) value
    ]

let private backend (file: ConfigFile) : Result<BackendSettings, string list> =
  match
    resolve backendSetting.Environment file.Backend
    |> Option.map _.Trim().ToLowerInvariant()
  with
  | None
  | Some "firebase" ->
    both (required firebaseUrlSetting file.FirebaseUrl) (required firebaseSecretSetting file.FirebaseSecret)
    |> Result.map (fun (url, secret) ->
      FirebaseMode {
        FirebaseSettings.Url = url
        Secret = secret
      })
  | Some "server" ->
    both
      (required serverUrlSetting file.ServerUrl |> Result.bind serverUrl)
      (required serverApiKeySetting file.ServerApiKey)
    |> Result.map (fun (url, apiKey) -> ServerMode { Url = url; ApiKey = apiKey })
  | Some other ->
    Error [
      sprintf "%s must be firebase or server, not %s" (describe backendSetting) other
    ]

let load () : Result<Settings, string> =
  let path = configPath ()

  match tryReadFile path with
  | None ->
    writeTemplate path

    Error(
      sprintf
        "Created config template at %s. Fill it in, or set FIREBASE_URL, FIREBASE_SECRET, and TUIGETHER_USER."
        path
    )
  | Some file ->
    match both (backend file) (required userSetting file.TuigetherUser) with
    | Ok(backend, user) ->
      Ok {
        Backend = backend
        TuigetherUser = user
        NotificationsEnabled = file.NotificationsEnabled |> Option.ofNullable |> Option.defaultValue true
      }
    | Error problems ->
      Error(
        sprintf
          "The config is incomplete. Set these values in %s or as environment variables:\n%s"
          path
          (problems |> List.map (sprintf "- %s") |> String.concat "\n")
      )
