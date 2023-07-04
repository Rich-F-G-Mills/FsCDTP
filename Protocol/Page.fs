
namespace Protocol

[<RequireQualifiedAccess>]
module Page =

    open System

    let [<Literal>] private domain = "Page."


    type FrameId = string

    type Frame =
        {
            id: FrameId
            parentId: FrameId option
            loaderId: Network.LoaderId
            name: string option
            url: string
            securityOrigin: string
            mimeType: string
        }

    type ScreenshotFormat =
        | Jpeg
        | Png
        | Webp

    type IEventMayHaveFrameID =
        interface
            abstract member FrameID: FrameId option
        end
    
    type IEventHasFrameID =
        interface
            inherit IEventMayHaveFrameID
            abstract member FrameID: FrameId
        end    


    [<RequireQualifiedAccess>]
    module __CaptureScreenshot =

        type Parameters =
            {
                format: string
                captureBeyondViewport: bool
            }

        type Response =
            {
                data: string
            }

        type Request =
            ProtocolRequest<Parameters, Response, byte []>

    let captureScreenshot format: __CaptureScreenshot.Request =
        let formatStr =
            match format with
            | ScreenshotFormat.Jpeg -> "jpeg"
            | ScreenshotFormat.Png -> "png"
            | ScreenshotFormat.Webp -> "webp"

        ProtocolRequest (
            SessionRequired,
            domain + "captureScreenshot",
            { format = formatStr; captureBeyondViewport = true },
            fun { data = data } -> Convert.FromBase64String data
        )


    let disable: ProtocolRequest<_, unit, _> =
        ProtocolRequest (SessionRequired, domain + "disable", (), id)

    let enable: ProtocolRequest<_, unit, _> =
        ProtocolRequest (SessionRequired, domain + "enable", (), id)


    module __Navigate =

        type Parameters =
            { url: string }

        type Response =
            { frameId: FrameId }

        type Request =
            ProtocolRequest<Parameters, Response, FrameId>

    let navigate url: __Navigate.Request =
        ProtocolRequest (SessionRequired, domain + "navigate", { url = url }, fun { frameId = frameId } -> frameId)


    module Events =

        type FrameDetached =
            {
                frameId: FrameId
                reason: string
            }

            interface IEventHasFrameID with
                member this.FrameID = this.frameId

            interface IEventMayHaveFrameID with
                member this.FrameID = Some this.frameId

        type FrameNavigated =
            {
                frame: Frame
            }

            interface IEventHasFrameID with
                member this.FrameID = this.frame.id

            interface IEventMayHaveFrameID with
                member this.FrameID = Some this.frame.id

        type FrameStartedLoading =
            {
                frameId: FrameId
            }

            interface IEventHasFrameID with
                member this.FrameID = this.frameId

            interface IEventMayHaveFrameID with
                member this.FrameID = Some this.frameId

        type FrameStoppedLoading =
            {
                frameId: FrameId
            }

            interface IEventHasFrameID with
                member this.FrameID = this.frameId

            interface IEventMayHaveFrameID with
                member this.FrameID = Some this.frameId

        type LoadEventFired =
            {
                timestamp: Network.MonotonicTime
            }

        type NavigatedWithinDocument =
            {
                frameId: FrameId
                url: string
            }

            interface IEventHasFrameID with
                member this.FrameID = this.frameId

            interface IEventMayHaveFrameID with
                member this.FrameID = Some this.frameId
