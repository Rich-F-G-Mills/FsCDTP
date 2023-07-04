
namespace Protocol

[<RequireQualifiedAccess>]
module Target =

    open System

    let [<Literal>] private domain = "Target."


    type TargetID = string

    type TargetInfo =
        {
            targetId: TargetID
            ``type``: string
            title: string
            url: string
            attached: Boolean
            openerId: TargetID option
        }

    type IEventMayHaveTargetID =
        interface
            abstract member TargetID: TargetID option
        end

    type IEventHasTargetID =
        interface
            inherit IEventMayHaveTargetID
            abstract member TargetID: TargetID
        end



    [<RequireQualifiedAccess>]
    module __ActivateTarget =

        type Parameters =
            { targetId: TargetID }

        type Request =
            ProtocolRequest<Parameters, unit, unit>

    let activateTarget targetId: __ActivateTarget.Request =
        ProtocolRequest (SessionNotRequired, domain + "activateTarget", { targetId = targetId }, id)


    [<RequireQualifiedAccess>]
    module __AttachToTarget =

        type Parameters =
            {
                targetId: TargetID
                flatten: bool
            }

        type Response =
            { sessionId: SessionID }

        type Request =
            ProtocolRequest<Parameters, Response, SessionID>

    let attachToTarget targetId: __AttachToTarget.Request =
        ProtocolRequest (SessionNotRequired, domain + "attachToTarget", { targetId = targetId; flatten = true }, fun { sessionId = sessionId } -> sessionId)


    [<RequireQualifiedAccess>]
    module __CloseTarget =

        type Parameters =
            {
                targetId: TargetID
            }

        type Request =
            ProtocolRequest<Parameters, unit, unit>

    let closeTarget targetId: __CloseTarget.Request =
        ProtocolRequest (SessionNotRequired, domain + "closeTarget", { targetId = targetId }, id)


    [<RequireQualifiedAccess>]
    module __CreateTarget =

        type Parameters =
            {
                url: string
                width: Integer option
                height: Integer option
            }

        type Response =
            { targetId: TargetID }

        type Request =
            ProtocolRequest<Parameters, Response, TargetID>

    let createTarget url: __CreateTarget.Request =
        ProtocolRequest (SessionNotRequired, domain + "createTarget", { url = url; width = None; height = None }, fun { targetId = targetId } -> targetId)

    //let withWidth width ({ Params = p } as request: CreateTarget.Request) =
    //    { request with Params = { p with width = width } }

    //let withHeight height ({ Params = p } as request: CreateTarget.Request) =
    //    { request with Params = { p with height = height } }

    
    [<RequireQualifiedAccess>]
    module __GetTargets =

        type Response =
            { targetInfos: TargetInfo list }

        type Request =
            ProtocolRequest<unit, Response, TargetInfo list>

    let getTargets: __GetTargets.Request =
        ProtocolRequest (SessionNotRequired, domain + "getTargets", (), function { targetInfos = targetInfos } -> targetInfos)


    [<RequireQualifiedAccess>]
    module __SetDiscoverTargets =

        type Parameters =
            { discover: bool }

        type Request =
            ProtocolRequest<Parameters, unit, unit>

    let setDiscoverTargets discover: __SetDiscoverTargets.Request =
        ProtocolRequest (SessionNotRequired, domain + "setDiscoverTargets", { discover = discover }, id)


    [<RequireQualifiedAccess>]
    module Events =

        type ReceivedMessageFromTarget =
            {
                sessionId: SessionID
                message: string
                targetId : TargetID option            
            }

            interface IEventHasSessionID with
                member this.SessionID = this.sessionId

            interface IEventMayHaveSessionID with
                member this.SessionID = Some this.sessionId

            interface IEventMayHaveTargetID with
                member this.TargetID = this.targetId

        type TargetCrashed =
            {
                targetId: TargetID
                status: string
                errorCode: Integer
            }

            interface IEventHasTargetID with
                member this.TargetID = this.targetId

            interface IEventMayHaveTargetID with
                member this.TargetID = Some this.targetId
        
        type TargetCreated =
            { targetInfo: TargetInfo }

            interface IEventHasTargetID with
                member this.TargetID = this.targetInfo.targetId

            interface IEventMayHaveTargetID with
                member this.TargetID = Some this.targetInfo.targetId

        type TargetDestroyed =
            { targetId: TargetID }

            interface IEventHasTargetID with
                member this.TargetID = this.targetId

            interface IEventMayHaveTargetID with
                member this.TargetID = Some this.targetId

        type TargetInfoChanged =
            { targetInfo: TargetInfo }

            interface IEventHasTargetID with
                member this.TargetID = this.targetInfo.targetId

            interface IEventMayHaveTargetID with
                member this.TargetID = Some this.targetInfo.targetId

        type AttachedToTarget =
            {
                sessionId: SessionID
                targetInfo: TargetInfo
                waitingForDebugger: bool            
            }

            interface IEventHasSessionID with
                member this.SessionID = this.sessionId

            interface IEventMayHaveSessionID with
                member this.SessionID = Some this.sessionId

            interface IEventHasTargetID with
                member this.TargetID = this.targetInfo.targetId

            interface IEventMayHaveTargetID with
                member this.TargetID = Some this.targetInfo.targetId

        type DetachedFromTarget =
            {
                sessionId: SessionID
                targetId: TargetID option         
            }

            interface IEventHasSessionID with
                member this.SessionID = this.sessionId

            interface IEventMayHaveSessionID with
                member this.SessionID = Some this.sessionId

            interface IEventMayHaveTargetID with
                member this.TargetID = this.targetId