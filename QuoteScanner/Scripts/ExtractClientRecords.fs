 
module internal ExtractClientRecords

open System.Text.RegularExpressions
open FParsec
open FSharp.Core
open FsToolkit.ErrorHandling
open Protocol
open Client


let private reClientDesc =
    new Regex(@"[MF](?:&[MF])?\s+[0-9]+K\s+[0-9]+(?:&[0-9]+)?\s+(?:NG|G5)\s+L")

let private GENDER =
    choice [
        skipChar 'M' >>% Gender.Male
        skipChar 'F' >>% Gender.Female
    ]

let private WHITESPACE =
    spaces1

let private GUARANTEE =
    choice [
        skipString "NG" >>% GuaranteeType.None
        skipString "G5" >>% GuaranteeType.FiveYears
    ]

let private ESCALATION =
    choice [
        skipString "L" >>% EscalationType.Level
    ]

let private CLIENT_DESCRIPTION (clientId, description): Parser<ClientRecord, unit> =
    parse {
        let! gender1 =
            GENDER

        let! gender2 =
            opt (skipChar '&' >>. GENDER) .>> WHITESPACE

        let! fundSize =
            pint32 |>> (*) 1000 .>> skipChar 'K' .>> WHITESPACE

        let! retAge1 =
            pint32

        let! retAge2 =
            // If we have a second life gender, then we MUST
            // have a second life retirement age.
            if gender2.IsSome then
                skipChar '&' >>. pint32 |>> Some .>> WHITESPACE
            else
                preturn None .>> WHITESPACE

        let! guarantee =
            GUARANTEE .>> WHITESPACE

        let! escalation =
            ESCALATION

        let life1 = {
            Gender = gender1
            RetirementAge = retAge1
        }

        let life2 =
            if gender2.IsSome then Some {
                Gender = gender2.Value
                RetirementAge = retAge2.Value
            } else None

        return {
            Id = ClientID clientId
            Description = description
            Life1 = life1
            Life2 = life2
            Escalation = escalation
            Guarantee = guarantee
            FundSize = fundSize
        }
    }


let execute logger =
    protocolScript {
        do logger "--- EXTRACTING CLIENT INFORMATION ---"

        let! rootNode =
            DOM.getDocument (Some 0)

        let! clientTrNodeIds =
            DOM.querySelectorAll rootNode.nodeId "table.table-clients tr[data-client-id]"

        let! clientRecords =
            clientTrNodeIds
            |> List.indexed
            |> List.sequenceDispatchableM (fun (idx, nodeId) ->
                protocolScript {
                    do logger (sprintf "  Processing client #%i... " idx) 

                    let! node =
                        DOM.describeNode (nodeId, Some 0)

                    let! attribs =
                        node.attributes
                        |> Result.requireSome (ProtocolFailureReason.UserSpecified "Missing node attributes.")
                        |> Result.map Map.ofAttributeSeq

                    let! clientId =
                        attribs
                        |> Map.tryFind "data-client-id"
                        |> Result.requireSome (ProtocolFailureReason.UserSpecified "Missing client id attribute.")

                    let! linkNodeId =
                        DOM.querySelector nodeId "div.col-xs-4 > a"

                    let! linkNode =
                        DOM.describeNode (linkNodeId, Some 1)

                    let! childNodes =
                        linkNode.children
                        |> Result.requireSome (ProtocolFailureReason.UserSpecified "Missing child element.")
                                
                    let! childNode =
                        childNodes
                        |> List.tryExactlyOne
                        |> Result.requireSome (ProtocolFailureReason.UserSpecified "Only single child element expected.")

                    let clientDescStr =
                        childNode.nodeValue
                        |> reClientDesc.Match
                        |> _.Value

                    let clientParser =
                        CLIENT_DESCRIPTION (clientId, clientDescStr)

                    let! clientRecord =
                        runParserOnString clientParser () "client description" clientDescStr
                        |> function
                            | ParserResult.Success (desc, _, _) ->
                                Ok desc
                            | ParserResult.Failure (msg, _, _) ->
                                Error (ProtocolFailureReason.UserSpecified msg)

                    return clientRecord
                })                

        return clientRecords
    }
