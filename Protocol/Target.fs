
namespace Protocol

[<RequireQualifiedAccess>]
module Target =

    open System

    let [<Literal>] private domain = "Target."


    type SessionID = string

    and TargetID = string

    and TargetInfo =
        {
            targetId: TargetID
            ``type``: string
            title: string
            url: string
            attached: Boolean
            openerId: TargetID option
        }


    module ActivateTarget =

        type Parameters =
            { targetId: TargetID }

        type Request = ProtocolRequest<Parameters, unit, unit>

    let activateTarget targetId: ActivateTarget.Request =
        ProtocolRequest (SessionNotRequired, domain + "activateTarget", { targetId = targetId }, id)


    module AttachToTarget =

        type Parameters =
            {
                targetId: TargetID
                flatten: bool
            }

        type Response =
            { sessionId: SessionID }

        type Request = ProtocolRequest<Parameters, Response, SessionID>

    let attachToTarget targetId: AttachToTarget.Request =
        ProtocolRequest (SessionNotRequired, domain + "attachToTarget", { targetId = targetId; flatten = true }, fun { sessionId = sessionId } -> sessionId)


    module CreateTarget =

        type Parameters =
            {
                url: string
                width: int option
                height: int option
            }

        type Response =
            { targetId: TargetID }

        type Request = ProtocolRequest<Parameters, Response, TargetID>

    let createTarget url: CreateTarget.Request =
        ProtocolRequest (SessionNotRequired, domain + "createTarget", { url = url; width = None; height = None }, fun { targetId = targetId } -> targetId)

    //let withWidth width ({ Params = p } as request: CreateTarget.Request) =
    //    { request with Params = { p with width = width } }

    //let withHeight height ({ Params = p } as request: CreateTarget.Request) =
    //    { request with Params = { p with height = height } }

    
    module GetTargets =

        type Response =
            { targetInfos: TargetInfo [] }

        type Request = ProtocolRequest<unit, Response, TargetInfo []>

    let getTargets: GetTargets.Request =
        ProtocolRequest (SessionNotRequired, domain + "getTargets", (), function { targetInfos = targetInfos } -> targetInfos)


    module SetDiscoverTargets =

            type Parameters =
                { discover: bool }

            type Request = ProtocolRequest<Parameters, unit, unit>

    let setDiscoverTargets discover: SetDiscoverTargets.Request =
        ProtocolRequest (SessionNotRequired, domain + "setDiscoverTargets", { discover = discover }, id)


    module Events =

        type ReceivedMessageFromTarget =
            {
                sessionId: SessionID
                message: string
                targetId : TargetID option            
            }

        type TargetCrashed =
            {
                targetId: TargetID
                status: string
                errorCode: int
            }
        
        type TargetCreated =
            { targetInfo: TargetInfo }

        type TargetDestroyed =
            { targetId: TargetID }

        type TargetInfoChanged =
            { targetInfo: TargetInfo }

        type AttachedToTarget =
            {
                sessionId: SessionID
                targetInfo: TargetInfo
                waitingForDebugger: bool            
            }

        type DetachedToTarget =
            {
                sessionId: SessionID
                targetId: TargetID option         
            }