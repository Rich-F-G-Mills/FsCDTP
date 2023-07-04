
namespace Client

[<AutoOpen>]
module Builder =

    open System
    open FSharp.Quotations
    open FsToolkit.ErrorHandling

    open Client
    open Protocol


    type Transportable<'TOutput> =
        private Transportable of ((Target.SessionID option * Transport.ICDTPTransport) -> Async<Outcome<'TOutput>>)

    type SwitchToSession =
        private SwitchToSession of SessionId: Target.SessionID option

    type RunAsChildRequest<'TInput, 'TOutput, 'TOutputMapped> =
        private RunAsChildRequest of ProtocolRequest<'TInput, 'TOutput, 'TOutputMapped>

    type AwaitEvent<'T> =
        private AwaitEvent of Expr<'T -> Events.CDTPEvent>


    type Request =
        static member SwitchToSession (newSessionId: Target.SessionID option) = SwitchToSession newSessionId

        static member RunAsChildRequest pr = RunAsChildRequest pr
        
        static member RunWithTransport (transport: Transport.ICDTPTransport) (Transportable f) =
            f (None, transport)    

        static member RunAsync asyncComp = asyncComp |> Async.map Ok

        static member AwaitEvent ([<ReflectedDefinition>] evt: Expr<'T -> Events.CDTPEvent>) =
            AwaitEvent evt


    type RequestBuilder () =
        static let toTransportRequest sessionId (br: ProtocolRequest<_, 'TOutput, _>): Result<Transport.Request<_,'TOutput>, exn> =
            let (ProtocolRequest (sessionStatus, method, parameters, _)) = br

            match sessionStatus, sessionId with
            | SessionRequired, Some _
            | SessionOptional, _ ->
                Ok { SessionId = sessionId; Method = method; Params = parameters }

            | SessionNotRequired, _ ->
                Ok { SessionId = None; Method = method; Params = parameters }

            | SessionRequired, None ->
                Error (Exception "No session ID has been set.")

        member _.Return x =
            Transportable (fun (_, _) -> AsyncResult.retn x)

        member _.Bind (SwitchToSession newSessionId, binder) =
            Transportable (fun (_, transport) ->
                async {
                    let (Transportable f) = binder ()

                    return! f (newSessionId, transport)
                })

        member _.Bind (RunAsChildRequest (ProtocolRequest (_, _, _, mapper) as br), binder) =
            Transportable(fun (sessionId, transport) ->
                asyncResult {                    
                    let! transportableRequest = toTransportRequest sessionId br

                    let! comp =
                        transport.SubmitRequest transportableRequest
                        |> AsyncResult.map mapper
                        |> Async.StartChild

                    let (Transportable f) = binder comp

                    return! f (sessionId, transport)           
                })

        member _.Bind (AwaitEvent evt, binder) =
            Transportable (fun (sessionId, transport) ->
                async {
                    let! result = Events.AsyncAwaitEvent evt (transport.Events)

                    let (Transportable f) = binder result

                    return! f (sessionId, transport)
                })

        member _.Bind (comp: Async<Outcome<'T>>, binder) =
            Transportable (fun (sessionId, transport) ->
                asyncResult {
                    let! result = comp

                    let (Transportable f) = binder result

                    return! f (sessionId, transport)
                })

        member _.Bind (ProtocolRequest (_, _, _, mapper) as br, binder) =
            Transportable(fun (sessionId, transport) ->
                asyncResult {
                    let! transportableRequest = toTransportRequest sessionId br

                    let! output = transport.SubmitRequest transportableRequest

                    let (Transportable f) = binder (mapper output)

                    return! f (sessionId, transport)
                })


    let request = new RequestBuilder ()
