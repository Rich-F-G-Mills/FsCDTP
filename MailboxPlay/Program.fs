
open System
open System.Threading
open FSharp.Control.Reactive
open Client


let obs =
    Observable.single 5
    |> Observable.delay (TimeSpan.FromMilliseconds 2_000)

let obs2: IObservable<int> =
    Observable.throw (new Exception "Failed!")

let cts =
    new CancellationTokenSource ()

let ct =
    cts.Token

do cts.CancelAfter 1_000

let main =
    async {
        return! Async.AwaitObservable obs
    }

do printfn "%A" (Async.RunSynchronously (main, cancellationToken = ct))