module Dependencies

type Dependencies = {
  Backend: Backend.Backend
  Notify: string -> unit
}
