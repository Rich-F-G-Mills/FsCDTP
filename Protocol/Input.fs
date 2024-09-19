
namespace Protocol

[<RequireQualifiedAccess>]
module Input =

    open System

    let [<Literal>] private domain = "Input."


    [<RequireQualifiedAccess>]
    module __DispatchKeyEvent =
        
        type Parameters =
            {
                ``type``: string
                modifiers: Integer
                text: string option
                unmodifiedText: string option
                keyIdentifier: string option
                code: string option
                key: string option
                autoRepeat: bool option
                isKeypad: bool option
                isSystemKey: bool option
            }

            static member val Default =
                {
                    ``type`` = String.Empty
                    modifiers = 0
                    text = None
                    unmodifiedText = None
                    keyIdentifier = None
                    code = None
                    key = None
                    autoRepeat = None
                    isKeypad = None
                    isSystemKey = None
                }

        type Request =
            ProtocolRequest<Parameters, unit, unit>

    let dispatchKeyEvent paramaters: __DispatchKeyEvent.Request =
        ProtocolRequest (SessionRequired, domain + "dispatchKeyEvent", paramaters, id)
