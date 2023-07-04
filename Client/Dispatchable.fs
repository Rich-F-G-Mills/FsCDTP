
namespace Client

open Protocol


// Currently, the state only reflects the prevailing session ID.
// No plans to extend at this time.
type DispatchableState =
    internal | DispatchableState of
        Session: SessionID option

// The monadic (?) type that will be used throughout.
// Is effectively a stateful version (?) of an Async<Result<...>>.
type Dispatchable<'T> =
    internal | Dispatchable of
        (DispatchableState * IClientProtocolController -> Async<ProtocolOutcome<DispatchableState * 'T>>)


[<RequireQualifiedAccess>]
module Dispatchable =
    
    open System.Collections.Generic
    open FsToolkit.ErrorHandling


    let retn (x): Dispatchable<_> =
        Dispatchable (fun (state, _) -> AsyncResult.retn (state, x))

    let inline returnFrom (dispatchable: Dispatchable<_>) =
        dispatchable

    let bind (Dispatchable dispatchable: Dispatchable<'T>, binder: 'T -> Dispatchable<'U>) =
        Dispatchable (fun (state, controller) ->
            asyncResult {
                let! (newState, result) =
                    dispatchable (state, controller)

                let (Dispatchable binder') =
                    binder result

                return! binder' (newState, controller)
            })

    let rec internal whileLoop (pred: unit -> bool, body: unit -> Dispatchable<unit>) =
        if pred () then 
            bind (body (), fun _ -> whileLoop (pred, body))
        else
            retn ()

    let internal forLoop (collection: #IEnumerable<'T>, body: 'T -> Dispatchable<unit>) =
        use ie =
            collection.GetEnumerator ()

        whileLoop (ie.MoveNext, fun () ->
            Dispatchable (fun (state, controller) ->
                asyncResult {
                    let (Dispatchable body') =
                        body ie.Current

                    let! newState, _ =
                        body' (state, controller)

                    return newState, ()                    
                }))

    let mapWithoutStateChange f (Dispatchable dispatchable) =
        Dispatchable (fun (state, controller) ->
            asyncResult {
                let! (_, result) =
                    dispatchable (state, controller)

                return state, f result
            })

