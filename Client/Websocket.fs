
namespace Client

[<RequireQualifiedAccess>]
module Websocket =

    open System
    open System.Collections.Generic
    open System.Buffers
    open System.Net.Http
    open System.Net.WebSockets
    open System.Text
    open System.Threading    
    
    open FSharp.Control.Reactive
    open FSharp.Json
    open FSharpx
    open FsToolkit.ErrorHandling

    // Must be placed here or Subject<_> cannot be found.
    open System.Reactive.Subjects


    type SendOutcome = Outcome<unit>
    type ReceiveOutcome = Outcome<string>

    type private SendRequest =
        | SendRequest of Content: string * Reply: (SendOutcome -> unit)

    type private Status =
        | IsDisconnected
        | IsAwaitingReceive
        | IsAwaitingReceiveAndSend of SendReply: (SendOutcome -> unit)

    type private Message =
        | Send of SendRequest
        | SendComplete of SendOutcome
        | ReceiveComplete of ReceiveOutcome


    type ICDTPWebsocket =
        inherit IDisposable
        abstract Send: string -> Async<SendOutcome>
        abstract Receipts: IObservable<string> with get


    type CDTPWebsocket internal (ws: ClientWebSocket) =
        let receipts = new Subject<_> ()
        
        let internalCancelReadCTS =
            new CancellationTokenSource ()
        
        let internalCancelMailboxCTS =
            new CancellationTokenSource ()

        let cancelReadCTS =
            CancellationTokenSource.CreateLinkedTokenSource(internalCancelMailboxCTS.Token, internalCancelReadCTS.Token)

        let processLogic (mbox: MailboxProcessor<_>) =
            let pendingSends = new Queue<_> ()
            let receiveBuffer = new ArrayBufferWriter<_> ()
            let sendBuffer = new ArrayBufferWriter<_> ()

            let sendContent (content: string) =
                async {
                    do sendBuffer.Clear ()

                    do Encoding.UTF8.GetBytes (content, sendBuffer) |> ignore
                            
                    let! result =
                        (ws.SendAsync (sendBuffer.WrittenMemory, WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, internalCancelMailboxCTS.Token)).AsTask ()
                        |> Async.AwaitTask
                        |> Async.Catch

                    do mbox.Post <|
                        match result with
                        | Choice1Of2 _ -> SendComplete (Ok ())
                        | Choice2Of2 ex -> SendComplete (Error ex)
                }

            let rec receiveLoop () =
                async {
                    if not cancelReadCTS.IsCancellationRequested then
                        let memory = receiveBuffer.GetMemory (1_024)

                        let! result =
                            (ws.ReceiveAsync (memory, cancelReadCTS.Token)).AsTask()                            
                            |> Async.AwaitTask
                            |> Async.Catch

                        match result with
                        | Choice1Of2 result ->
                            do receiveBuffer.Advance result.Count                            

                            if result.EndOfMessage then
                                let content = Encoding.UTF8.GetString(receiveBuffer.WrittenSpan)

                                do mbox.Post <| ReceiveComplete (Ok content) 
                                
                                do receiveBuffer.Clear ()

                                do! receiveLoop ()

                        | Choice2Of2 ex ->
                            do mbox.Post <| ReceiveComplete (Error ex)                            
                }

            let rec inner state =
                async {
                    let! msg = mbox.Receive ()

                    match msg, state with
                    | Send (SendRequest (content, reply)), IsAwaitingReceive ->
                        do Async.StartImmediate (sendContent content, internalCancelMailboxCTS.Token)

                        do! inner <| IsAwaitingReceiveAndSend reply

                    | Send sr, IsAwaitingReceiveAndSend _ ->
                        pendingSends.Enqueue sr

                        do! inner state

                    | Send (SendRequest (_, reply)), IsDisconnected ->
                        do reply (Error <| new Exception ("Disconnected."))

                        do! inner state

                    | SendComplete outcome, IsAwaitingReceiveAndSend sendReply ->
                        do sendReply outcome
                        
                        match outcome with
                        | Ok () when pendingSends.Count > 0 ->
                            let (SendRequest (nextContent, nextSendReply)) =
                                pendingSends.Dequeue ()
                            
                            do Async.StartImmediate (sendContent nextContent, internalCancelMailboxCTS.Token)

                            do! inner <| IsAwaitingReceiveAndSend nextSendReply

                        | Ok () ->
                            do! inner <| IsAwaitingReceive

                        | Error ex ->
                            do internalCancelReadCTS.Cancel ()
                            do receipts.OnError ex

                            // https://www.salmanq.com/blog/5-things-you-probably-didnt-know-about-net-websockets/
                            do ws.CloseOutputAsync (WebSocketCloseStatus.Empty, "Close", CancellationToken.None) |> ignore

                            do! inner IsDisconnected

                    | ReceiveComplete outcome, IsAwaitingReceive
                    | ReceiveComplete outcome, IsAwaitingReceiveAndSend _ ->
                        match outcome with
                        | Ok content ->
                            do receipts.OnNext content
                            do! inner state

                        | Error ex ->                        
                            do internalCancelReadCTS.Cancel ()
                            do receipts.OnError ex

                            // https://www.salmanq.com/blog/5-things-you-probably-didnt-know-about-net-websockets/
                            do ws.CloseOutputAsync (WebSocketCloseStatus.Empty, "Close", CancellationToken.None) |> ignore

                            do! inner IsDisconnected                           
                    
                    | ReceiveComplete _, IsDisconnected
                    | SendComplete _, IsDisconnected
                    | SendComplete _, IsAwaitingReceive _ ->
                        failwith "Unexpected state."
                }

            Async.StartImmediate (receiveLoop (), cancelReadCTS.Token)

            inner IsAwaitingReceive

        let processor =
            MailboxProcessor.Start (processLogic, internalCancelMailboxCTS.Token)

        interface ICDTPWebsocket with
            member val Receipts =
                receipts |> Observable.asObservable

            member _.Send content =
                processor.PostAndAsyncReply (fun channel ->
                    Send <| SendRequest (content, channel.Reply))

            member _.Dispose () =
                do internalCancelMailboxCTS.Cancel ()
                do receipts.OnCompleted ()


    let create (host, port, cancellationToken: CancellationToken) =       
        asyncResult {
            use httpClient = new HttpClient ()

            let! topLevelContent =
                let jsonUri =
                    sprintf "http://%s:%i/json/version" host port

                httpClient.GetStringAsync (jsonUri, cancellationToken)
                |> Async.AwaitTask
                |> Async.CatchAsResult

            let! (browsetMetadata: {| webSocketDebuggerUrl: string |}) =
                Result.protect Json.deserialize topLevelContent

            let ws = new ClientWebSocket ()

            ws.Options.KeepAliveInterval <- TimeSpan.FromSeconds 5

            do! ws.ConnectAsync(Uri browsetMetadata.webSocketDebuggerUrl, cancellationToken)
                |> Async.AwaitTask
                |> Async.CatchAsResult

            if ws.State = WebSocketState.Open then
                return new CDTPWebsocket (ws) :> ICDTPWebsocket
            else
                return! Error <| Exception "Failed to connect."
        }