module Auth

open System
open System.Text.RegularExpressions
open Firebase.Auth
open Firebase.Auth.Providers

// Builds the Firebase Authentication client from the project's Web API key and
// auth domain. Email/password is the only enabled provider.
let createClient (settings: Config.Settings) : FirebaseAuthClient =
  let config =
    FirebaseAuthConfig(
      ApiKey = settings.FirebaseApiKey,
      AuthDomain = settings.FirebaseAuthDomain,
      Providers = [| EmailProvider() :> FirebaseAuthProvider |]
    )

  new FirebaseAuthClient(config)

// Signs in with email/password, returning the authenticated user or a
// human-readable error. FirebaseAuthException carries a structured Reason we can
// turn into a friendly message; anything else falls back to its message text.
let signIn (client: FirebaseAuthClient) (email: string) (password: string) : Async<Result<User, string>> =
  async {
    try
      let! credential =
        client.SignInWithEmailAndPasswordAsync(email, password)
        |> Async.AwaitTask

      return Ok credential.User
    with
    | :? FirebaseAuthException as e ->
      let message =
        match e.Reason with
        | AuthErrorReason.WrongPassword
        | AuthErrorReason.UnknownEmailAddress
        | AuthErrorReason.InvalidEmailAddress -> "Invalid email or password."
        | AuthErrorReason.UserDisabled -> "This account has been disabled."
        | AuthErrorReason.TooManyAttemptsTryLater -> "Too many attempts. Try again later."
        | _ -> e.Message

      return Error message
    | e -> return Error e.Message
  }

// Firebase RTDB keys may not contain '.', '$', '#', '[', ']' or '/'. The
// identity string is used as a child key for presence/driver/locks, so sanitize
// it (emails always contain a '.').
let private sanitizeKey (raw: string) =
  Regex.Replace(raw, @"[.$#\[\]/]", "_")

// The participant name shown to teammates: the account display name if set,
// otherwise the email local-part. Always sanitized for use as a Firebase key.
let identity (user: User) : string =
  let info = user.Info

  let raw =
    match String.IsNullOrWhiteSpace info.DisplayName with
    | false -> info.DisplayName
    | true ->
      match info.Email with
      | null -> user.Uid
      | email ->
        match email.IndexOf '@' with
        | -1 -> email
        | at -> email.Substring(0, at)

  sanitizeKey raw
