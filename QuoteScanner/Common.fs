
[<AutoOpen>]
module internal Common

open System


type ClientID =
    ClientID of string

[<RequireQualifiedAccess>]
type Gender =
    | Male
    | Female

type LifeDetails =
    {
        Gender: Gender
        RetirementAge: int
    }

[<RequireQualifiedAccess>]
type GuaranteeType =
    | None
    | FiveYears

[<RequireQualifiedAccess>]
type EscalationType =
    | Level

type ClientRecord =
    {
        Id: ClientID
        Description: string
        Life1: LifeDetails
        Life2: LifeDetails option
        Escalation: EscalationType
        Guarantee: GuaranteeType
        FundSize: int
    }


[<RequireQualifiedAccess>]
module State =

    type AwaitingClientRecords =
        {
            ConfirmClientRecords:
                ClientRecord list -> AwaitingClientUpdate
        }

    and AwaitingClientUpdate =
        {
            UpdateRequiredFor: ClientRecord
            ConfirmClientUpdated:
                unit -> Choice<AwaitingClientUpdate, AwaitingAnnuityQuotes>
        }

    and AwaitingAnnuityQuotes =
        {
            QuotesRequiredFor: ClientRecord
            ConfirmQuotes:
                (string * Result<float, string>) list -> AwaitingAnnuityQuotes option
        }


[<RequireQualifiedAccess>]
type State =
    | AwaitingClientRecords of State.AwaitingClientRecords
    | AwaitingClientUpdate of State.AwaitingClientUpdate
    | AwaitingAnnuityQuotes of State.AwaitingAnnuityQuotes
    | Complete


type IPersistentState =
    interface
        inherit IDisposable
        abstract member OpeningState: State with get
    end



