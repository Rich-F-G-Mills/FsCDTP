
module internal RequestClientQuotes

open System
open System.Text.RegularExpressions
open FSharp.Data
open FSharpx.Text
open FsToolkit.ErrorHandling
open Protocol
open Client


let [<Literal>] private screenshotFolder =
    """C:\Users\Millch\Documents\FsCDTP\SCREENSHOTS"""


let private rePensionAmount =
    new Regex("""£([0-9]+)\.00""")


let rec private awaitQuotes previousFound =
    protocolScript {
        let! quoteResultsRootNode =
            DOM.getDocument (Some 0)

        let! quoteResultsNodeIds =
            DOM.querySelectorAll quoteResultsRootNode.nodeId ".list-group-item.product-Lifetime"

        if not quoteResultsNodeIds.IsEmpty && previousFound = quoteResultsNodeIds.Length then
            return quoteResultsNodeIds

        else
            do! Async.Sleep 5_000

            return! awaitQuotes quoteResultsNodeIds.Length
    }


let private extractQuotesFromPage nid =
    protocolScript {
        let! html =
            DOM.getOuterHTML nid

        let parsedHtml =
            HtmlDocument.Parse html

        let providerName =
            parsedHtml
            |> HtmlDocument.elements
            |> List.exactlyOne
            |> HtmlNode.attributeValue "class"
            |> Strings.split ' '
            |> Array.choose (function
                | Regex.Match RegexOptions.None "provider-([A-Z]+)" { GroupValues = [providerCode] }  ->
                    let providerName =
                        match providerCode with
                        | "SL" -> "Standard Life"
                        | "LG" -> "Legal & General"
                        | "CL" -> "Canada Life"
                        | "NU" -> "Aviva"
                        | "JR" -> "Just Retirement"
                        | "SW" -> "Scottish Widows"
                        | _ -> failwith "Unknown Provider"

                    Some providerName
                | _ -> None)
            |> Array.exactlyOne

        let providerAmount =
            match html with
            | Regex.Match RegexOptions.None """£([0-9,\.]+)""" { GroupValues = [rawAmount] } ->        
                rawAmount
                |> String.filter ("0123456789.".Contains)
                |> double
                |> Ok

            | _ ->
                parsedHtml.CssSelect "ul.result-notes > li"
                |> List.map HtmlNode.innerText
                |> String.concat "|"
                |> Error

        return providerName, providerAmount
    }


let execute logger (retirementDate: DateOnly) (clientRecord: ClientRecord) =
    protocolScript {
        do logger (sprintf "--- REQUESTING QUOTES FOR CLIENT '%s' ---" clientRecord.Description)

        let (ClientID cid) =
            clientRecord.Id

        let urlQuoteOptions =
            sprintf "https://amsretirement.co.uk/ams/Client/Main/RetirementOptions/%s" cid

        let! (target, _) =
            Helpers.createTargetAndSwitchSession urlQuoteOptions

        let! stoppedLoadingEvent =  
            getEventObservable ProtocolEvent.Page_FrameStoppedLoading

        do! Page.enable

        do! awaitTimeoutAsync (TimeSpan.FromSeconds 5) stoppedLoadingEvent

        do! setCheckboxValue ("#SelectedPensions_0_", true)

        let! quoteOptionsRootNode =
            DOM.getDocument (Some 0)

        let! totalPensionNodeId =
            DOM.querySelector quoteOptionsRootNode.nodeId "#pensionTotal"

        let! totalPensionNode =
            DOM.describeNode (totalPensionNodeId, Some 1)

        let! totalPensionAmountStr =
            totalPensionNode.children
            |> Result.requireSome (ProtocolFailureReason.UserSpecified "Unexpected error selecting pension.")
            |> Result.map List.exactlyOne
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

        do! setCheckboxValue ("#ClientConsent", true)

        do! setCheckboxValue ("#HealthQuestionsRefused", true)

        do! Async.Sleep 1_000

        do! clickButton "#btnGetQuotes"

        do! awaitTimeoutAsync (TimeSpan.FromSeconds 15) stoppedLoadingEvent

        do! Async.Sleep 10_000

        let! quoteResultsNodeIds =
            awaitQuotes 0

        do logger (sprintf "   %i quotes received." quoteResultsNodeIds.Length)

        do! clickButton ".show-more-Lifetime"

        // Wait for page to respond.
        do! Async.Sleep 1_000

        let screenshotPath =
            sprintf """%s\QUOTES --- %s --- %s (LIFE 2).jpg"""
                screenshotFolder
                (retirementDate.ToString ("yyyy-MM-dd"))
                clientRecord.Description
                    
        do! takeScreenshot screenshotPath

        let! clientQuotes =
            quoteResultsNodeIds
            |> List.sequenceDispatchableM extractQuotesFromPage

        return clientQuotes

        // Have commented out so that quotes are left available for the user.
        //do! Target.closeTarget target
    }
