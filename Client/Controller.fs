
namespace Client


module Controller =

    open System
    open System.Text.Json
    open System.Collections.Generic
    open System.Threading
    open FSharp.Control.Reactive
    open System.Reactive
    open FSharp.Json
    open FsToolkit.ErrorHandling
    open Protocol


    // This has yet to be serialized for transport over websocket.
    // TODO - Confirm whether can be internal/private and not break
    // the JSON serialization?
    type ProtocolPayload<'TParams> =
        {
            sessionId: SessionID option
            id: int
            method: string
            ``params``: 'TParams option
        }

    // TODO - Confirm visibility?
    [<RequireQualifiedAccess>]
    type UnparsedMethodResponse =
        | SuccessfulMethodCall of Idx: int * Result: string
        | FailedMethodCall of Idx: int * Error: string

    // TODO - Confirm visibility?
    [<RequireQualifiedAccess>]
    type UnparsedProtocolResponse =
        | MethodResponse of UnparsedMethodResponse
        | Event of UnserializedProtocolEvent
        | Error of Error: string

        static let (|AsInt|_|) (str: string) =
            match Int32.TryParse str with
            | true, value -> Some value
            | _ -> None

        static member fromJsonString (jsonString: string) =
            use parsedDoc =
                JsonDocument.Parse jsonString

            use enumObj =
                parsedDoc.RootElement.EnumerateObject ()

            let propNames =
                enumObj |> Seq.map (fun p -> p.Name, p.Value) |> Map.ofSeq

            let has propName =
                propNames |> Map.tryFind propName |> Option.map (fun pv -> pv.GetRawText ())

            match has "id", has "method", has "params", has "result", has "error" with
            // Successful mathod call.
            | Some (AsInt idx), None, None, Some result, None ->
                UnparsedProtocolResponse.MethodResponse
                    (UnparsedMethodResponse.SuccessfulMethodCall (idx, result))

            // Failed method call.
            | Some (AsInt idx), None, None, None, Some error ->
                UnparsedProtocolResponse.MethodResponse
                    (UnparsedMethodResponse.FailedMethodCall (idx, error))

            // Event.
            | None, Some method, Some ``params``, None, None ->
                // For some reason, event methods have 2x lots of surrounding "s.
                UnparsedProtocolResponse.Event { Method = method.Trim ('"'); Params = ``params`` } 

            // Non-specific failure.
            | None, None, None, None, Some error ->
                UnparsedProtocolResponse.Error error

            // This should never happen!
            | _ ->
                failwith "Unexpected controller error."

    [<RequireQualifiedAccess>]
    type private ShutdownReason =
        // Requested via (for example) a dispose request.
        | UserRequested of Confirm: (unit -> unit) option
        // Requested because (for example) the underlying
        // websocket has experienced an error.
        | SendOrReceiveFailure of Reason: ProtocolFailureReason

    type private IMethodCallBlock =
        interface
            inherit IDisposable
            abstract member Idx: int
            abstract member AwaitResponse: Async<ProtocolOutcome<UnparsedMethodResponse>>
            abstract member NotifyError: ProtocolFailureReason -> unit
        end

    [<RequireQualifiedAccess>]
    type private ControllerAction =
        | BeginMethodCall of Reply: (ProtocolOutcome<IMethodCallBlock> -> unit)
        | RegisterIdxCallback of
            Idx: int *
            Callback: (ProtocolOutcome<UnparsedMethodResponse> -> unit) *
            Cancelled: (OperationCanceledException -> unit)
        | CancelMethodCall of Idx: int
        | EndMethodCall of Idx: int
        | NotifyMethodResponse of Response: UnparsedMethodResponse
        | NotifyReadFailure of Reason: ProtocolFailureReason
        | NotifySendFailure of Reason: ProtocolFailureReason
        | RequestEvents of Reply: (ProtocolOutcome<IObservable<ProtocolEvent>> -> unit)
        | RequestShutdown of Confirm: (unit -> unit) option

    type private MethodResponseState =
        | CompleteOrCancelled
        | AwaitingResponseAndCallback
        | AwaitingCallback of Response: UnparsedMethodResponse
        | AwaitingResponse of
            Callback: (ProtocolOutcome<UnparsedMethodResponse> -> unit) *
            Cancelled: (OperationCanceledException -> unit)


    let private jsonConfig =
        JsonConfig.create (serializeNone = SerializeNone.Omit, deserializeOption = DeserializeOption.AllowOmit)

    let private jsonSerialize payload =
        Result.protect (Json.serializeEx jsonConfig) payload
        |> Result.mapError ProtocolFailureReason.SerializeFailed

    let private jsonDeserialize str =
        Result.protect (Json.deserializeEx jsonConfig) str
        |> Result.mapError ProtocolFailureReason.DeserializeFailed


    let private eventSelector = function
        | UnparsedProtocolResponse.Event evt ->
            match Events.deserializeEvent evt with
            | Ok evt' ->
                Some evt'
            // For now we just ignore any events that we failed to deserialize.
            | Error msg ->
                failwith (sprintf "Unexpected failure to deserialize event: %A" msg)
        | _ ->
            None

    let private methodReponseSelector = function
        | UnparsedProtocolResponse.MethodResponse methodResponse ->
            Some methodResponse
        | _ ->
            None


    type ClientProtocolController internal (
            sender: string -> Async<Result<unit, ProtocolFailureReason>>,
            receipts: IObservable<UnparsedProtocolResponse>
        ) =

        let logSubject =
            new Subjects.Subject<string> ()

        let logObservable =
            Observable.asObservable logSubject

        let logMessage =
            logSubject.OnNext << sprintf "[CONTROLLER]: %s"

        // Can be used to stop the loop underlying the mailbox.
        let mainLoopCTS =
            new CancellationTokenSource ()

        // Rather than everything link to the observable within the
        // websocket, this subject subscribes to that observable
        // from which all subscribers will be notified.
        let eventsSubject =
            new Subjects.Subject<_> ()

        // Convert it to an observable to hide the underlying subject.
        let eventsObservable =
            Observable.asObservable eventsSubject

        // Connect it to the receipts observable in the web-socket.
        let eventsSubscription =
            receipts
            |> Observable.choose eventSelector
            |> Observable.subscribeObserver eventsSubject         

        // Contains all method calls that are in-flight.
        let pendingMethodResponses =
            new Dictionary<_,_> ()

        let mainLoop (mbox: MailboxProcessor<_>) =
            let shutdownState reason =
                async {
                    do logMessage "Transitioning to shutdown state."

                    // Close out any pending method calls.
                    for pendingResponse in pendingMethodResponses do
                        match pendingResponse.Value with
                        | AwaitingResponse (callback, _) ->
                            do callback (Error ProtocolFailureReason.ControllerClosed)
                        | _ ->
                            ()

                    do pendingMethodResponses.Clear ()

                    // Depending on the reason for shutting-down, we
                    // may need to take additional steps.
                    match reason with
                    | ShutdownReason.UserRequested confirm ->
                        do eventsSubject.OnCompleted ()
                        do logMessage "Completing user requested shutdown."
                        
                        match confirm with
                        | Some confirm' -> do confirm' ()
                        | None -> ()

                    | ShutdownReason.SendOrReceiveFailure reason ->
                        do eventsSubject.OnError (new ProtocolException (reason))
                        do logMessage "Completing error-induced shutdown."

                    while true do
                        match! mbox.Receive () with
                        | ControllerAction.BeginMethodCall reply ->
                            do reply (Error ProtocolFailureReason.ControllerClosed) 
                            
                        | ControllerAction.RequestEvents reply ->
                            do reply (Error ProtocolFailureReason.ControllerClosed)

                        | ControllerAction.NotifyReadFailure _
                        | ControllerAction.NotifySendFailure _
                        | ControllerAction.CancelMethodCall _
                        | ControllerAction.EndMethodCall _
                        | ControllerAction.NotifyMethodResponse _
                        | ControllerAction.RequestShutdown None ->
                            // Would be (very!) surprising to see this. Ignore.
                            ()

                        | ControllerAction.RegisterIdxCallback (_, callback, _) ->
                            do callback (Error ProtocolFailureReason.ControllerClosed)

                        | ControllerAction.RequestShutdown (Some confirm) ->
                            // Confirm that we are already shut-down!
                            do confirm ()
                }

            let createMethodBlock idx =
                { new IMethodCallBlock with
                    member _.Idx =
                        idx

                    member _.AwaitResponse =
                        // Return out awaiter where the underlying idx has ALREADY been
                        // registered with the main-loop. Noting that this is a member
                        // property with deferred evaluation!
                        async {
                            // This will get the prevailing token from the caller.
                            let! ct =
                                Async.CancellationToken

                            use _registration =
                                ct.Register (fun _ ->
                                    do mbox.Post (ControllerAction.CancelMethodCall idx))

                            let awaiter =
                                Async.FromContinuations (fun (onCompleted, _, onCancelled) ->
                                    do mbox.Post (ControllerAction.RegisterIdxCallback (idx, onCompleted, onCancelled)))

                            return! awaiter
                        }                        
                                        
                    member _.NotifyError reason =
                        do mbox.Post (ControllerAction.NotifySendFailure reason)

                    member _.Dispose () =
                        do mbox.Post (ControllerAction.EndMethodCall idx) }

            let aliveState =
                async {
                    let mutable nextMethodIdx = 0

                    while true do
                        match! mbox.Receive () with
                        | ControllerAction.BeginMethodCall reply ->
                            // Register our interest in this method idx.
                            do pendingMethodResponses.Add
                                (nextMethodIdx, AwaitingResponseAndCallback)

                            let methodBlock =
                                createMethodBlock nextMethodIdx

                            do reply (Ok methodBlock)

                            do nextMethodIdx <- nextMethodIdx + 1

                        | ControllerAction.RegisterIdxCallback (idx, callback, cancelled) ->
                            // If the idx doesn't exist, something has gone very wrong
                            // and an exception should (and will) be raised.
                            match pendingMethodResponses[idx] with
                            | CompleteOrCancelled ->
                                do cancelled (new OperationCanceledException ())
                            | AwaitingResponseAndCallback ->
                                do pendingMethodResponses[idx] <-
                                    AwaitingResponse (callback, cancelled)
                            | AwaitingCallback response ->
                                do callback (Ok response)
                                // Make sure we're not in a position where callbacks
                                // can be fired multiple times!
                                do pendingMethodResponses[idx] <-
                                    CompleteOrCancelled
                            | AwaitingResponse _ ->
                                // Why are we registering a callback when
                                // we already have one?
                                failwith "Unexpected state."

                        | ControllerAction.NotifyMethodResponse methodCallResponse ->
                            // Match on the method response in order to extract the
                            // underlying idx.
                            // If we're not shutdown, then (by design) we should only be
                            // receiving responses for idxs that have already been used.
                            match methodCallResponse with
                            | UnparsedMethodResponse.SuccessfulMethodCall (idx, _)
                            | UnparsedMethodResponse.FailedMethodCall (idx, _) ->
                                // We can only receive idxs for calls that we've already made.
                                // Allow an exception to be raised if this fails as that signifies
                                // something has gone VERY wrong.
                                match pendingMethodResponses[idx] with
                                | CompleteOrCancelled ->
                                    ()
                                | AwaitingResponseAndCallback ->
                                    do pendingMethodResponses[idx] <-
                                        AwaitingCallback methodCallResponse
                                | AwaitingResponse (callback, _) ->
                                    do callback (Ok methodCallResponse)
                                    // Make sure we're not in a position where callbacks
                                    // can be fired multiple times!
                                    do pendingMethodResponses[idx] <-
                                        CompleteOrCancelled
                                    // Don't remove the idx, this is captured below.
                                | AwaitingCallback _ ->
                                    // If we already have a response, how can this happen?
                                    failwith "Unexpected state."

                        | ControllerAction.CancelMethodCall idx ->
                            match pendingMethodResponses.TryGetValue idx with
                            | false, _ ->
                                do pendingMethodResponses.Add (idx, CompleteOrCancelled)
                            | true, AwaitingResponseAndCallback
                            | true, AwaitingCallback _ ->
                                do pendingMethodResponses[idx] <-
                                    CompleteOrCancelled
                            | true, AwaitingResponse (_, cancelled) ->
                                do cancelled (new OperationCanceledException ())
                                // Make sure we're not in a position where callbacks
                                // can be fired multiple times!
                                do pendingMethodResponses[idx] <-
                                    CompleteOrCancelled                                
                            | true, CompleteOrCancelled ->
                                failwith "Unexpected state."

                        | ControllerAction.EndMethodCall idx ->
                            // Similar situation to above, allow an exception to be raised if necessary.
                            do ignore <| pendingMethodResponses.Remove idx

                        | ControllerAction.RequestEvents reply ->
                            do reply (Ok eventsObservable)

                        | ControllerAction.NotifySendFailure reason ->
                            do logMessage (sprintf "Notified of send failure <<%A>>" reason)
                            return! shutdownState (ShutdownReason.SendOrReceiveFailure reason)
                            
                        | ControllerAction.NotifyReadFailure reason ->
                            do logMessage (sprintf "Notified of read failure <<%A>>" reason)
                            return! shutdownState (ShutdownReason.SendOrReceiveFailure reason)

                        | ControllerAction.RequestShutdown confirm ->
                            return! shutdownState (ShutdownReason.UserRequested confirm)                                                    
                }

            aliveState

        let mainLoopMailbox =
            MailboxProcessor.Start (mainLoop, mainLoopCTS.Token)

        let methodResponsesSubscription =
            let notifyMethodResponse =                
                mainLoopMailbox.Post << ControllerAction.NotifyMethodResponse

            // The only reason this would ever been called is if the
            // web-socket was disposed of before us. However, that should
            // never be the case. Regardless, if we're notified of this,
            // proceed to shutdown ourselves.
            let requestShutdown () =
                do logMessage "Notified of receipts completion."
                do mainLoopMailbox.Post (ControllerAction.RequestShutdown None)

            let notifyReadFailure (observableExn: exn) =
                let reason =
                    observableExn :?> ProtocolException |> _.Reason

                do mainLoopMailbox.Post (ControllerAction.NotifyReadFailure reason)

            receipts
            |> Observable.choose methodReponseSelector
            |> Observable.subscribeSafeWithCallbacks
                notifyMethodResponse notifyReadFailure requestShutdown

        // Note how this has signature unit -> Async<...> rather than
        // just Async<...>. This is because we cannot re-use the async object
        // returned by PostAndAsyncReply (which is not unreasonable!).
        let beginMethodCall () =
            mainLoopMailbox.PostAndAsyncReply
                (ControllerAction.BeginMethodCall << _.Reply)

        let submitRequest (dispatch: ProtocolDispatch<'TParams, 'TResponse>) =
            asyncResult {
                // Notify the main-loop that we are about to begin a method call.
                // The act of disposing this notifies that the method call has finished.
                // ...regardless of whether successful or not.
                use! methodCall =
                    beginMethodCall ()

                let payload =
                    {
                        sessionId = dispatch.Session
                        // Make sure we use the allocated idx.
                        id = methodCall.Idx
                        method = dispatch.Method
                        ``params`` =
                            if typeof<'TParams> = typeof<unit> then
                                None
                            else
                                Some dispatch.Params
                    }

                // We've wrapped the following within another asyncResult block as,
                // should an error occur, we want to be able to notify the main-loop
                // using our disposable method call above.
                let methodCallAsync =
                    asyncResult {
                        let! serializedPayload =
                            jsonSerialize payload

                        do! sender serializedPayload

                        // The main-loop will already be looking for a response
                        // to our particular idx.
                        let! methodCallResponse =
                            methodCall.AwaitResponse

                        let! serializedResponse =
                            match methodCallResponse with
                            | UnparsedMethodResponse.SuccessfulMethodCall (_, serializedResponse') ->
                                Ok serializedResponse'
                            | UnparsedMethodResponse.FailedMethodCall (_, error) ->
                                Error (ProtocolFailureReason.MethodCallFailed error)

                        let! deserializedResponse =
                            if typeof<'TResponse> = typeof<unit> then
                                Ok Unchecked.defaultof<'TResponse>
                            else
                                jsonDeserialize serializedResponse

                        return deserializedResponse
                    }

                return!
                    methodCallAsync
                    // We cannot tack this to the end of the parent asyncResult block
                    // as we'd be referring to a method call block that had
                    // already been disposed of.
                    |> AsyncResult.teeError methodCall.NotifyError
            }

        interface IClientProtocolController with
            member _.SubmitRequest dispatch =
                submitRequest dispatch

            // This property will be re-evaluated each time it is called.
            // We CANNOT use an auto-implemented (ie. 'val') member property here
            // as we would be re-using the same async awaiter each time!
            member _.Events =
                mainLoopMailbox.PostAndAsyncReply
                    (ControllerAction.RequestEvents << _.Reply)

        interface IClientLoggable with
            member val Log =
                logObservable

        interface IDisposable with
            member _.Dispose () =
                do logMessage "Disposing of protocol controller..."

                do mainLoopMailbox.PostAndReply
                    (ControllerAction.RequestShutdown << Some << _.Reply)

                do mainLoopCTS.Cancel ()

                do eventsSubscription.Dispose ()
                do methodResponsesSubscription.Dispose ()

                do eventsSubject.Dispose ()

                do mainLoopCTS.Dispose ()

                do mainLoopMailbox.Dispose ()

                do logMessage "Protocol controller disposed."

                // Make sure that this is the last thing that ever gets disposed.
                do logSubject.Dispose ()


    let create (socket: IClientProtocolSocket) =
        asyncResult {
            let! rawReceipts =
                socket.Receipts

            let receipts =
                rawReceipts
                |> Observable.map UnparsedProtocolResponse.fromJsonString

            return new ClientProtocolController (socket.Send, receipts) :> IClientProtocolController
        }