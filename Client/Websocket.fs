
namespace Client

open System
open System.Buffers
open System.Collections.Generic
open System.Net.Http
open System.Net.WebSockets
open System.Reactive
open System.Text
open System.Threading    
    
open FSharp.Control.Reactive
open FSharp.Json
open FsToolkit.ErrorHandling


module WebSocket =

    type private ISendLock =
        interface
            inherit IDisposable
            abstract member NotifyError: ProtocolFailureReason -> unit
        end

    [<RequireQualifiedAccess>]
    type private SocketAction =
        | RequestSendAccess of Reply: (Result<ISendLock, ProtocolFailureReason> -> unit)
        | NotifySendFailure of Reason: ProtocolFailureReason
        | ReleaseSendAccess
        | NotifyRead of Reply: Result<string, ProtocolFailureReason>
        | RequestReceipts of Reply: (Result<IObservable<string>, ProtocolFailureReason> -> unit)
        | RequestShutdown of Confirm: (unit -> unit)

    [<RequireQualifiedAccess>]
    type private ShutdownReason =
        | UserRequested of Confirm: (unit -> unit)
        | SendOrReceiveFailure of Reason: ProtocolFailureReason

    type ClientProtocolSocket internal (ws: ClientWebSocket) =
        
        let logSubject =
            new Subjects.Subject<string> ()

        let logObservable =
            Observable.asObservable logSubject

        let logMessage =
            logSubject.OnNext << sprintf "[SOCKET]: %s"

        let receiptsSubject =
            new Subjects.Subject<string>()

        let receiptsObservable =
            Observable.asObservable receiptsSubject

        let receiveLoopCTS =
            new CancellationTokenSource ()

        let sendCTS =
            new CancellationTokenSource ()

        let mainLoopCTS =
            new CancellationTokenSource ()

        let sendBuffer =
            new ArrayBufferWriter<_>()

        let receiveBuffer =
            new ArrayBufferWriter<_>()

        let pendingLockRequests =
            new Queue<_> ()

        let receiveLoop (mbox: MailboxProcessor<_>) =
            let inner: Async<Result<unit, _>> =
                asyncResult {
                    let! prevailingCT =
                        Async.CancellationToken

                    while true do
                        let memory =
                            receiveBuffer.GetMemory (1_024)

                        // Check that the websocket has the required state.
                        do! ws.State
                            |> Result.requireEqualTo WebSocketState.Open ProtocolFailureReason.SocketClosed

                        let! receiveValueTask =
                            try
                                // The call to receive async could fail before a task is even returned.
                                Ok (ws.ReceiveAsync(memory, prevailingCT))
                            with exn ->
                                Error (ProtocolFailureReason.Exception exn)
                    
                        let! receiveResult =
                            receiveValueTask
                            |> _.AsTask()                            
                            |> Async.AwaitTask
                            // Based on the documentation, the only exception we're expecting is
                            // that arising from a cancelled read.
                            |> Async.CatchAsResult
                            |> AsyncResult.mapError ProtocolFailureReason.Exception
                    
                        do receiveBuffer.Advance receiveResult.Count 

                        if receiveResult.EndOfMessage then
                            let content = 
                                Encoding.UTF8.GetString (receiveBuffer.WrittenSpan)                        

                            // Clear the buffer ready for the next round.
                            do receiveBuffer.Clear ()

                            do mbox.Post (SocketAction.NotifyRead (Ok content))
                }
            
            inner
            // Catch any errors and make sure they are reported to the main loop.
            |> AsyncResult.teeError (mbox.Post << SocketAction.NotifyRead << Error)
            |> AsyncResult.teeError (logMessage << sprintf "Receive loop terminated due to failure <<%A>>")
            // We only cared about the error so far as to report it to the main loop.
            // Once done, we ignore any result.
            |> Async.Ignore

        let mainLoop (mbox: MailboxProcessor<_>) =
            // Although disposable, it can be re-used as solely used
            // as a mechanism to relay messages to the main-loop.
            let sendLock =
                { new ISendLock with
                    member _.NotifyError reason =
                        do mbox.Post (SocketAction.NotifySendFailure reason)

                    member _.Dispose () =
                        do mbox.Post SocketAction.ReleaseSendAccess }

            // This is an absorbing state.
            let shutdownState reason =
                async {
                    // If we're transitioning to the shut down state, there is
                    // some house-keeping that we need to do.
                    do logMessage "Transitioning to shutdown state."

                    // Cancel the receive loop.
                    do receiveLoopCTS.Cancel ()
                    // Cancel any sends in progress.
                    do sendCTS.Cancel ()

                    // Cancel any outstanding locks.
                    for pendingLocks in pendingLockRequests do
                        do pendingLocks (Error ProtocolFailureReason.SocketClosed)

                    do pendingLockRequests.Clear ()

                    match reason with
                    | ShutdownReason.UserRequested confirm ->
                        do receiptsSubject.OnCompleted ()
                        do logMessage "Completing user requested Shutdown."
                        do confirm ()
                    | ShutdownReason.SendOrReceiveFailure reason ->
                        do receiptsSubject.OnError (new ProtocolException (reason))
                        do logMessage "Completing error-induced shutdown."                    

                    // Any time we get a request, we notify that the connection
                    // is already closed.
                    while true do                            
                        match! mbox.Receive () with
                        | SocketAction.RequestSendAccess reply ->
                            do reply (Error ProtocolFailureReason.SocketClosed)                       

                        | SocketAction.NotifySendFailure _
                        | SocketAction.ReleaseSendAccess
                        | SocketAction.NotifyRead _ ->
                            // Would be surprised to get this but there is a chance that
                            // a pipeline request comes through.
                            ()

                        | SocketAction.RequestReceipts reply ->
                            do reply (Error ProtocolFailureReason.SocketClosed)

                        | SocketAction.RequestShutdown confirm ->
                            // Should this arise, simply confirm that
                            // we are already shut down.
                            do confirm ()
                }

            let rec idleState () =
                async {
                    match! mbox.Receive () with
                    | SocketAction.RequestSendAccess reply ->
                        do reply (Ok sendLock)
                        return! sendingState ()

                    | SocketAction.NotifySendFailure _
                    | SocketAction.ReleaseSendAccess ->
                        failwith "Unexpected request."

                    | SocketAction.RequestReceipts reply ->
                        do reply (Ok receiptsObservable)
                        return! idleState ()

                    | SocketAction.NotifyRead (Ok response) ->
                        do receiptsSubject.OnNext response
                        return! idleState ()

                    | SocketAction.NotifyRead (Error reason) ->
                        do logMessage (sprintf "Notified of read failure <<%A>>" reason)
                        return! shutdownState (ShutdownReason.SendOrReceiveFailure reason)

                    | SocketAction.RequestShutdown confirm ->
                        return! shutdownState (ShutdownReason.UserRequested confirm)
                }

            and sendingState () =
                async {
                    match! mbox.Receive () with
                    | SocketAction.RequestSendAccess reply ->
                        do pendingLockRequests.Enqueue reply
                        return! sendingState ()                

                    | SocketAction.NotifySendFailure reason ->
                        do logMessage (sprintf "Notified of send failure <<%A>>" reason)
                        return! shutdownState (ShutdownReason.SendOrReceiveFailure reason)

                    | SocketAction.ReleaseSendAccess ->
                        match pendingLockRequests.TryDequeue () with
                        | true, nextRequest ->
                            do nextRequest (Ok sendLock)
                            return! sendingState ()
                        | false, _ ->
                            return! idleState ()

                    | SocketAction.RequestReceipts reply ->
                        do reply (Ok receiptsObservable)
                        return! sendingState ()

                    | SocketAction.NotifyRead (Ok response) ->
                        do receiptsSubject.OnNext response
                        return! sendingState ()

                    | SocketAction.NotifyRead (Error reason) ->
                        do logMessage (sprintf "Notified of read failure <<%A>>" reason)
                        return! shutdownState (ShutdownReason.SendOrReceiveFailure reason)

                    | SocketAction.RequestShutdown confirm ->
                        return! shutdownState (ShutdownReason.UserRequested confirm)
                }

            idleState ()      


        // Based on experiments... The following appears to be true:
        // * Disposing of the mailbox does not stop the async loop.
        // * The async loop must be stopped using a passed in CT or by
        //   ceasing any recursion.
        let mainLoopMailbox =
            MailboxProcessor.Start (mainLoop, mainLoopCTS.Token)

        do Async.StartImmediate (receiveLoop mainLoopMailbox, receiveLoopCTS.Token)

        let requestLock () =
            mainLoopMailbox.PostAndAsyncReply
                (SocketAction.RequestSendAccess << _.Reply)                

        let send (content: string) =
            asyncResult {
                // Whatever happens, this will ensure that the lock is released.
                // This will be the case even if this is cancelled.
                // Request the lock here so that we don't progress further
                // if the connection has already closed.
                // ...appreciating that the lock will be held for
                // infinitesimally longer than would otherwise be needed.
                use! lock =
                    requestLock ()

                let! prevailingCT =
                    Async.CancellationToken

                // Allow the send operation to be cancelled by our own CTS as well.
                use combinedCTS =
                    CancellationTokenSource.CreateLinkedTokenSource
                        (sendCTS.Token, prevailingCT)

                do sendBuffer.Clear ()

                do Encoding.UTF8.GetBytes (content, sendBuffer) |> ignore
                    
                // We want to capture any errors within this block so that
                // we can notify the main loop should any occur.
                let sendOutcomeAsync =
                    asyncResult {
                        do! ws.State
                            |> Result.requireEqualTo WebSocketState.Open ProtocolFailureReason.SocketClosed

                        let! receiveValueTask =
                            try
                                // The call to send async could fail before a task is even returned.
                                Ok (ws.SendAsync(sendBuffer.WrittenMemory, WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, combinedCTS.Token))
                            with exn ->
                                Error (ProtocolFailureReason.Exception exn)

                        return!
                            receiveValueTask
                            |> _.AsTask()
                            // If the task contains an exception, calling AwaitTask will not itself
                            // fail. The failure will only occur once the resulting Async is executed.
                            // Using Async.CatchAsResult will ensure that the outcome is instead
                            // represented using the Result type.
                            |> Async.AwaitTask
                            |> Async.CatchAsResult
                            |> AsyncResult.mapError ProtocolFailureReason.Exception
                    }
                    // Ensure that any error is passed back to the main loop.
                    |> AsyncResult.teeError lock.NotifyError   

                return! sendOutcomeAsync                                 
            }

        interface IClientProtocolSocket with
            member _.Send content =
                send content

            // This will get re-evaluated each time... This CANNOT ever
            // be a 'member val' property as we'd be re-using the same
            // awaiter each time.
            member _.Receipts =
                mainLoopMailbox.PostAndAsyncReply
                    (SocketAction.RequestReceipts << _.Reply)

        interface IClientLoggable with
            member val Log =
                logObservable

        interface IDisposable with
            member _.Dispose () =
                do logMessage "Disposing of protocol websocket..."

                do mainLoopMailbox.PostAndReply
                    (SocketAction.RequestShutdown << _.Reply)

                // Although we've already cancelled the receive loop
                // and (pending) sends. We still need to stop the main
                // synchronization loop which continues to run even
                // after an error.
                do mainLoopCTS.Cancel ()
                do receiptsSubject.Dispose ()     
                
                do receiveLoopCTS.Dispose ()
                do sendCTS.Dispose ()
                do mainLoopCTS.Dispose ()

                do mainLoopMailbox.Dispose ()

                do logMessage "Protocol websocket disposed."

                // Make sure that this is the last thing that ever gets disposed.
                do logSubject.Dispose ()


    let create (host, port) =       
        asyncResult {
            let! prevailingCT =
                Async.CancellationToken

            use httpClient =
                new HttpClient ()

            let jsonUri =
                sprintf "http://%s:%i/json/version" host port

            let! getContentTask =
                try
                    Ok (httpClient.GetStringAsync (jsonUri, prevailingCT))
                with exn ->
                    Error (ProtocolFailureReason.Exception exn)

            let! content =
                getContentTask
                |> Async.AwaitTask
                |> Async.CatchAsResult
                |> AsyncResult.mapError ProtocolFailureReason.Exception

            let! (browserMetadata: {| webSocketDebuggerUrl: string |}) =
                Result.protect Json.deserialize content
                |> Result.mapError ProtocolFailureReason.Exception

            let ws =
                new ClientWebSocket ()

            do ws.Options.KeepAliveInterval <-
                TimeSpan.FromSeconds 5

            let! connectTask =
                try
                    Ok (ws.ConnectAsync(Uri browserMetadata.webSocketDebuggerUrl, prevailingCT))
                with exn ->
                    Error (ProtocolFailureReason.Exception exn)

            do! connectTask
                |> Async.AwaitTask
                |> Async.CatchAsResult
                |> AsyncResult.mapError ProtocolFailureReason.Exception

            return new ClientProtocolSocket (ws) :> IClientProtocolSocket
        }