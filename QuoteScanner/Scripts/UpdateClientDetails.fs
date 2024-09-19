
module internal UpdateClientDetails

open System
open Protocol
open Client


let [<Literal>] private screenshotFolder =
    """C:\Users\Millch\Documents\FsCDTP\SCREENSHOTS"""


let execute logger (retirementDate: DateOnly) clientRecord =
    protocolScript {
        do logger "  Opening first life client details screen..."

        let (ClientID cid) =
            clientRecord.Id

        let urlUpdateL1Client =
            sprintf "https://amsretirement.co.uk/ams/Client/Main/EditClient/%s" cid

        let! (target, _) =
            Helpers.createTargetAndSwitchSession urlUpdateL1Client

        let! stoppedLoadingEvent =  
            getEventObservable ProtocolEvent.Page_FrameStoppedLoading

        do! Page.enable

        do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent

        let l1Dob =
            retirementDate.AddYears (-clientRecord.Life1.RetirementAge)

        let newL1DobStr =
            l1Dob.ToString ("dd/MM/yyyy")

        let! currentL1DobStr =
            getAttributeValue "#DOB" "value"

        if currentL1DobStr <> newL1DobStr then
            do logger "  Updating life 1 DOB."

            do! populateEditBox "#DOB" newL1DobStr
        else
            do logger "  No life 1 DOB update needed."

        let newRetDateStr =
            retirementDate.ToString ("dd/MM/yyyy")

        let! currentRetDateStr =
            getAttributeValue "#RetirementDate" "value"

        if currentRetDateStr <> newRetDateStr then
            do logger "  Updating retirement date."

            do! populateEditBox "#RetirementDate" newRetDateStr
        else
            do logger "  No retirement date update needed."

        if currentL1DobStr <> newL1DobStr || currentRetDateStr <> newRetDateStr then
            do logger "  Submitting changes and waiting."

            // Make sure all keyboard inputs have been flushed through.
            do! Async.Sleep 1_000

            // Need to draw focus away from the text boxes so that changes are recognised.
            do! focus "#search"

            do! clickButton "#btnSave"

            do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent

            // By reloading the page we better ensure that our changes have been
            // persisted before taking the screenshot.
            do! reloadPage

            do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent
        else
            do logger "  No changes to be saved."

        do logger "  Taking screenshot."

        let screenshotPath =
            sprintf """%s\INPUTS --- %s --- %s (LIFE 1).jpg"""
                screenshotFolder
                (retirementDate.ToString ("yyyy-MM-dd"))
                clientRecord.Description                

        do! takeScreenshot screenshotPath

        if clientRecord.Life2.IsSome then
            let life2 =
                clientRecord.Life2.Value

            let urlUpdateL2Client =
                sprintf "https://amsretirement.co.uk/ams/Client/Main/EditDependant/%s" cid

            do logger "  Opening second life client details screen..."

            let! _ =
                Page.navigate urlUpdateL2Client

            do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent

            let l2Dob =
                retirementDate.AddYears (-life2.RetirementAge)

            let newL2DobStr =
                l2Dob.ToString ("dd/MM/yyyy")          

            let! currentL2DobStr =
                getAttributeValue "#DOB" "value"

            if currentL2DobStr <> newL2DobStr then
                do logger "  Updating life 2 DOB."

                do! populateEditBox "#DOB" newL2DobStr
            else
                do logger "  No life 2 DOB update needed."

            if currentL2DobStr <> newL2DobStr then
                do logger "  Submitting changes and waiting."

                do! Async.Sleep 1_000

                do! focus "#search"

                do! clickButton "#btnSave"

                do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent

                do! reloadPage

                do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent
            else
                do logger "  No changes to be saved."

            do logger "  Taking screenshot."

            let screenshotPath =
                sprintf """%s\INPUTS --- %s --- %s (LIFE 2).jpg"""
                    screenshotFolder
                    (retirementDate.ToString ("yyyy-MM-dd"))
                    clientRecord.Description
                    
            do! takeScreenshot screenshotPath

        do! Target.closeTarget target
    }
