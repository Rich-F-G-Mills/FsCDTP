
namespace Client

[<RequireQualifiedAccess>]
module Transport =

    open System
    open System.Text.Json
    open System.Threading
   
    open FSharp.Control.Reactive
    open FSharp.Json    
    open FSharpx
    open FsToolkit.ErrorHandling
    open System.Reactive.Subjects

    open Protocol


    type Request<'TInput, 'TOutput> =
        {
            SessionId: Target.SessionID option
            Method: string
            Params: 'TInput
        }

    type private Message =
        | Send of Builder: (int -> string) * Reply: (Outcome<string> -> unit)
        | SendComplete of Id: int * Outcome: Websocket.SendOutcome * Reply: (Outcome<string> -> unit)
        | Received of Content: string

    type Payload<'TInput> =
        {
            sessionId: Target.SessionID option
            id: int
            method: string
            ``params``: 'TInput option
        }


    type ICDTPTransport =
        inherit IDisposable

        abstract SubmitRequest : Request<'TInput, 'TOutput> -> Async<Outcome<'TOutput>>
        abstract Events: IObservable<Events.CDTPEvent>


    type CDTPTransport internal (webSocket: Websocket.ICDTPWebsocket) =
        static let jsonConfig =
            JsonConfig.create (serializeNone = SerializeNone.Omit, deserializeOption = DeserializeOption.AllowOmit)

        static let (|AsInt|_|) (str: string) =
            match Int32.TryParse str with
            | true, value -> Some value
            | _ -> None

        let internalCancelMailboxCTS =
            new CancellationTokenSource ()

        let events = new Subject<_> ()

        let processLogic (mbox: MailboxProcessor<_>) =
            let rec inner ((currentIdx, outstandingRequests) as state) =
                async {
                    match! mbox.Receive () with
                    | Send (builder, reply) ->
                        async {
                            let content = builder currentIdx

                            let! wsOutcome = webSocket.Send content

                            do mbox.Post (SendComplete (currentIdx, wsOutcome, reply))
                        } |> Async.StartImmediate
                        
                        do! inner (currentIdx + 1, outstandingRequests)

                    | SendComplete (sentIdx, Ok (), reply) ->
                        do! inner (currentIdx, outstandingRequests |> Map.add sentIdx reply)

                    | SendComplete (_, Error ex, reply) ->
                        do reply (Error ex)

                        do! inner state

                    | Received content ->
                        use jsonDom = JsonDocument.Parse content
                            
                        use enumObj = jsonDom.RootElement.EnumerateObject ()

                        let propNames =
                            enumObj |> Seq.map (fun p -> p.Name, p.Value) |> Map.ofSeq

                        let Has propName =
                            propNames |> Map.tryFind propName |> Option.map (fun pv -> pv.GetRawText ())

                        match Has "id", Has "method", Has "params", Has "result", Has "error" with
                        // Result of method call.
                        | Some (AsInt id), None, None, Some result, None ->
                            if Map.containsKey id outstandingRequests then
                                do outstandingRequests[id] (Ok result)

                                do! inner (currentIdx, outstandingRequests |> Map.remove id)
                            else
                                do! inner state

                        // Failed method call.
                        | Some (AsInt id), None, None, None, Some error ->
                            if Map.containsKey id outstandingRequests then
                                do outstandingRequests[id] (Error <| Exception error)

                                do! inner (currentIdx, outstandingRequests |> Map.remove id)
                            else
                                do! inner state

                        // Event.
                        | None, Some method, Some ``params``, None, None ->
                            Events.deserializeEvent { Method = method.Trim('"'); Params = ``params`` }
                            |> Result.iter events.OnNext

                            do! inner state

                        // Non-specific failure.
                        | None, None, None, None, Some error ->
                            do! inner state                      
                }

            inner (1, Map.empty)

        let processor =
            MailboxProcessor.Start (processLogic, internalCancelMailboxCTS.Token)

        let receipts =
            webSocket.Receipts.Subscribe (Received >> processor.Post)

        interface ICDTPTransport with
            member _.SubmitRequest (tr: Request<'TInput, 'TOutput>) =
                let builder id =
                    let (payload: Payload<'TInput>) =
                        {
                            sessionId = tr.SessionId
                            id = id
                            method = tr.Method
                            ``params`` = if typeof<'TInput> = typeof<unit> then None else Some tr.Params                        
                        }
                    
                    Json.serializeEx jsonConfig payload

                asyncResult {
                    let! responseJson =
                        processor.PostAndAsyncReply (fun reply -> Send (builder, reply.Reply))

                    // Have deliberately not protected this as this SHOULD NOT fail.
                    // If it does, a serious error (!) has occured with the protocol implementation.
                    let (result: 'TOutput) =
                        if typeof<'TOutput> = typeof<unit> then
                            Unchecked.defaultof<'TOutput>
                        else
                            Json.deserializeEx jsonConfig responseJson

                    return result
                }

            member _.Events = events |> Observable.asObservable

            member _.Dispose () =
                do internalCancelMailboxCTS.Cancel ()
                do events.OnCompleted ()
                do receipts.Dispose ()
            

    let create (ws: Websocket.ICDTPWebsocket) =
        new CDTPTransport (ws) :> ICDTPTransport