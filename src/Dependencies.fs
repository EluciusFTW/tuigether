module Dependencies

open Firebase.Database

type Dependencies = {
  Client: FirebaseClient
  Backend: Backend.Backend
  Notify: string -> unit
}
