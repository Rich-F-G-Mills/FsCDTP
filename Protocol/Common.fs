
namespace Protocol

[<AutoOpen>]
module Common =

    type ProtocolSessionRequirement =
        | SessionRequired
        | SessionOptional
        | SessionNotRequired

    type ProtocolRequest<'TParams, 'TResponse, 'TMappedResponse> =
        | ProtocolRequest of
            SessionRequirement: ProtocolSessionRequirement *
            Method: string *
            Params: 'TParams *
            Mapper: ('TResponse -> 'TMappedResponse)


    type Integer = int64

    type SessionID = string


    type IEventMayHaveSessionID =
        interface
            abstract member SessionID: SessionID option
        end

    type IEventHasSessionID =
        interface
            inherit IEventMayHaveSessionID
            abstract member SessionID: SessionID
        end