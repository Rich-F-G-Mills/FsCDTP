
module RequestAnnuities

open System
open System.Text.RegularExpressions
open FsToolkit.ErrorHandling
open Protocol
open Client


let private rePensionAmount =
    new Regex("""£([0-9]+)\.00""")


let internal execute logger runContext clientRecord =
    protocolScript {
        do logger (sprintf "--- REQUESTING QUOTES FOR CLIENT '%s' ---" clientRecord.Description)

        let urlQuoteOptions =
            sprintf "https://amsretirement.co.uk/ams/Client/Main/RetirementOptions/%s" clientRecord.Id

        let! (target, _) =
            Helpers.createTargetAndSwitchSession urlQuoteOptions

        let! stoppedLoadingEvent =  
            getEventObservable ProtocolEvent.Page_FrameStoppedLoading

        do! Page.enable

        do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent

        let! quoteOptionsRootNode =
            DOM.getDocument (Some 0)

        let! totalPensionNodeId =
            DOM.querySelector quoteOptionsRootNode.nodeId "#pensionTotal"

        let! totalPensionNode =
            DOM.describeNode (totalPensionNodeId, Some 1)

        if totalPensionNode.childNodeCount = Some 0 then
            do logger "  Selecting pension."

            do! clickButton "#SelectedPensions_0_"

        else
            do logger "  Pension already selected."

        let! totalPensionNode =
            DOM.describeNode (totalPensionNodeId, Some 1)

        let! totalPensionAmountStr =
            totalPensionNode.children
            |> Result.requireSome (ProtocolFailureReason.UserSpecified "Unexpected error selecting pension.")
            |> Result.map Array.exactlyOne
            |> Result.map _.nodeValue

        let totalPensionAmountMatch =
            totalPensionAmountStr.Replace(",", "")
            |> rePensionAmount.Match

        do! totalPensionAmountMatch.Groups.Count
            |> Result.requireEqualTo 2
                (ProtocolFailureReason.UserSpecified "Unable to extract selected pension amount.")

        let! totalPensionAmount =
            match Int32.TryParse totalPensionAmountMatch.Groups[1].Value with
            | true, amount ->
                Ok amount
            | false, _ ->
                Error (ProtocolFailureReason.UserSpecified "Unable to parse total pension amount.")

        do! totalPensionAmount
            |> Result.requireEqualTo clientRecord.FundSize (ProtocolFailureReason.UserSpecified "Fund size mismatch.")

        do logger (sprintf "  Total fund value = %i." totalPensionAmount)

        do! setComboBoxValue ("#SchemeType", "ContributionOMO")

        do! setComboBoxValue ("#TFCSelection", "Nil")

        // There are 2x radio buttons with the exact name and ID.
        // However, we only care about the first which is what
        // will be targetted here.
        do! clickButton ("#SpecifyCommencementDate")

        do! setComboBoxValue ("#PaymentFrequency", "Monthly")

        do! setComboBoxValue ("#PaymentTiming", "InAdvance")

        do! setComboBoxValue ("#EscalationNPRSelection", "0")

        if clientRecord.Life2.IsSome then
            do! setComboBoxValue ("#JointLife", "True")

            do! setComboBoxValue ("#SecondIncomeNPRSelection", "50.00")

            do! setComboBoxValue ("#AnySpouse", "False")

        do! setComboBoxValue ("#GuaranteeType", "GuaranteePeriod")

        do! setComboBoxValue ("#GuaranteePeriodNPRSelection", "5")

        do! setComboBoxValue ("#Overlap", "False")

        do! setComboBoxValue ("#IsAdvised", "False")

        do! setComboBoxValue ("#NonAdvisedBasisOfSaleCategory", "NonAdvised_DirectOffer")

        do! setComboBoxValue ("#RemunerationType", "Commission")
    }
