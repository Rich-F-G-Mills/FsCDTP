
[<AutoOpen>]
module Common

open System
open System.IO
open FSharp.Control.Reactive
open FsToolkit.ErrorHandling
open Client
open Protocol


let internal getEventObservableForCurrentSession (evt: #IEventMayHaveSessionID -> ProtocolEvent) =
    protocolScript {
        let! currentSessionId =
            getSessionID

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

let internal awaitTimeoutAsync duration obs =
    obs
    |> Observable.map ignore
    |> Observable.timeoutSpan duration
    |> Observable.ignoreElements
    |> Observable.catch (Observable.single ())
    |> Async.AwaitObservable
    |> Async.Catch
    |> Async.map (Result.ofChoice >> Result.mapError ProtocolFailureReason.Exception)

let internal takeScreenshot screenshotPath =
    protocolScript {
        let! screenshotBytes =
            Page.captureScreenshot Page.ScreenshotFormat.Jpeg           

        do File.WriteAllBytes (screenshotPath, screenshotBytes)
    }

let internal clickButton selector =
    protocolScript {
        let parameters =
            { Runtime.__Evaluate.Parameters.Default with
                expression = sprintf "$('%s')[0].click()" selector }

        let! _ =
            Runtime.evaluate parameters

        return ()
    }

let internal setComboBoxValue (selector, value) =
    protocolScript {
        let parameters =
            { Runtime.__Evaluate.Parameters.Default with
                expression =
                    sprintf "document.querySelector('%s').value = '%s'" selector value }

        let! _ =
            Runtime.evaluate parameters

        let parameters =
            { Runtime.__Evaluate.Parameters.Default with
                expression =
                    sprintf "document.querySelector('%s').dispatchEvent(new Event('change'))" selector }

        let! _ =
            Runtime.evaluate parameters

        return ()
    }

let internal getAttributes nodeId =
    protocolScript {
        let! attributes =
            DOM.getAttributes nodeId

        return Map.ofAttributeArray attributes
    }



[<RequireQualifiedAccess>]
type internal Gender =
    | Male
    | Female

type internal LifeDetails =
    {
        Gender: Gender
        RetirementAge: int
    }

[<RequireQualifiedAccess>]
type internal GuaranteeType =
    | None
    | FiveYears

[<RequireQualifiedAccess>]
type internal EscalationType =
    | Level

type internal ClientRecord =
    {
        Id: string
        Description: string
        Life1: LifeDetails
        Life2: LifeDetails option
        Escalation: EscalationType
        Guarantee: GuaranteeType
        FundSize: int
    }

type internal RunContext =
    {
        RetirementDate: DateOnly
    }