
namespace Protocol

[<RequireQualifiedAccess>]
module Runtime =

    open System

    let [<Literal>] private domain = "Runtime."


    type CallFrame =
        {
            functionName: string
            scriptId: ScriptId
            url: string
            lineNumber: int
            columnNumber: int

        }

    and ExecutionContextDescription =
        {
            id: ExecutionContextId
            origin: string
            name: string
            uniqueId: string
        }

    and ExceptionDetails =
        {
            exceptionId: int
            text: string
            lineNumber: int
            columnNumber: int
            scriptId: ScriptId option
            url: string option
            ``exception``: RemoteObject option
            executionContextId: ExecutionContextId option
        }

    and ExecutionContextId = int

    and InternalPropertyDescriptor =
        {
            name: string
            value: RemoteObject option
        }

    and PropertyDescriptor =
        {
            name: string
            value: RemoteObject option
            writable: bool option
            get: RemoteObject option
            set: RemoteObject option
            configurable: bool
            enumerable: bool
            wasThrown: bool option
            isOwn: bool option
            symbol: RemoteObject option
        }

    and RemoteObject =
        {
            ``type``: string
            subtype: string option
            className: string option
            unserializableValue: UnserializableValue option
            description: string option
            objectId: RemoteObjectId option
        }

    and RemoteObjectId = string

    and ScriptId = string

    and TimeDelta = float

    and Timestamp = float

    and UnserializableValue = string


    let enable : ProtocolRequest<unit, unit, unit> =
        ProtocolRequest (SessionNotRequired, domain + "enable", (), id)


    module __Evaluate =

        type Parameters =
            {
                expression: string
                includeCommandLineAPI: bool option
                silent: bool option
                contextId: ExecutionContextId option
                returnByValue: bool option
                awaitPromise: bool option
                throwOnSideEffect: bool option
            }

            static member val Default =
                {
                    expression = String.Empty
                    includeCommandLineAPI = None
                    silent = None
                    contextId = None
                    returnByValue = None
                    awaitPromise = None
                    throwOnSideEffect = None
                }

        type Response =
            {
                result: RemoteObject
                exceptionDetails: ExceptionDetails option
            }

        type Request =
            ProtocolRequest<Parameters, Response, Response>

    let evaluate parameters : __Evaluate.Request =
        ProtocolRequest (SessionRequired, domain + "evaluate", parameters, id)


    module Events =

        type ConsoleAPICalled =
            {
                ``type``: string
                args: RemoteObject []
                executionContextId: ExecutionContextId
                timestamp: Timestamp
                context: string option
            }

        type ExceptionRevoked =
            {
                reason: string
                exceptionId: int
            }

        type ExceptionThrown =
            {
                timestamp: Timestamp
                exceptionDetails: ExceptionDetails
            }

        type ExecutionContextCreated =
            {
                context: ExecutionContextDescription
            }

        type ExecutionContextDestroyed =
            {
                executionContextId: ExecutionContextId
                executionContextUniqueId: string
            }

        type InspectRequested =
            {
                object: RemoteObject
                executionContextId: ExecutionContextId option
            }

        type BindingCalled =
            {
                name: string
                payload: string
                executionContextId: ExecutionContextId
            }
