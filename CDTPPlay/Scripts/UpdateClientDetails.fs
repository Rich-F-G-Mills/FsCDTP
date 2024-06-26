﻿
module UpdateClientDetails

open System
open System.IO
open Protocol
open Client


let [<Literal>] private screenshotFolder =
    """C:\Users\Millch\Documents\FsCDTP\SCREENSHOTS"""


let internal execute logger runContext clientRecord =
    protocolScript {
        do logger (sprintf "--- UPDATING DETAILS FOR CLIENT '%s' ---" clientRecord.Description)

        do logger "  Opening first life client details screen..."

        let urlUpdateL1Client =
            sprintf "https://amsretirement.co.uk/ams/Client/Main/EditClient/%s" clientRecord.Id

        let! (target, _) =
            Helpers.createTargetAndSwitchSession urlUpdateL1Client

        let! stoppedLoadingEvent =  
            getEventObservable ProtocolEvent.Page_FrameStoppedLoading

        do! Page.enable

        do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent

        let! l1RootNode =
            DOM.getDocument (Some 0)

        let! l1DobNodeId =
            DOM.querySelector l1RootNode.nodeId "#DOB"

        let l1Dob =
            runContext.RetirementDate.AddYears (-clientRecord.Life1.RetirementAge)

        let newL1DobStr =
            l1Dob.ToString ("dd/MM/yyyy")

        let! currentL1DobInputAttribs =
            getAttributes l1DobNodeId

        let currentL1DobStr =
            currentL1DobInputAttribs["value"]

        if currentL1DobStr <> newL1DobStr then
            do logger "  Updating life 1 DOB."

            do! DOM.setAttributeValue (l1DobNodeId, "value", newL1DobStr)
        else
            do logger "  No life 1 DOB update needed."

        let! retDateNodeId =
            DOM.querySelector l1RootNode.nodeId "#RetirementDate"

        let newRetDateStr =
            runContext.RetirementDate.ToString ("dd/MM/yyyy")

        let! currentRetDateInputAttribs =
            getAttributes retDateNodeId

        let currentRetDateStr =
            currentRetDateInputAttribs["value"]

        if currentRetDateStr <> newRetDateStr then
            do logger "  Updating retirement date."
            
            do! DOM.setAttributeValue (retDateNodeId, "value", newRetDateStr)
        else
            do logger "  No retirement date update needed."

        if currentL1DobStr <> newL1DobStr || currentRetDateStr <> newRetDateStr then
            do logger "  Submitting changes."

            do! clickButton "#btnSave"

            do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent
        else
            do logger "  No changes to be saved."

        do logger "  Taking screenshot."

        let screenshotPath =
            sprintf """%s\%s --- %s (LIFE 1).jpg"""
                screenshotFolder
                (runContext.RetirementDate.ToString ("yyyy-MM-dd"))
                clientRecord.Description                

        do! takeScreenshot screenshotPath

        if clientRecord.Life2.IsSome then
            let life2 =
                clientRecord.Life2.Value

            let urlUpdateL2Client =
                sprintf "https://amsretirement.co.uk/ams/Client/Main/EditDependant/%s" clientRecord.Id

            do logger "  Opening second life client details screen..."

            let! _ =
                Page.navigate urlUpdateL2Client

            do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent

            let! l2RootNode =
                DOM.getDocument (Some 0)    

            let! l2DobNodeId =
                DOM.querySelector l2RootNode.nodeId "#DOB"

            let l2Dob =
                runContext.RetirementDate.AddYears (-life2.RetirementAge)

            let newL2DobStr =
                l2Dob.ToString ("dd/MM/yyyy")

            let! currentL2DobInputAttribs =
                getAttributes l2DobNodeId               

            let currentL2DobStr =
                currentL2DobInputAttribs["value"]

            if currentL2DobStr <> newL2DobStr then
                do logger "  Updating life 2 DOB."

                do! DOM.setAttributeValue (l2DobNodeId, "value", newL2DobStr)
            else
                do logger "  No life 2 DOB update needed."

            if currentL2DobStr <> newL2DobStr then
                do logger "  Submitting changes and waiting."

                do! clickButton "#btnSave"

                do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent
            else
                do logger "  No changes to be saved."

            do logger "  Taking screenshot."

            let screenshotPath =
                sprintf """%s\%s --- %s (LIFE 2).jpg"""
                    screenshotFolder
                    (runContext.RetirementDate.ToString ("yyyy-MM-dd"))
                    clientRecord.Description
                    
            do! takeScreenshot screenshotPath

        do! Target.closeTarget target
    }
