
namespace rec Client

open System.Collections.Generic
open System.Threading
open FsToolkit.ErrorHandling
open Protocol


type ProtocolScriptBuilder internal () =        

    member _.Return x =
        Dispatchable.retn x

    member _.ReturnFrom x =
        Dispatchable.returnFrom x

    member _.Bind (dispatchable: Dispatchable<'T>, binder) =
        Dispatchable.bind (dispatchable, binder)

    member _.Delay (delayed: unit -> Dispatchable<_>) =
        delayed

    member _.Run (delayed: unit -> Dispatchable<_>) =
        delayed ()

    member _.Combine (Dispatchable first: Dispatchable<unit>, second: unit -> Dispatchable<_>) =
        Dispatchable (fun (state, controller) ->
            asyncResult {
                let! (newState, _) =
                    first (state, controller)

                let (Dispatchable second') =
                    second ()

                return! second' (newState, controller)
            })

    member _.While (pred, body) =
        Dispatchable.whileLoop (pred, body)

    member _.For (collection, body) =
        Dispatchable.forLoop (collection, body)

    member _.Zero () =
        Dispatchable.retn ()

    member _.Source (dispatchable: Dispatchable<_>) =
        dispatchable

    // This is a special case as we cannot add IDispatchable to this type.
    member _.Source (protocolRequest: ProtocolRequest<_,_,'T>) =
        ProtocolRequest.toDispatchable protocolRequest

    member _.Source (async: Async<unit>) =
        Dispatchable (fun (state, _) ->
            asyncResult {
                do! async

                return state, ()
            })

    member _.Source (async: Async<ProtocolOutcome<_>>) =
        AsyncResult.toDispatchable async

    member _.Source (result: ProtocolOutcome<_>) =
        Result.toDispatchable result

    //member _.Source (async: Async<_>) =
    //    Async.toDispatchable async

    // Without this, we cannot implement the 'for x in ...' construct.
    member _.Source (collection: #IEnumerable<'T>) =
        collection


[<AutoOpen>]
module ProtocolScript =

    let protocolScript =
        new ProtocolScriptBuilder ()        

    let runProtocolScript (controller: #IClientProtocolController) (Dispatchable inner) =
        asyncResult {
            let! (_, result) =
                inner (DispatchableState None, controller)

            return result
        }
        