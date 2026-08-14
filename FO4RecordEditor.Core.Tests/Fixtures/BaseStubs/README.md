# Reduced base script stubs

Hand-authored stand-ins for the Fallout 4 base scripts, used as Papyrus import roots by the graph
test gate.

## Why these exist

`PapyrusScriptIndex` resolves import roots from disk and has no in-memory injection path. Any test
that compiles a script touching `ObjectReference`, `Actor`, `Debug` or `Game` therefore needs those
sources on disk. Without a checked-in tree, the entire graph gate would only run when
`FO4RE_PSC_ROOTS` points at a real Fallout 4 script install, which would mean the headline result
("24 of 24 fixture graphs compile clean") could not be reproduced from a bare checkout.

## What they are

Declarations only. Signatures, parameter names, defaults and inheritance, matching the real base
scripts. Function bodies are omitted by declaring members `native`, which is sufficient because
these files are only ever import roots: nothing here is compiled, so a callee's body never matters,
only its signature and return type.

`ScriptObject.GetState` and `GotoState` are real non-native functions whose bodies use the `__state`
compiler intrinsic. They are declared `native` here for the same reason.

## What they deliberately are not

Not a copy of Bethesda's shipped sources. No bodies, no documentation comments and no prose were
taken from the game's `.psc` files. Signatures are API facts, also published on the Creation Kit
wiki; the shipped source text is not redistributed here.

Not complete. Each file carries the members the fixture graphs actually use, and grows as fixtures
are added. A missing member is a compile refusal in a test, which is a loud failure, not a silent
wrong answer.

No `Institute_Papyrus_Flags.flg` is shipped. `PapyrusUserFlagTable.Fallout4Default()` already
matches that file bit for bit, and `FromFileOrDefault(null)` falls back to it, so copying Bethesda's
file in would add a redistribution question for no behavioural gain.

## The risk, stated

This tree is a second source of truth and can drift from the real base scripts. That is the price of
an ungated gate. `BaseStubFidelityTests` is the mitigation: when `FO4RE_PSC_ROOTS` is set it checks
every member declared here against the real tree and reports any signature that disagrees. The
residual risk is that nobody runs the gated sweep.
