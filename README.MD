# FsCDTP
This is an F# wrapper around a subset of the [Chrome Developer Tools Protocol](https://chromedevtools.github.io/devtools-protocol/) (CDTP), including the ability to submit requests to, and intercept events from, an already running Chrome browser.

## Why?
There are no shortage of APIs targetting CDTP.

Within the .NET ecosystem alone, a simple search yields projects such as [PuppeteerSharp](https://www.puppeteersharp.com/) and [ChromeDevTools](https://github.com/MasterDevs/ChromeDevTools).

However, as a personal challenge if nothing else, I wanted to create something that was (relatively) more idiomatic for F#; this includes exposing a compuation expression builder that allows for the elegant construction of CDTP scripts.

## What's Included?
The project is split into two parts:
  - **`Protocol`**: Contains the bindings for requests, events and any associated types; these are _mostly_ aligned with definitions as represented by the [protocol](https://chromedevtools.github.io/devtools-protocol/).

  - **`Client`**: Provides the "glue" between the protocol bindings above and a running instance of Chrome.

**Please refer to the project specific _READ ME_ documents for more information.**

## Future Developments
These will occur as, when and if I decide to do them. Feel free to fork should the desire become too great!

As a personal challenge, I would like to see what I can come up with regarding unit tests.

## Warning
**Use at your own risk!**... If (including, but certainly not limited to) your computer melts or business implodes from using this... That's on you. I don't want to hear about it. Consider yourself warned.

