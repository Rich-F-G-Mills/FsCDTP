
open System
open System.Reactive
open FSharp.Control.Reactive
open FsToolkit.ErrorHandling

open Client


let private retirementDate =
    DateOnly(2024, 9, 5)

let [<Literal>] private stateDB =
    """C:\Users\Millch\Documents\FsCDTP\QuoteScanner\state.db"""


[<RequireQualifiedAccess>]
module private ScriptStage =

    let rec awaitingClientQuotes logger (state: State.AwaitingAnnuityQuotes) =
        protocolScript {
            do logger (sprintf "STATE --- AWAITING QUOTES FOR CLIENT #%s." state.QuotesRequiredFor.Description)

            let! clientQuotes =
                runWithoutStateChange (RequestClientQuotes.execute logger retirementDate state.QuotesRequiredFor)

            let nextState =
                state.ConfirmQuotes clientQuotes

            match nextState with
            | Some nextClient ->
                do! awaitingClientQuotes logger nextClient
            | None ->
                return ()
        }

    let rec awaitingClientUpdates logger (state: State.AwaitingClientUpdate) =
        protocolScript {
            do logger (sprintf "STATE --- AWAITING UPDATE FOR CLIENT #%s." state.UpdateRequiredFor.Description)

            do! runWithoutStateChange (UpdateClientDetails.execute logger retirementDate state.UpdateRequiredFor)

            let nextState =
                state.ConfirmClientUpdated ()

            match nextState with
            | Choice1Of2 state' ->
                do! awaitingClientUpdates logger state'
            | Choice2Of2 state' ->
                do! awaitingClientQuotes logger state'
        }
    
    let awaitingClientRecords logger (state: State.AwaitingClientRecords) =
        protocolScript {
            do logger "STATE --- AWAITING CLIENT RECORDS."

            // We don't care about the target ID or session ID.
            let! _ =
                NavigateToClientList.execute logger

            let! clientRecords =
                ExtractClientRecords.execute logger

            let nextState =
                state.ConfirmClientRecords clientRecords

            do! awaitingClientUpdates logger nextState
        }


let rec private script logger state =
    protocolScript {
        match state with
        | State.AwaitingClientRecords state' ->
            do! ScriptStage.awaitingClientRecords logger state'

        | State.AwaitingClientUpdate state' ->
            do! ScriptStage.awaitingClientUpdates logger state'

        | State.AwaitingAnnuityQuotes state' ->
            do! ScriptStage.awaitingClientQuotes logger state'

        | State.Complete ->
            do logger "STATE --- COMPLETE."

            return ()
    }


let updateDOB =
    protocolScript {
        let! targets =
            Protocol.Target.getTargets

        let target =
            targets
            |> List.filter (fun t -> t.url.EndsWith("3665528"))
            |> List.exactlyOne

        let! _ =
            Helpers.attachTargetAndSwitchSession target.targetId

        do! populateEditBox "#DOB" "01/08/1970"

        do! focus "#Title"

        do! clickButton "#btnSave"
    }


let private mainAsync =
    asyncResult {       
        use logSubject =
            new Subjects.Subject<string> ()

        use _ =
            logSubject
            // Ensure that we only process one at a time.
            |> Observable.synchronize
            |> Observable.subscribe (printfn "LOG >>> %s")

        let userLogger =
            logSubject.OnNext << sprintf "[USER]: %s"

        do userLogger "Reading state from database..."

        use persistentState =
            SqlitePersistency.create stateDB retirementDate false

        do userLogger "Creating websocket... "

        use! ws =
            WebSocket.create ("localhost", 9222)

        do ws.Log.Add logSubject.OnNext

        //let! receipts =
        //    ws.Receipts

        //do receipts.Add (printfn "<<< %s >>>")

        do userLogger "Creating controller... "

        use! controller =
            Controller.create (ws)

        do controller.Log.Add logSubject.OnNext

        do userLogger "Beginning script..."

        do! runProtocolScript controller (script userLogger persistentState.OpeningState)

        //do! runProtocolScript controller updateDOB

        return 0
    }

mainAsync
|> Async.RunSynchronously
|> Result.teeError (printfn "Program error: %A")
|> ignore