
module internal NavigateToClientList

open System
open Protocol
open Client


let [<Literal>] private urlClientList =
    "https://amsretirement.co.uk/ams/Dashboard/Main/ClientHistory"

let execute logger =
    protocolScript {
        do logger "--- NAVIGATING TO CLIENT LIST ---"

        do logger "  Getting targets..."

        let! targets =
            Target.getTargets
            
        do logger "  Checking for an existing target..."

        let existingTarget =
            targets
            |> List.tryFind (fun { url = url } -> url = urlClientList)

        let! target =
            match existingTarget with
            | Some { targetId = tid } ->
                do logger "  Existing target found."
                Dispatchable.retn tid
            | None ->
                do logger "  Not found -> Creating new target."
                ProtocolRequest.toDispatchable (Target.createTarget urlClientList)                

        let! sessionId =
            Helpers.attachTargetAndSwitchSession target

        let! stoppedLoadingEvent =  
            getEventObservable ProtocolEvent.Page_FrameStoppedLoading

        do! Page.enable

        if existingTarget.IsNone then
            do logger "  Waiting for page load."

            do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent

        return target, sessionId
    }