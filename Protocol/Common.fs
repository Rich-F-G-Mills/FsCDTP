
namespace Protocol

[<AutoOpen>]
module Common =

    type SessionRequirement =
        | SessionRequired
        | SessionOptional
        | SessionNotRequired

    type ProtocolRequest<'TInput, 'TOutput, 'TOutputMapped> =
        | ProtocolRequest of SessionRequirement: SessionRequirement * Method: string * Params: 'TInput * Mapper: ('TOutput -> 'TOutputMapped)