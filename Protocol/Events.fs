
namespace Protocol


type UnserializedProtocolEvent =
    { Method: string; Params: string }

[<RequireQualifiedAccess>]
type ProtocolEvent =
    | Page_FrameDetached of Page.Events.FrameDetached
    | Page_FrameNavigated of Page.Events.FrameNavigated
    | Page_FrameStartedLoading of Page.Events.FrameStartedLoading
    | Page_FrameStoppedLoading of Page.Events.FrameStoppedLoading
    | Page_LoadEventFired of Page.Events.LoadEventFired
    | Page_NavigatedWithinDocument of Page.Events.NavigatedWithinDocument
    | Runtime_ConsoleAPICalled of Runtime.Events.ConsoleAPICalled
    | Runtime_ExceptionRevoked of Runtime.Events.ExceptionRevoked
    | Target_AttachedToTarget of Target.Events.AttachedToTarget
    | Target_DetachedFromTarget of Target.Events.DetachedFromTarget
    | Target_TargetCrashed of Target.Events.TargetCrashed
    | Target_TargetCreated of Target.Events.TargetCreated
    | Target_TargetDestroyed of Target.Events.TargetDestroyed
    | Target_TargetInfoChanged of Target.Events.TargetInfoChanged
    | Target_ReceivedMessageFromTarget of Target.Events.ReceivedMessageFromTarget
    | Unknown of UnserializedProtocolEvent