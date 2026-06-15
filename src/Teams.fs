module Teams

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Collections.Generic

// Creating a Teams meeting needs a delegated Microsoft Graph token with the
// OnlineMeetings.ReadWrite scope. There is no ambient way for a standalone TUI
// to obtain one, so we run the OAuth2 device-code flow against the public
// "Microsoft Graph Command Line Tools" client (which is preauthorized for that
// scope). The first call requires a one-time browser login; the refresh token is
// then cached on disk so later calls acquire silently. Self-contained like Git.fs
// — only HttpClient + System.Text.Json, no extra NuGet dependency.

type DeviceCode = { VerificationUri: string; UserCode: string }

let private clientId =
  match Environment.GetEnvironmentVariable "TEAMS_CLIENT_ID" with
  | s when not (String.IsNullOrWhiteSpace s) -> s
  | _ -> "14d82eec-204b-4c2f-b7e8-296a70dab67e" // Microsoft Graph Command Line Tools (public client)

let private authority =
  match Environment.GetEnvironmentVariable "TEAMS_TENANT_ID" with
  | s when not (String.IsNullOrWhiteSpace s) -> sprintf "https://login.microsoftonline.com/%s" s
  | _ -> "https://login.microsoftonline.com/organizations"

let private scope = "https://graph.microsoft.com/OnlineMeetings.ReadWrite offline_access"

let private deviceCodeUrl = sprintf "%s/oauth2/v2.0/devicecode" authority
let private tokenUrl = sprintf "%s/oauth2/v2.0/token" authority
let private meetingsUrl = "https://graph.microsoft.com/v1.0/me/onlineMeetings"

let private http = new HttpClient()

let private nowSeconds () = DateTimeOffset.UtcNow.ToUnixTimeSeconds()

// ─── Token cache ─────────────────────────────────────────────────────────────

[<CLIMutable>]
type private TokenCache = {
  RefreshToken: string
  AccessToken: string
  ExpiresAt: int64 // unix seconds
}

let private cachePath () =
  Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ApplicationData, "tuigether", "teams-token.json")

let private readCache () : TokenCache option =
  try
    let path = cachePath ()

    match File.Exists path with
    | false -> None
    | true ->
      let json = File.ReadAllText path
      let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
      Some(JsonSerializer.Deserialize<TokenCache>(json, options))
  with _ ->
    None

let private writeCache (cache: TokenCache) =
  try
    let path = cachePath ()
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, JsonSerializer.Serialize cache)
    // Owner-only — the refresh token is plaintext (no libsecret dependency).
    try
      File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
    with _ ->
      ()
  with _ ->
    ()

// ─── JSON helpers ────────────────────────────────────────────────────────────

let private tryStr (body: string) (name: string) : string option =
  try
    use doc = JsonDocument.Parse body

    match doc.RootElement.TryGetProperty name with
    | true, el when el.ValueKind = JsonValueKind.String -> Some(el.GetString())
    | _ -> None
  with _ ->
    None

let private tryInt (body: string) (name: string) : int option =
  try
    use doc = JsonDocument.Parse body

    match doc.RootElement.TryGetProperty name with
    | true, el when el.ValueKind = JsonValueKind.Number -> Some(el.GetInt32())
    | true, el when el.ValueKind = JsonValueKind.String ->
      match Int32.TryParse(el.GetString()) with
      | true, v -> Some v
      | _ -> None
    | _ -> None
  with _ ->
    None

// Graph errors are nested: { "error": { "code", "message" } }.
let private graphError (body: string) : string =
  try
    use doc = JsonDocument.Parse body

    match doc.RootElement.TryGetProperty "error" with
    | true, errEl ->
      match errEl.TryGetProperty "message" with
      | true, m when m.ValueKind = JsonValueKind.String -> m.GetString()
      | _ -> body
    | _ -> body
  with _ ->
    body

let private postForm (url: string) (fields: (string * string) list) : Async<Result<int * string, string>> =
  async {
    try
      use content = new FormUrlEncodedContent(fields |> List.map KeyValuePair)
      let! resp = http.PostAsync(url, content) |> Async.AwaitTask
      let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
      return Ok(int resp.StatusCode, body)
    with ex ->
      return Error ex.Message
  }

// ─── Token acquisition ───────────────────────────────────────────────────────

let private acquireSilently () : Async<Result<string, string>> =
  async {
    match readCache () with
    | Some c when not (String.IsNullOrEmpty c.AccessToken) && c.ExpiresAt > nowSeconds () + 60L -> return Ok c.AccessToken
    | Some c when not (String.IsNullOrEmpty c.RefreshToken) ->
      let! result =
        postForm tokenUrl [
          "client_id", clientId
          "grant_type", "refresh_token"
          "refresh_token", c.RefreshToken
          "scope", scope
        ]

      match result with
      | Error e -> return Error e
      | Ok(200, body) ->
        match tryStr body "access_token" with
        | Some at ->
          let rt = tryStr body "refresh_token" |> Option.defaultValue c.RefreshToken
          let exp = nowSeconds () + int64 (tryInt body "expires_in" |> Option.defaultValue 3600)

          writeCache {
            RefreshToken = rt
            AccessToken = at
            ExpiresAt = exp
          }

          return Ok at
        | None -> return Error "no access_token in refresh response"
      | Ok(_, body) -> return Error(tryStr body "error_description" |> Option.defaultValue "token refresh failed")
    | _ -> return Error "no cached token"
  }

let private deviceCodeFlow (onDeviceCode: DeviceCode -> unit) : Async<Result<string, string>> =
  async {
    let! init = postForm deviceCodeUrl [ "client_id", clientId; "scope", scope ]

    match init with
    | Error e -> return Error e
    | Ok(200, body) ->
      match tryStr body "device_code", tryStr body "user_code", tryStr body "verification_uri" with
      | Some deviceCode, Some userCode, Some verificationUri ->
        onDeviceCode {
          VerificationUri = verificationUri
          UserCode = userCode
        }

        let deadline = nowSeconds () + int64 (tryInt body "expires_in" |> Option.defaultValue 900)

        let rec poll (intervalSec: int) =
          async {
            do! Async.Sleep(intervalSec * 1000)

            match nowSeconds () > deadline with
            | true -> return Error "device login timed out"
            | false ->
              let! tok =
                postForm tokenUrl [
                  "client_id", clientId
                  "grant_type", "urn:ietf:params:oauth:grant-type:device_code"
                  "device_code", deviceCode
                ]

              match tok with
              | Error e -> return Error e
              | Ok(200, b) ->
                match tryStr b "access_token" with
                | Some at ->
                  let rt = tryStr b "refresh_token" |> Option.defaultValue ""
                  let exp = nowSeconds () + int64 (tryInt b "expires_in" |> Option.defaultValue 3600)

                  writeCache {
                    RefreshToken = rt
                    AccessToken = at
                    ExpiresAt = exp
                  }

                  return Ok at
                | None -> return Error "no access_token in token response"
              | Ok(_, b) ->
                match tryStr b "error" with
                | Some "authorization_pending" -> return! poll intervalSec
                | Some "slow_down" -> return! poll (intervalSec + 5)
                | _ -> return Error(tryStr b "error_description" |> Option.defaultValue "device login failed")
          }

        return! poll (tryInt body "interval" |> Option.defaultValue 5)
      | _ -> return Error "unexpected devicecode response"
    | Ok(_, body) -> return Error(tryStr body "error_description" |> Option.defaultValue "devicecode request failed")
  }

let acquireToken (onDeviceCode: DeviceCode -> unit) : Async<Result<string, string>> =
  async {
    let! silent = acquireSilently ()

    match silent with
    | Ok at -> return Ok at
    | Error _ -> return! deviceCodeFlow onDeviceCode
  }

// ─── Meeting creation ────────────────────────────────────────────────────────

let createMeeting (subject: string) (onDeviceCode: DeviceCode -> unit) : Async<Result<string, string>> =
  async {
    let! token = acquireToken onDeviceCode

    match token with
    | Error e -> return Error e
    | Ok accessToken ->
      try
        let bodyJson = JsonSerializer.Serialize {| subject = subject |}
        use req = new HttpRequestMessage(HttpMethod.Post, meetingsUrl)
        req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", accessToken)
        req.Content <- new StringContent(bodyJson, Encoding.UTF8, "application/json")
        let! resp = http.SendAsync req |> Async.AwaitTask
        let! respBody = resp.Content.ReadAsStringAsync() |> Async.AwaitTask

        match resp.IsSuccessStatusCode with
        | true ->
          match tryStr respBody "joinWebUrl" with
          | Some url -> return Ok url
          | None -> return Error "meeting created but no joinWebUrl in response"
        | false -> return Error(graphError respBody)
      with ex ->
        return Error ex.Message
  }
