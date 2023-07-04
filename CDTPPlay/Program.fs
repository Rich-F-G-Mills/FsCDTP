
open System.Threading

open FsToolkit.ErrorHandling

open Protocol
open Client


[<EntryPoint>]
let main _ =

    asyncResult {
        use! ws =
            Websocket.create ("localhost", 9222, CancellationToken.None)

        do printfn "Web socket created."

        use transport =
            Transport.create ws

        do printfn "Transport created."

        //transport.Events |> Observable.add (printfn "Event: %A\n")

        do!
            request {
                let! targets = Target.getTargets

                do! Target.setDiscoverTargets true

                let! sessionId = Target.attachToTarget (targets[0].targetId)

                do! Target.activateTarget targets[0].targetId

                do! Request.SwitchToSession (Some sessionId)

                let! _ = Page.navigate "http://www.github.com"

                let! targetCreated = Request.AwaitEvent Events.Target_TargetCreated

                do printfn "%A" targetCreated.targetInfo

                return targetCreated
            }
            |> Request.RunWithTransport transport
            |> Async.Ignore
    } |> Async.RunSynchronously
    |> ignore

    0