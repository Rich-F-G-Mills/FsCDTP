
namespace Client


[<AutoOpen>]
module rec Types =

    open System
    open Protocol


    [<RequireQualifiedAccess>]
    type ProtocolFailureReason =
        | ControllerClosed
        | SocketClosed
        | MissingSession
        | SerializeFailed of exn
        | DeserializeFailed of exn
        | MethodCallFailed of Error: string
        | Exception of exn
        | UserSpecified of string

    type ProtocolException (reason: ProtocolFailureReason) =
        inherit Exception ()
        member val Reason = reason

    type ProtocolOutcome<'T> =
        Result<'T, ProtocolFailureReason>


    // Represents the "yet to be serialized" payload that will be
    // sent over the web-socket.
    type ProtocolDispatch<'TParams, 'TResponse> =
        {
            Session: SessionID option
            Method: string
            Params: 'TParams
        }


    [<Interface>]
    type IClientLoggable =       
        interface
            abstract Log: IObservable<string>
        end

    [<Interface>]
    type IClientProtocolSocket =       
        interface
            inherit IDisposable
            inherit IClientLoggable
            abstract Send: string -> Async<ProtocolOutcome<unit>>
            abstract Receipts: Async<ProtocolOutcome<IObservable<string>>> with get
        end

    [<Interface>]
    type IClientProtocolController =
        interface
            inherit IDisposable
            inherit IClientLoggable
            abstract SubmitRequest: ProtocolDispatch<'TInput, 'TOutput> -> Async<ProtocolOutcome<'TOutput>>
            abstract Events: Async<ProtocolOutcome<IObservable<ProtocolEvent>>>
        end
