
namespace Protocol

[<RequireQualifiedAccess>]
module Network =

    let [<Literal>] private domain = "Network."


    type LoaderId =
        string

    type MonotonicTime =
        float