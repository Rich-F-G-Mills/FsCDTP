
[<AutoOpen>]

[<AutoOpen>]
module internal ScriptingHelpers

open System.IO
open FSharp.Control.Reactive
open FsToolkit.ErrorHandling
open Client
open Protocol


let getEventObservableForCurrentSession (evt: #IEventMayHaveSessionID -> ProtocolEvent) =
    protocolScript {
        let! currentSessionId =
            getSession

        let! currentSessionId =
            currentSessionId
            |> Result.requireSome (ProtocolFailureReason.UserSpecified "No prevailing session available.")

        let! eventObservable =
            getEventObservable evt

        let requiredEventObservable =
            eventObservable
            |> Observable.filter (fun evtParams ->
                let eventSessionId =
                    evtParams.SessionID
                    
                eventSessionId = Some currentSessionId)

        return requiredEventObservable
    }

let awaitTimeoutAsync duration obs =
    obs
    |> Observable.map ignore
    |> Observable.timeoutSpan duration
    // We don't care about any elements that we actually receive. Ignore them.
    |> Observable.ignoreElements
    |> Observable.catch (Observable.single ())
    |> Async.AwaitObservable
    |> Async.Catch
    |> Async.map Result.ofChoice
    |> Async.map (Result.mapError ProtocolFailureReason.Exception)

let takeScreenshot screenshotPath =
    protocolScript {
        let! screenshotBytes =
            Page.captureScreenshot Page.ScreenshotFormat.Jpeg           

        do File.WriteAllBytes (screenshotPath, screenshotBytes)
    }

let private runExpression expression =
    protocolScript {
        let parameters =
            { Runtime.__Evaluate.Parameters.Default with
                expression = expression }

        let! _ =
            Runtime.evaluate parameters

        return ()
    }    

let clickButton selector =
    runExpression (sprintf "document.querySelector('%s').click()" selector)

let private triggerEvent selector event =
    runExpression (sprintf "document.querySelector('%s').dispatchEvent(new Event('%s'))" selector event)

let private triggerChangeEvent selector =
    triggerEvent selector "change"
    
let private triggerClickEvent selector =
    triggerEvent selector "click"

let setComboBoxValue (selector, value) =
    protocolScript {
        do! runExpression (sprintf "document.querySelector('%s').value = '%s'" selector value)

        do! triggerClickEvent selector

        do! triggerChangeEvent selector

        return ()
    }

let setCheckboxValue (selector, value: bool) =
    protocolScript {
        let value' =
            if value then "true" else "false"

        do! runExpression (sprintf "document.querySelector('%s').checked = %s" selector value')

        do! triggerClickEvent selector

        do! triggerChangeEvent selector

        return ()
    }

let getAttributes nodeId =
    protocolScript {
        let! attributes =
            DOM.getAttributes nodeId

        return Map.ofAttributeSeq attributes
    }