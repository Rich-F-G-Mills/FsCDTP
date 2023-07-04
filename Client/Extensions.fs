
namespace Client


[<AutoOpen>]
module Extensions =

    open System
    open FSharp.Control.Reactive
    open FsToolkit.ErrorHandling

    open Protocol


    [<RequireQualifiedAccess>]
    module internal String =
        let inline toLower (str: string) =
            str.ToLower ()

        let inline replace (what: char) (``with``: char) (str: string) =
            str.Replace (what, ``with``)


    [<RequireQualifiedAccess>]
    module Result =
        let inline internal protect ([<InlineIfLambda>]f) x =
            try
                Ok (f x)
            with e -> Error e

        let toDispatchable (result: ProtocolOutcome<_>) =
            Dispatchable (fun (state, _) ->
                asyncResult {
                    let! result' =
                        result

                    return state, result'
                })


    [<RequireQualifiedAccess>]
    module List =
        // TODO - Is it even possible to have a TCO version of this?
        let rec private inner_SequenceDispatchableM (f: 'T -> Dispatchable<'U>) (fxs: 'U list) (xs: 'T list) =
            match xs with
            | [] ->
                Dispatchable.retn (List.rev fxs)
            | x::xs' ->
                Dispatchable.bind (f x, fun fx -> inner_SequenceDispatchableM f (fx::fxs) xs')

        // Heavily inspired by 'List.sequenceResultM' as provided by FsToolkit.
        let sequenceDispatchableM (f: 'T -> Dispatchable<'U>) (xs: 'T list) =
            inner_SequenceDispatchableM f [] xs


    [<RequireQualifiedAccess>]
    module Async =        
        let internal CatchAsResult x =
            Async.Catch x
            |> Async.map Result.ofChoice

        module private GuardedAwaitObservable =

            type private Callbacks<'T> =
                {
                    OnValueReceived: 'T -> unit
                    OnError: exn -> unit
                    OnCancelled: OperationCanceledException -> unit       
                }

            [<RequireQualifiedAccess>]
            type private State<'T> =
                | AwaitingValueOrCallbacks
                | AwaitingValue of Callbacks: Callbacks<'T>
                // This might occur if the observable broadcasts
                // before we have our async callbacks.
                | AwaitingCallbacks of Value: Choice<'T, exn>
                | CompleteOrCancelled

            // Previous attempts have used a mailbox process in order to
            // synchronise state changes. However, we can accomplish the same
            // in a "light-weight" manner using some monitor locks.
            let internal exec (observable, withAwaiter) =           
                async {
                    let mutable state =
                        State.AwaitingValueOrCallbacks                

                    let updateState =
                        let stateLock = new obj ()

                        fun mapper ->
                            lock stateLock (fun _ -> do state <- mapper state)

                    let onValueReceived v =
                        do updateState (function                          
                            | State.AwaitingValueOrCallbacks ->
                                // Record the value we've received; now we just need our async callback!
                                State.AwaitingCallbacks (Choice1Of2 v)
                            | State.AwaitingValue { OnValueReceived = ovr } ->
                                // We've already got the callback... Call it!
                                do ovr v
                                State.CompleteOrCancelled
                            // We could get multiple notifications before the callbacks are registered.
                            | State.AwaitingCallbacks _
                            // ...or before we're unsubscribed.
                            | State.CompleteOrCancelled ->
                                // Either way, maintain the current state.
                                state)

                    let onException exn =
                        do updateState (function 
                            | State.AwaitingValueOrCallbacks ->
                                State.AwaitingCallbacks (Choice2Of2 exn)
                            | State.AwaitingValue { OnError = oe } ->                            
                                do oe exn
                                State.CompleteOrCancelled
                            | State.CompleteOrCancelled   
                            // This means that the observable is putting out additional
                            // messages after sending an onError/onCompleted message.
                            // Although this breaks the supposed contract,
                            // we're not going to be the ones to police it here.
                            // Just maintain the current state.
                            | State.AwaitingCallbacks _ ->                          
                                state)

                    let onError exn =
                        do onException (new Exception ("Observable error.", exn))

                    let onComplete () =
                        do onException (new Exception "Observable completed.")

                    let! ct =
                        Async.CancellationToken

                    // Register a callback to be called upon cancellation.
                    // However, we need to be (and are) careful that we could be cancelled
                    // once the async callbacks are known but have yet to be registered. 
                    use _register =
                        ct.Register (fun _ ->
                            updateState (function
                                | State.AwaitingValue { OnCancelled = oc } ->
                                    do oc (new OperationCanceledException "Await cancelled.")
                                    State.CompleteOrCancelled
                                | _ ->
                                    State.CompleteOrCancelled))

                    // If the underlying subject has completed/errored, the callbacks
                    // will be notified before the (empty) disposable is returned.
                    // Also, because we want to be able to ensure that we're already
                    // waiting for the event before we get our async callbacks, it's
                    // cleaner to have the subscription set up beforehand.
                    use _subscription =
                        observable
                        |> Observable.subscribeWithCallbacks
                            onValueReceived onError onComplete

                    let awaiter =
                        Async.FromContinuations (fun (onAsyncComplete, onAsyncError, onAsyncCancelled) ->                        
                            // What if we're cancelled here? (ie. before the upcoming statelock)...
                            // The state will be CompleteOrCancelled because, at the
                            // time the CT callback was called, we weren't aware of
                            // the async callbacks above. As such, we need to (and do) explicitly check
                            // to see if the CT has been triggered within the protected region.

                            do updateState (fun currentState ->
                                if ct.IsCancellationRequested then
                                    do onAsyncCancelled (new OperationCanceledException "Await cancelled.")
                                    State.CompleteOrCancelled
                                else
                                    match currentState with
                                    | State.AwaitingCallbacks (Choice1Of2 v) ->
                                        do onAsyncComplete v
                                        State.CompleteOrCancelled

                                    | State.AwaitingCallbacks (Choice2Of2 exn) ->
                                        do onAsyncError exn
                                        State.CompleteOrCancelled

                                    | State.AwaitingValueOrCallbacks ->
                                        State.AwaitingValue {
                                            OnValueReceived = onAsyncComplete
                                            OnError = onAsyncError
                                            OnCancelled = onAsyncCancelled
                                        }

                                    | State.AwaitingValue _ ->
                                        // Suggests we're registering callbacks twice?                            
                                        failwith "Unexpected state."

                                    | State.CompleteOrCancelled ->
                                        State.CompleteOrCancelled)
                        )

                    return! withAwaiter awaiter
                }

        let GuardedAwaitObservable observable withAwaiter =
            GuardedAwaitObservable.exec (observable, withAwaiter)

        let AwaitObservable observable =
            GuardedAwaitObservable observable id


    [<RequireQualifiedAccess>]
    module AsyncResult =
        let toDispatchable (async: Async<Result<_, ProtocolFailureReason>>) =
            Dispatchable (fun (state, _) ->
                asyncResult {
                    let! result =
                        async

                    return state, result
                })

        let GuardedAwaitObservable observable (withAwaiter: Async<_> -> Async<Result<_, exn>>) =
            Async.GuardedAwaitObservable observable withAwaiter
            |> Async.Catch
            |> Async.map (function
                | Choice1Of2 (Ok v) -> Ok v
                | Choice1Of2 (Error exn) -> Error exn
                | Choice2Of2 exn -> Error exn)

        let AwaitObservable observable =
            GuardedAwaitObservable observable id


    [<RequireQualifiedAccess>]
    module ProtocolRequest =
        let toDispatchable (request) = 
            Dispatchable (fun (DispatchableState session as state, dispatcher) ->
                asyncResult {
                    let (ProtocolRequest (sessionReq, method, ``params``, mapper)) =
                        request

                    let! request =
                        match sessionReq, session with
                        | ProtocolSessionRequirement.SessionNotRequired, _ ->
                            Ok { Session = None; Method = method; Params = ``params`` }
                        | ProtocolSessionRequirement.SessionOptional, _
                        | ProtocolSessionRequirement.SessionRequired, Some _ ->
                            Ok { Session = session; Method = method; Params = ``params`` }
                        | ProtocolSessionRequirement.SessionRequired, None ->
                            Error ProtocolFailureReason.MissingSession

                    let! outcome =
                        dispatcher.SubmitRequest request

                    return state, mapper outcome
                })     


    [<RequireQualifiedAccess>]
    module Map =
        // Should this return a Result instead of failing with an exception?
        let ofAttributeSeq (attribs: #seq<string>) =
            attribs
            |> Seq.indexed
            |> Seq.map (fun (idx, x) -> idx / 2, x)
            |> Seq.groupBy fst
            |> Seq.map (fun (_, xs) ->
                let x' =
                    xs |> Seq.map snd |> List.ofSeq

                match x' with
                | name::[value] -> name, value
                | _ -> failwith "Unable to convert attribute list to map.")
            |> Map.ofSeq

        
            