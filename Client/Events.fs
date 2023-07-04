
namespace Client

module Events =

    open System
    open FSharp.Control.Reactive
    open FSharp.Reflection
    open FSharp.Control
    open FSharp.Quotations
    open FSharp.Json

    open FSharpx
    open FSharpx.Control.Observable
    open FsToolkit.ErrorHandling
    open Protocol


    type CDTPEvent =
    | Page_DomContentEventFired of Page.Events.DomContentEventFired
    | Runtime_ConsoleAPICalled of Runtime.Events.ConsoleAPICalled
    | Runtime_ExceptionRevoked of Runtime.Events.ExceptionRevoked
    | Target_AttachedToTarget of Target.Events.AttachedToTarget
    | Target_DetachedToTarget of Target.Events.DetachedToTarget
    | Target_TargetCrashed of Target.Events.TargetCrashed
    | Target_TargetCreated of Target.Events.TargetCreated
    | Target_TargetDestroyed of Target.Events.TargetDestroyed
    | Target_TargetInfoChanged of Target.Events.TargetInfoChanged
    | Target_ReceivedMessageFromTarget of Target.Events.ReceivedMessageFromTarget
    | Unknown of CDTPUnserializedEvent


    [<AbstractClass; Sealed>]
    type Deserializer private () =
        static let jsonConfig =
            JsonConfig.create (serializeNone = SerializeNone.Omit, deserializeOption = DeserializeOption.AllowOmit)

        static member deserialize str: 'TOuput =
            Json.deserializeEx jsonConfig str


    let deserializeEvent : CDTPUnserializedEvent -> Outcome<CDTPEvent> =
        let deserializeMethod =
            typeof<Deserializer>.GetMethod("deserialize")

        let eventCases =
            FSharpType.GetUnionCases typeof<CDTPEvent>
            |> Array.filter (fun uci -> uci.Name <> nameof(Unknown))
            |> Array.map (fun uci ->
                let normalisedName =
                    uci.Name
                    |> String.toLower
                    |> String.replace '_' '.'
            
                let fieldType =
                    uci.GetFields ()
                    |> Array.exactlyOne
                    |> fun pi -> pi.PropertyType

                // Inspired by http://www.fssnip.net/7ZQ/title/Dynamic-Invocation-of-Generic-Functions-In-A-Module
                let method =
                    deserializeMethod.MakeGenericMethod([| fieldType |])

                let factory (str: string) =
                    let serializedObj =
                        method.Invoke (null, [| str |])

                    FSharpValue.MakeUnion (uci, [| serializedObj |]) :?> CDTPEvent

                (normalisedName, Result.protect factory))
            |> Map.ofArray

        fun ({ Method = method; Params = ``params`` } as evt) ->        
            eventCases
            |> Map.tryFind (method |> String.toLower)
            |> function
                | Some factory -> factory ``params``
                | None -> Ok (Unknown evt)


    let AsyncAwaitEvent<'T> (case: Expr<'T -> CDTPEvent>) (observable: IObservable<CDTPEvent>) =
        // Approach inspired by Tomas' response for https://stackoverflow.com/questions/75735025/identify-du-level-when-partially-applied.
        match case with
        | Patterns.Lambda (_, Patterns.NewUnionCase (uci, _)) ->
            observable
            |> Observable.choose (fun eventCase ->
                match FSharpValue.GetUnionFields (eventCase, typeof<CDTPEvent>) with
                | (uci', [| eventParams |]) when uci = uci' -> Some (eventParams :?> 'T)
                | _ -> None)
            |> Async.AwaitObservable
        | _ ->
            // It was deliberately decided that this would raise an event rather than be wrapped in a Result.
            failwith "Unable to await unrecognised event."


            
