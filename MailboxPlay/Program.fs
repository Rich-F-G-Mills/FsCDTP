
open System
open FsToolkit.ErrorHandling



type Escapable () =
    member _.Return (x) =
        Some x

    member _.Combine (lhs: 'T option, rhs: unit -> 'T option) =
        match lhs with
        | Some _ -> lhs
        | None -> rhs ()

    member _.Delay (d: unit -> 'T option) =
        d

    member _.Zero () =
        None

    member _.Run (d: unit -> 'T option) =
        d ()


let escapable = new Escapable ()

let o =
    escapable {
        let x = 4

        if x = 2 then
            return 3

        if x = 3 then
            return 4

        return 5
    }

printfn "%A" o