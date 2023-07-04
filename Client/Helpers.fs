
namespace Client


[<RequireQualifiedAccess>]
module Helpers =

    open Protocol


    let attachTargetAndSwitchSession target =
        protocolScript {
            let! sessionId =
                Target.attachToTarget target

            do! switchToSession sessionId

            return sessionId
        }

    let createTargetAndSwitchSession url =
        protocolScript {
            let! newPage =
                Target.createTarget url

            let! sessionId =
                attachTargetAndSwitchSession newPage

            return newPage, sessionId
        }