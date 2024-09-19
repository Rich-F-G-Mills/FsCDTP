
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

let reloadPage =
    runExpression "location.reload()"

let clickButton selector =
    runExpression (sprintf "document.querySelector('%s').click()" selector)

let private triggerEvent selector event =
    runExpression (sprintf "document.querySelector('%s').dispatchEvent(new Event('%s'))" selector event)

let private triggerChangeEvent selector =
    triggerEvent selector "change"
    
let private triggerClickEvent selector =
    triggerEvent selector "click"

let nodeIdFromSelector selector =
    protocolScript {
        let! rootNode =
            DOM.getDocument (Some 0)

        let! selectedNodeId =
            DOM.querySelector rootNode.nodeId selector

        return selectedNodeId
    }

let focus selector =
    protocolScript {
        let! selectedNodeId =
            nodeIdFromSelector selector

        do! DOM.focus selectedNodeId    
    }

let getAttributeValue selector attribute =
    protocolScript {
        let! selectedNodeId =
            nodeIdFromSelector selector

        let! attributes =
            DOM.getAttributes selectedNodeId

        let attributesMap =
            Map.ofAttributeSeq attributes

        let! attributeValue =
            attributesMap
            |> Map.tryFind attribute
            |> Result.requireSome (
                ProtocolFailureReason.UserSpecified (
                    sprintf "Unable to locate attribute '%s' for '%s'." attribute selector))

        return attributeValue
    }

let setAttributeValue selector attribute value =
    protocolScript {
        let! selectedNodeId =
            nodeIdFromSelector selector

        do! DOM.setAttributeValue (selectedNodeId, attribute, value)

        do! triggerChangeEvent selector
    }

// https://developer.mozilla.org/en-US/docs/Web/API/KeyboardEvent/key
let private simulateCharPress (chr: char) =
    protocolScript {
        let parameters =
            { Input.__DispatchKeyEvent.Parameters.Default with
                ``type`` = "char"
                text = Some (string chr)
            }

        do! Input.dispatchKeyEvent parameters
    }

let populateEditBox selector (str: string) =
    protocolScript {
        // Clear out whatever is currently shown.
        do! setAttributeValue selector "value" ""

        // Set focus so that it will pick up keyboard events.
        do! focus selector

        for chr in str do
            do! simulateCharPress chr
    }

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