module Config

open System
open System.IO
open System.Text.Json

[<CLIMutable>]
type ConfigFile = {
  FirebaseUrl: string
  FirebaseApiKey: string
  FirebaseAuthDomain: string
  Email: string
  Password: string
  NotificationsEnabled: Nullable<bool>
}

type Settings = {
  FirebaseUrl: string
  FirebaseApiKey: string
  FirebaseAuthDomain: string
  // Optional credentials for silent (non-interactive) sign-in. Both present →
  // authenticate at startup; otherwise the app shows the interactive login.
  Credentials: (string * string) option
  NotificationsEnabled: bool
}

let private configPath () =
  Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ApplicationData, "tuigether", "config.json")

let private templateJson =
  """{
  "firebaseUrl": "",
  "firebaseApiKey": "",
  "firebaseAuthDomain": "",
  "email": "",
  "password": "",
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

let load () : Result<Settings, string> =
  let path = configPath ()

  match tryReadFile path with
  | None ->
    writeTemplate path

    Error(
      sprintf
        "Created config template at %s. Fill it in, or set FIREBASE_URL, FIREBASE_API_KEY, and FIREBASE_AUTH_DOMAIN."
        path
    )
  | Some file ->
    let firebaseUrl = resolve "FIREBASE_URL" file.FirebaseUrl
    let firebaseApiKey = resolve "FIREBASE_API_KEY" file.FirebaseApiKey
    let firebaseAuthDomain = resolve "FIREBASE_AUTH_DOMAIN" file.FirebaseAuthDomain

    let email = resolve "TUIGETHER_EMAIL" file.Email
    let password = resolve "TUIGETHER_PASSWORD" file.Password

    let credentials =
      match email, password with
      | Some e, Some p -> Some(e, p)
      | _ -> None

    match firebaseUrl, firebaseApiKey, firebaseAuthDomain with
    | Some url, Some apiKey, Some authDomain ->
      Ok {
        FirebaseUrl = url
        FirebaseApiKey = apiKey
        FirebaseAuthDomain = authDomain
        Credentials = credentials
        NotificationsEnabled = file.NotificationsEnabled |> Option.ofNullable |> Option.defaultValue true
      }
    | _ ->
      Error(
        sprintf
          "Missing required config. Set values in %s or as FIREBASE_URL, FIREBASE_API_KEY, and FIREBASE_AUTH_DOMAIN environment variables."
          path
      )
