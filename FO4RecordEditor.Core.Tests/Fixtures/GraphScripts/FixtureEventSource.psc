; A fixture-owned script, not a stand-in for anything Bethesda ships.
;
; It exists because a custom event handler needs a script that DECLARES the custom event, and no
; vanilla base script does. It deliberately does not live in BaseStubs: everything there is checked
; against the real game tree by BaseStubFidelityTests, and this has no real counterpart to check.

Scriptname FixtureEventSource extends Quest

CustomEvent AffinityChanged

Function RaiseIt() native
