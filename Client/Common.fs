
namespace Client

[<AutoOpen>]
module Common =

    open FSharpx

    type Outcome<'T> = Result<'T, exn>

    type AsyncOutcome<'T> = Async<Outcome<'T>>

    module Async =
        let CatchAsResult<'T> : Async<'T> -> Async<Result<'T, exn>> =
            Async.Catch
            >> Async.map (function
                | Choice1Of2 value -> Ok value
                | Choice2Of2 value -> Error value)

    type CDTPUnserializedEvent =
        { Method: string; Params: string }

    