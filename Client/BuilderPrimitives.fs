
namespace Client


[<AutoOpen>]
module BuilderPrimitives =

    open FsToolkit.ErrorHandling

    let switchToSession newSession =
        Dispatchable (fun _ ->
            AsyncResult.retn (DispatchableState (Some newSession), ()))

    // No need to make deferred as can be re-used.
    let unlinkSession =
        Dispatchable (fun _ ->
            AsyncResult.retn (DispatchableState None, ()))

    let getSession =
        Dispatchable (fun (DispatchableState sessionId as state, _) ->
            asyncResult {                
                return state, sessionId
            })

    let runAsChild (Dispatchable dispatchable) =
        Dispatchable (fun (state, dispatcher) ->
            asyncResult {
                let! childAsync =
                    dispatchable (state, dispatcher)
                    |> Async.StartChild

                return state, childAsync
            })

    let runWithoutStateChange (Dispatchable dispatchable) =
        Dispatchable (fun (state, dispatcher) ->
            asyncResult {
                // Ignore any state change.
                let! (_, result) =
                        dispatchable (state, dispatcher)

                return state, result
            })

    let getEventObservable evt =
        Dispatchable (fun (state, dispatcher) ->
            asyncResult {
                let! allEventsObservable =
                    dispatcher.Events

                let requiredEventObservable =
                    allEventsObservable
                    |> Observable.choose (Events.chooseSpecificEvent evt)
                
                return state, requiredEventObservable
            })
