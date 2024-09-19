
[<RequireQualifiedAccess>]
module internal SqlitePersistency

open System
open System.Globalization
open System.IO
open Microsoft.Data.Sqlite


// Computation expression that allows early exit from a wider expression.
type private Escapable () =
    member _.Return (x) =
        Some x

    member _.Combine (lhs: 'T option, rhs: unit -> 'T option) =
        match lhs with
        | Some _ -> lhs
        | None -> rhs ()

    member _.Delay (d: unit -> 'T option) =
        d

    member _.Zero () =
        None

    member _.Run (d: unit -> 'T option) =
        match d () with
        | Some v ->
            v
        | None ->
            failwith "Escapable block failed to return a value."

let private escapable =
    new Escapable ()


type SqliteDataReader with
    member this.GetStringOption ordinal =
        if this.IsDBNull ordinal then None else Some (this.GetString ordinal)

    member this.GetDateOnlyOption ordinal =
        if this.IsDBNull ordinal then
            None
        else
            DateTime.ParseExact (this.GetString ordinal, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            |> DateOnly.FromDateTime
            |> Some


module (*private*) Operations =
    let getRetirementDate (dbConn: SqliteConnection) =
        use dbCommand =
            dbConn.CreateCommand ()

        do dbCommand.CommandText <-
            "SELECT value FROM state WHERE key = 'RETIREMENT_DATE'"

        use reader =
            dbCommand.ExecuteReader ()

        do ignore <| reader.Read ()

        reader.GetDateOnlyOption 0

    let setRetirementDate (dbConn: SqliteConnection) (newRetDate: DateOnly option) =
        let newRetDateStr =
            match newRetDate with
            | None -> "NULL"
            | Some date -> sprintf "'%s'" (date.ToString ("yyyy-MM-dd"))

        use dbCommand =
            dbConn.CreateCommand ()

        do dbCommand.CommandText <-
            // Any time we change the retirement date, make sure we
            // clear out everything else.
            sprintf """UPDATE state SET value = %s WHERE key = 'RETIREMENT_DATE';""" newRetDateStr

        do ignore <| dbCommand.ExecuteNonQuery ()

    let getClientRecords (dbConn: SqliteConnection) =
        use dbCommand =
            dbConn.CreateCommand ()

        do dbCommand.CommandText <-
            "SELECT * FROM clients"

        use reader =
            dbCommand.ExecuteReader ()

        let o =
            Map.ofSeq (seq {
                for idx = 0 to reader.FieldCount - 1 do
                    yield (reader.GetName idx, idx)
            })

        let clientRecords = [
            while reader.Read () do
                yield {
                    Id = ClientID (reader.GetString (o["id"]))
                    Description = reader.GetString (o["description"])
                    Life1 = {
                        Gender =
                            match reader.GetString (o["gender_1"]) with
                            | "M" -> Gender.Male
                            | "F" -> Gender.Female
                            | g -> failwith (sprintf "Unexpected life 1 gender '%s'." g)
                        RetirementAge =
                            reader.GetInt32 (o["ret_age_1"])
                    }
                    Life2 =
                        match reader.GetStringOption (o["gender_2"]) with
                        | Some g ->
                            Some {
                                Gender =
                                    match g with
                                    | "M" -> Gender.Male
                                    | "F" -> Gender.Female
                                    | _ -> failwith (sprintf "Unexpected life 2 gender '%s'." g)
                                RetirementAge =
                                    reader.GetInt32 (o["ret_age_2"])
                            }
                        | None ->
                            None
                    Escalation =
                        match reader.GetString (o["escalation"]) with
                        | "LEVEL" -> EscalationType.Level
                        | e -> failwith (sprintf "Unexpected escalation '%s'." e)
                    Guarantee =
                        match reader.GetInt32 (o["guarantee_period"]) with
                        | 0 -> GuaranteeType.None
                        | 5 -> GuaranteeType.FiveYears
                        | g -> failwith (sprintf "Unexpected guarantee duration '%i'." g)
                    FundSize =
                        reader.GetInt32 (o["fund_size"])
                }
            ]

        clientRecords

    let setClientRecords (dbConn: SqliteConnection) (clients: ClientRecord list) =
        let clientsSQL =
            clients
            |> List.map (fun c ->
                let (ClientID cid) =
                    c.Id
                let gender1 =
                    match c.Life1.Gender with
                    | Gender.Male -> "M" | Gender.Female -> "F"
                let gender2 =
                    match c.Life2 with
                    | Some { Gender = Gender.Male } -> "'M'"
                    | Some { Gender = Gender.Female } -> "'F'"
                    | None -> "NULL"
                let retAge2 =
                    match c.Life2 with
                    | Some { RetirementAge = r } -> r.ToString ()
                    | None -> "NULL"
                let escalation =
                    match c.Escalation with
                    | EscalationType.Level -> "LEVEL"
                let gteePeriod =
                    match c.Guarantee with
                    | GuaranteeType.None -> 0
                    | GuaranteeType.FiveYears -> 5
                
                sprintf "('%s', '%s', '%s', %i, %s, %s, '%s', %i, %i)"
                    cid
                    c.Description
                    gender1
                    c.Life1.RetirementAge
                    gender2
                    retAge2
                    escalation
                    gteePeriod
                    c.FundSize
            )

        let clientsSQL =
            String.Join (",", clientsSQL)

        use dbCommand =
            dbConn.CreateCommand ()

        do dbCommand.CommandText <-
            if clients.IsEmpty then
                """DELETE FROM updated;
                   DELETE FROM clients;"""
            else
                sprintf """DELETE FROM updated;
                           DELETE FROM clients;
            
                           INSERT INTO clients (
                             id, 
                             description,
                             gender_1,
                             ret_age_1,
                             gender_2,
                             ret_age_2,
                             escalation,
                             guarantee_period, 
                             fund_size
                           ) VALUES %s;""" clientsSQL

        do ignore <| dbCommand.ExecuteNonQuery ()

    let getClientIDsPendingUpdate (dbConn: SqliteConnection) =
        use dbCommand =
            dbConn.CreateCommand ()

        do dbCommand.CommandText <-
            "SELECT id FROM clients EXCEPT SELECT id FROM updated"

        use reader =
            dbCommand.ExecuteReader ()

        // This couldn't be a sequence as we'd lose 'reader' once function returns.
        [
            while reader.Read () do
                yield ClientID (reader.GetString 0)
        ]

    let resetUpdatedClients (dbConn: SqliteConnection) =
        use dbCommand =
            dbConn.CreateCommand ()

        do dbCommand.CommandText <-
            "DELETE FROM updated;"

        do ignore <| dbCommand.ExecuteNonQuery ()

    let notifyClientUpdated (dbConn: SqliteConnection) (ClientID cid) =
        use dbCommand =
            dbConn.CreateCommand ()

        do dbCommand.CommandText <-
            sprintf "INSERT INTO updated (id, user, timestamp) VALUES ('%s', '%s', '%s')"
                cid
                Environment.UserName
                (DateTime.Now.ToString "yyyy-MM-dd HH:mm:ss")

        do ignore <| dbCommand.ExecuteNonQuery ()

    let getClientIDsPendingQuotes (dbConn: SqliteConnection) =
        use dbCommand =
            dbConn.CreateCommand ()

        do dbCommand.CommandText <-
            "SELECT id FROM clients EXCEPT SELECT id FROM quotes"

        use reader =
            dbCommand.ExecuteReader ()

        [
            while reader.Read () do
                yield ClientID (reader.GetString 0)
        ]

    let resetQuotesReceived (dbConn: SqliteConnection) =
        use dbCommand =
            dbConn.CreateCommand ()

        do dbCommand.CommandText <-
            "DELETE FROM quotes;"

        do ignore <| dbCommand.ExecuteNonQuery ()

    let notifyQuotesReceived (dbConn: SqliteConnection) (ClientID cid) quotes =
        use dbCommand =
            dbConn.CreateCommand ()

        let (user, timestamp) =
            Environment.UserName,
            (DateTime.Now.ToString "yyyy-MM-dd HH:mm:ss")

        let sqlValues =
            quotes
            |> List.map (fun (provider, result) ->
                let (quote, error) =
                    match result with
                    | Ok (r: float) -> r.ToString (), "NULL"
                    // Need to escape ' within the SQL.
                    | Error (err: string) -> "NULL", sprintf "'%s'" (err.Replace("'", "''"))

                sprintf "('%s', '%s', '%s', '%s', %s, %s)" cid user timestamp provider quote error)
            |> List.toArray         

        let concatSqlValues =
            String.Join (", ", sqlValues)

        do dbCommand.CommandText <-
            sprintf "INSERT INTO quotes (id, user, timestamp, provider, quote, error) VALUES %s"
                concatSqlValues

        do ignore <| dbCommand.ExecuteNonQuery ()


module private State =

    let awaitingAnnuityQuotes (dbConn: SqliteConnection) outstanding =
        let rec inner oustanding': State.AwaitingAnnuityQuotes =
            match oustanding' with
            | [] ->
                failwith "No clients require quotes."
            | o::os ->
                {
                    QuotesRequiredFor = o
                    ConfirmQuotes =
                        fun quotes ->
                            do Operations.notifyQuotesReceived dbConn o.Id quotes

                            match os with
                            | [] -> None
                            | _ -> Some (inner os)
                }

        inner outstanding

    let awaitingClientUpdates (dbConn: SqliteConnection) allClients outstanding =
        let rec inner outstanding': State.AwaitingClientUpdate =
            match outstanding' with
            | [] ->
                failwith "No clients require an update."
            | o::os ->
                {
                    UpdateRequiredFor = o
                    ConfirmClientUpdated =
                        fun () ->
                            do Operations.notifyClientUpdated dbConn o.Id

                            match os with
                            | [] -> Choice2Of2 (awaitingAnnuityQuotes dbConn allClients)
                            | _ -> Choice1Of2 (inner os)
                }

        inner outstanding

    let awaitingClientRecords (dbConn: SqliteConnection): State.AwaitingClientRecords =
        {
            ConfirmClientRecords =
                fun clientRecords ->
                    do Operations.setClientRecords dbConn clientRecords

                    awaitingClientUpdates dbConn clientRecords clientRecords
        }


let create dbFilePath (retDate: DateOnly) forceReset =
    let dbConnStr =
        new SqliteConnectionStringBuilder ()

    do dbConnStr.DataSource <- dbFilePath
    do dbConnStr.Mode <- SqliteOpenMode.ReadWriteCreate

    let dbConn =
        new SqliteConnection (dbConnStr.ConnectionString)

    do dbConn.Open ()

    // Apply our DB schema.
    let applySchema () =
        use dbCommand =
            dbConn.CreateCommand ()

        do dbCommand.CommandText <-
            File.ReadAllText "DbSchema.sql"

        do ignore <| dbCommand.ExecuteNonQuery ()

    do applySchema ()


    let openingState =
        escapable {
            let dbRetDate =
                Operations.getRetirementDate dbConn
    
            if dbRetDate <> Some retDate || forceReset then
                // Underlying logic will clear out clients, etc... any time this is set.
                do Operations.setRetirementDate dbConn (Some retDate)

                return State.AwaitingClientRecords (State.awaitingClientRecords dbConn)


            let clientRecords =
                Operations.getClientRecords dbConn

            // Same situation as if the retirement date has changed.
            if clientRecords.IsEmpty then
                return State.AwaitingClientRecords (State.awaitingClientRecords dbConn)
              

            let clientIDsPendingUpdate =
                Operations.getClientIDsPendingUpdate dbConn

            let clientsRequiringUpdate =
                clientRecords
                |> List.filter (fun c ->
                    clientIDsPendingUpdate 
                    |> List.contains c.Id)
                
            if not clientIDsPendingUpdate.IsEmpty then
                return State.AwaitingClientUpdate (State.awaitingClientUpdates dbConn clientRecords clientsRequiringUpdate)


            let clientIDsPendingQuotes =
                Operations.getClientIDsPendingQuotes dbConn

            let clientsRequiringQuotes =
                clientRecords
                |> List.filter (fun c ->
                    clientIDsPendingQuotes 
                    |> List.contains c.Id)

            if not clientsRequiringQuotes.IsEmpty then
                return State.AwaitingAnnuityQuotes (State.awaitingAnnuityQuotes dbConn clientsRequiringQuotes)

            return State.Complete
        }
        
    // Depending on the opening state, we may need to reset certain tables to ensure consistency.
    match openingState with
    | State.AwaitingClientRecords _ ->
        do Operations.setClientRecords dbConn []
        do Operations.resetUpdatedClients dbConn
        do Operations.resetQuotesReceived dbConn
    | State.AwaitingClientUpdate _ ->
        do Operations.resetQuotesReceived dbConn
    | State.AwaitingAnnuityQuotes _ | State.Complete ->
        ()


    { new IPersistentState with
        member _.Dispose () = do dbConn.Dispose ()
        member _.OpeningState = openingState }

