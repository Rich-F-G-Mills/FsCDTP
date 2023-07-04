
# FsCDTP | Protocol

This project provides bindings against a subset of the published [CDTP API](https://chromedevtools.github.io/devtools-protocol/).

## Structure

Within the `Protocol` namespace, there are modules corresponding to the various API subheadings, such as `DOM`, `Network`, `Page`, etc...

In isolation, this project is of little use without the ability to actually action requests as offered by the `Client` namespace.

Each can contain one or more of the following:

  1) **Protocol specific types**
     - For example, the [`Target`](https://chromedevtools.github.io/devtools-protocol/tot/Target) subset of the API provides for a [`TargetInfo`](https://chromedevtools.github.io/devtools-protocol/tot/Target/#type-TargetInfo) type which is available here.

  2) **Protocol requests**
     - Requests are in _camelCase_ as per the published API.
     - Each request is a factory/alias for a corresponding `ProtocolRequest` object that is generic in terms of the request parameters and the corresponding response (beyond a success/failed acknowledgement).
     - The generated `ProtocolRequest` contains all the information required for a controller (`Client.ClientProtocolController`) to serialize the request and deserialize the response (once/if received) into a usable concrete type.
     - Note that for certain requests, the response is "transformed" in some way. For example, under the published API, the [`Target.attachToTarget`](https://chromedevtools.github.io/devtools-protocol/tot/Target/#method-attachToTarget) request will respond with a wrapper around a [`SessionID`](https://chromedevtools.github.io/devtools-protocol/tot/Target/#type-SessionID). Although this indirection is a consequence of the API's JSON schema, the F# binding **directly** returns the underlying `SessionID`.

  3) **Protocol events**
     - There is a single DU (`ProtocolEvent`) where each single parameter case corresponds to a given event along with its corresponding payload.
     - For example, the API provides for a [`targetCreated`](https://chromedevtools.github.io/devtools-protocol/tot/Target/#event-targetCreated) event. This is has a corresponding binding of `ProtocolEvent.Target_TargetCreated` which, when successfully deserialized by the controller, will contain a `Target.Events.TargetCreated` payload that can be obtained via pattern matching.
     - Some events implement interfaces such as `IEventHasSessionID` and `IEventMayHaveSessionID`. These can be useful when filtering on events that correspond to a given `SessionID`. Note that any event payload type that implements `IEventHasSessionID` will always implement `IEventMayHaveSessionID` as well. The only difference being that the session ID returned by the latter is represented by `Option<SessionID>`.
     - Other interfaces may be available depending on the event in question.