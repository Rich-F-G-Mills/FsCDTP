
# FsCDTP | Client

This namespace provides the physical apparatus to execute protocol requests to, and intercept protocol events from, a Chrome browser instance running with the required debug settings (see [here](https://github.com/aslushnikov/getting-started-with-cdp/blob/master/README.md#protocol-fundamentals)).

## Usage

The project has been designed so that the majority of callables return an asynchronous result. Specifically, `Async<Result<'T, ProtocolFailureReason>>` for some generic type parameter `'T`.

Before any requests can be actioned, the user must provide the following:

  - An implementation of `IClientProtocolSocket` which simply provides a **thread-safe** mechanism to send a string via standard .NET websocket (`Send: string -> ...`) and an observable to intercept responses via an `IObservable<string>`.
  - An implementation of `IClientProtocolController` which provides the ability to action a `ProtocolDispatch` request (ie. a formatted `ProtocolRequest` that is ready for serialization) via `SubmitRequest` as well as intercept and events with an observable of deserialized `ProtocolEvent` objects.

Concrete implementations of these are available via `WebSocket.create` and `Controller.create` respectively. Note that both of these are returned as asynchronous results.

## Scripts

The easiest way to contrust a script of requests is with the `protocolScript` expression builder. Through the use of `let!` and `do!` constructs, it is possible to submit requests (including other `protocolScript` instances) and obtain responses as concrete types. It is also possible to action a number of other types as represented by the multiple overloads of `ProtocolScriptBuilder.Source(...)`.

Behind the scenes, all `let!` and `do!` constructs are converted into `Dispatchable<...>` instances. This is a single case DU which wraps a callable with the following signature `DispatchableState * IClientProtocolController -> Async<ProtocolOutcome<DispatchableState * 'T>>`. 

That is, when it is provided with the current state and a corresponding controller, it responds with possibly different state along with any result provided by that request.

Note that by "state", as representd by `DispatchableState`, we mean the current `SessionID` (if any) to which all session dependent requests are targetted. Note that not all requests require a prevailing session in order to work. Each `ProtocolRequest` instance (as provided by the `Protocol` library) knows whether a prevailing session is required.

Ultimately, the `protocolScript` builder returns a `Dispatchable<...>` object. Given the monadic nature in which the underlying `Dispatchable<...>` objects (among others) are composed within a script, the syntax of compuation expressions lends itself to this task.

We can then **asynchronously** run the resulting `Dispatchable<...>` using `runProtocolScript`.

## Scripting Helpers

The automatically opened `BuilderPrimitives` module exposes a number of helpful utilities. Note they are all intended to be used within a `protocolScript` build expression and as part of a `let!` or `do!` construct.

These include:

  - `switchToSession`; allows the user to switch the dispatcher state to a new session.

  - `unlinkSession`; removes the prevailing session.

  - `getSession`; get the prevaling `SessionID`.

  - `runAsChild`; is a wrapper around `Async.StartChild` and allows a `Dispatchable<...>` instance to be run as a child workflow, using the same controller as the parent workflow.

  - `getEventObservable`; returns an observable for the event as specified by the supplied `ProtocolEvent` DU case constructor. Uses `Events.chooseSpecificEvent` as discussed in the following section.

## Additional Information

  - As mentioned previously, the majority of callables return `Async<Result<'T, ProtocolFailureReason>>` where `ProtocolFailureReason` is a DU where each case corresponds to a different reason for failure.

  - The `FsToolkit.ErrorHandling` package provides elegant ways to work with such values via the `AsyncResult` module and corresponding `asyncResult` computation expression builder.

  - Any request submitted to the controller must be of type `ProtocolDispatch<'TParams, 'TResponse>`. This is a dispatch request that is ready for serialization before being sent to the browser via WebSocket. It is not anticipated that the user will ever have to deal with such a conversion as this is handled by the `protocolScript` builder.

  - Both `IClientProtocolSocket` and `IClientProtocolController` also implement `ILoggable` which provides for an observable of log messages (ie. strings). At some point, this _might_ be expanded to provide an indication of log-level as well.

  - Noting that a `ProtocolEvent` observable is made available under any implementation of `IClientProtocolController`, it is possible to both determine whether a specific event has occurred **and** extract its corresponding payload using `Events.chooseSpecificEvent: (eventConstructor: 'T -> ProtocolEvent) -> ProtocolEvent -> 'T option`. For this to work, the user need only pass in the corresponding `ProtocolEvent` DU case constructor (eg. `ProtocolEvent.Target_TargetCreated`) and an observed event.

  ## Type Extensions

  A number of type extensions are made available, including:

  - **`List...`**
    - `sequenceDispatchableM: ('T -> Dispatchable<'U>) -> 'T list -> Dispatchable<'U list>`. This was inspired by `List.sequenceResultM` as provided by the `FsToolkit` library.

  - **`Async...`**
    - `CatchAsResult: Async<'T> -> Async<Result<'T, exn>>`.
    - `GuardedAwaitObservable: IObservable<'T> -> (withAwaiter: Async<'T> -> Async<'U>) -> Async<'U>`. The function specified by `withAwaiter` will be passed a promise for the next value received from the observable. By the time `withAwaiter` is executed, we are **already** looking for the next value from the observable. This allows the user to be waiting for a particular event **before** submitting a request that will lead to said event being raised. Hence why it is referred to as `Guarded`. Note that it is fully aware of any prevailing `CancellationToken` and can be cancelled at any time.
    - `AwaitObservable: IObservable<'T> -> Async<'T>`. This is an unguarded version of that above and is also respectful of any prevailing cancellation token.
