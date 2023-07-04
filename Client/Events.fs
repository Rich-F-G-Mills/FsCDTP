

namespace Client


module Events =

    open FSharp.Reflection
    open FSharp.Json

    open Protocol


    [<AbstractClass; Sealed>]
    // Cannot be private or reflection further down will not work.
    type Deserializer private () =
        static let jsonConfig =
            JsonConfig.create (serializeNone = SerializeNone.Omit, deserializeOption = DeserializeOption.AllowOmit)

        // We use a static member rather than a 'let' binding in a module
        // so we can more reasily invoke this method via reflection.
        static member deserialize str: 'TOuput =
            Json.deserializeEx jsonConfig str


    // Not market internal as might be useful to alternative controller implementations.
    let deserializeEvent =
        let deserializeMethod =
            typeof<Deserializer>.GetMethod("deserialize")

        let eventCases =
            FSharpType.GetUnionCases typeof<ProtocolEvent>
            |> Array.filter (fun uci -> uci.Name <> nameof ProtocolEvent.Unknown)
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

                let unprotectedfactory (str: string) =
                    let serializedObj =
                        method.Invoke (null, [| str |])

                    FSharpValue.MakeUnion (uci, [| serializedObj |]) :?> ProtocolEvent

                let factory =
                    Result.protect unprotectedfactory

                (normalisedName, factory))
            |> Map.ofArray

        fun ({ Method = method; Params = ``params`` } as evt) ->  
            eventCases
            |> Map.tryFind (String.toLower method)
            |> function
                | Some factory -> factory ``params``
                | None -> Ok (ProtocolEvent.Unknown evt)


    // Have annotated 'observedEvent' as was previously generic without.
    let chooseSpecificEvent (_required: 'TEventParams -> ProtocolEvent) (observedEvent: ProtocolEvent) =
        let (unionCaseInfo, obsEventParams') =
            FSharpValue.GetUnionFields (observedEvent, typeof<ProtocolEvent>)

        let obsEventParamsType =
            unionCaseInfo
            |> _.GetFields()
            |> Array.exactlyOne
            |> _.PropertyType

        if typeof<'TEventParams> = obsEventParamsType then
            let eventParams =
                obsEventParams'
                |> Array.exactlyOne
                :?> 'TEventParams

            Some eventParams                
        else
            None        
