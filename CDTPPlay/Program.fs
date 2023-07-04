
open System
open System.Reactive
open System.Threading
open FSharp.Control.Reactive
open FsToolkit.ErrorHandling

open Protocol
open Client


let private runContext =
    { RetirementDate = DateOnly (2024, 6, 9) }

let myCTS =
    new CancellationTokenSource ()

let private script logger =
    protocolScript {
        let! _ =
            NavigateToClientList.execute logger
        
        let! clientRecords =
            ExtractClientRecords.execute logger

        //for client in clientRecords do
        //    do! runWithoutStateChange (UpdateClientDetails.execute logger runContext client)

        do! RequestAnnuities.execute logger runContext clientRecords[0]

        return clientRecords        
    }

asyncResult {
    use logSubject =
        new Subjects.Subject<string> ()

    use _ =
        logSubject
        |> Observable.synchronize
        |> Observable.subscribe (printfn "LOG >>> %s")

    let userLogger =
        logSubject.OnNext << sprintf "[USER]: %s"

    do printf "Creating websocket... "

    use! ws =
        WebSocket.create ("localhost", 9222)

    do ws.Log.Add logSubject.OnNext

    let! receipts =
        ws.Receipts

    //do receipts.Add (printfn "<<< %s >>>")

    do printfn "Done."

    do printf "Creating controller... "

    use! controller =
        Controller.create (ws)

    do controller.Log.Add logSubject.OnNext

    do printfn "Done."

    do userLogger "Beginning script..."

    let! clients =
        runProtocolScript controller (script userLogger)

    do userLogger "Script complete."

    do printfn "\n\nCLIENTS ---\n\n%A" clients

    return 0
}
|> Async.RunSynchronously
|> Result.teeError (printfn "Program error: %A")
|> ignore
