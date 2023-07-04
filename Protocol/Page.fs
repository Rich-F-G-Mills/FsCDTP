
namespace Protocol

[<RequireQualifiedAccess>]
module Page =

    let [<Literal>] private domain = "Page."


    type FrameId = string


    module Navigate =

        type Parameters =
            { url: string }

        type Response =
            { frameId: FrameId }

        type Request = ProtocolRequest<Parameters, Response, FrameId>

    let navigate url: Navigate.Request =
        ProtocolRequest (SessionRequired, domain + "navigate", { url = url }, fun { frameId = frameId } -> frameId)


    module Events =

        type DomContentEventFired  =
            {
                timestamp: Network.MonotonicTime
            }