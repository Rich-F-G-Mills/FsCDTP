# FsCDTP
An F# wrapper around a very **(!)** small subset of the [Chrome Developer Tools Protocol](https://chromedevtools.github.io/devtools-protocol/).

Feel free to fork for further development.

## Why?
There are no shortage of APIs targetting CDTP.

Within the .NET ecosystem alone, a simple search yields projects such as [PuppeteerSharp](https://www.puppeteersharp.com/) and [ChromeDevTools](https://github.com/MasterDevs/ChromeDevTools).

However, as a personal challenge if nothing else, I wanted to create something that was (relatively) more idiomatic for F#.

## What?
The project is split into two parts:
 - `Protocol`: Contains the type definitions for requests and events; these are _mostly_ aligned with definitions as represented by the [protocol](https://chromedevtools.github.io/devtools-protocol/).

 - `Client`: Contains logic which exposes:
	 - A `CDTPWebsocket` that will connect to a Chrome browser (exposing a debug port) and synchronise simultaneous reads and writes over the connection; it is backed by a `MailboxProcessor` that continuously checks for content to read and queues writes.
	 - A `Transport` that, using the web socket above, deals with the (de)serialization of JSON requests, intercepts events and ensures that each request has a unique `id` element.
	 - A `RequestBuilder` that provides a computation expression approach to specifying CDTP commands and awaiting events. For the benefit of `sessionId` aware commands, it tracks a prevailing session and automatically supplies this to those commands as required.

## Specifics
There are some specific aspects that I'm proud of and would like to bring attention to:
 - Events.
     - All events are encapsulated within a single DU.
     - This allows pattern matching on the event case.
     - One such case is `| Target_TargetCreated of Target.Events.TargetCreated`.
     - Within the request builder, you need only specify `let! targetCreated = Request.AwaitEvent Events.Target_TargetCreated`.
     - Behind the scenes, once this event is received, the logic will deserialize a corresponding type record that will be represented by `targetCreated`.
     - Simply put, we have a stringly typed return value and need only specify the corresponding DU event case.
     - This is done using the `[<ReflectedDefinition>]` attribute to quote the DU level that has been entered by the user.

 - Session ID.
     - The request builder tracks a prevailing session Id that will be passed to each request as needed.
     - The underlying `sessionId` can be updated with a call to `do! Request.SwitchToSession (Some sessionId)`.


## Future Developments
These will occur as, when and if I decide to do them.

As a personal challenge, I would like to see what I can come up with regarding unit tests.

## Warning
**Use at your own risk!**... If (including, but not limited to) your computer melts or business implodes from using this... That's on you. I don't want to hear about it. Consider yourself warned.

## Credit
Thanks is given to the following posts used in its construction:

 - [Identify DU level when partially applied](https://stackoverflow.com/questions/75735025/identify-du-level-when-partially-applied)
 - [Dynamic Invocation of Generic Functions In A Module](http://www.fssnip.net/7ZQ/title/Dynamic-Invocation-of-Generic-Functions-In-A-Module)

## Sample

    open System.Threading
    
    open FsToolkit.ErrorHandling
    
    open Protocol
    open Client
        
    [<EntryPoint>]
    let main _ =    
        asyncResult {
            use! ws =
                Websocket.create ("localhost", 9222, CancellationToken.None)
    
            use transport =
                Transport.create ws
    
            do!
                request {
                    let! targets = Target.getTargets
    
                    do! Target.setDiscoverTargets true
    
                    let! sessionId = Target.attachToTarget (targets[0].targetId)
    
                    do! Target.activateTarget targets[0].targetId
    
                    do! Request.SwitchToSession (Some sessionId)
    
                    let! _ = Page.navigate "http://www.github.com"
    
				    // Will block until user manually creates tab in browser.
                    let! targetCreated = Request.AwaitEvent Events.Target_TargetCreated
       
                    return targetCreated
                }
                |> Request.RunWithTransport transport
                |> Async.Ignore
        } |> Async.RunSynchronously
        |> ignore
    
        0