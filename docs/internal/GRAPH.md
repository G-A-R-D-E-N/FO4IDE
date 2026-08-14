# The node graph compiler

A visual scripting layer over the Papyrus toolchain. A graph document becomes readable `.psc`
source, which the built-in compiler turns into a `.pex`. Everything below it already existed and is
documented in [PAPYRUS.md](PAPYRUS.md).

## Why it exists, and why it emits source

Writing a Papyrus script meant writing Papyrus. This adds an authoring surface above that.

It emits `.psc` text rather than building the syntax tree or the bytecode directly, and that is the
load-bearing decision:

- The resolver, type checker, code generator and PEX writer are reused unchanged. They are measured
  at 158 of 161 scripts instruction-identical to the Creation Kit, and that measurement carries over.
- The output is source a person can read, diff, hand-edit and check into a repository. A graph that
  produced only a binary would be a black box.
- It makes the roundtrip gate natural: generated source and hand-written source go through exactly
  the same compiler, so they can be compared instruction for instruction.

The alternative, building `PapyrusScript` nodes directly, was rejected because every mutator on that
tree is `internal` and because it would produce no readable artefact.

## Layers

| Layer | File | State |
|---|---|---|
| Document model | `GraphDocument.cs`, `GraphDocumentJson.cs` | done |
| Diagnostics | `GraphDiagnostic.cs` (`GRA####`) | done |
| Node types | `NodeDefinition.cs`, `BuiltinNodeDefinitions.cs` | done |
| Palette | `NodePalette.cs`, `WikiDocs.cs` | done |
| Type resolution | `GraphTypeResolver.cs` | done |
| Refusal engine | `GraphValidator.cs` | done |
| Control flow | `GraphExecFlow.cs` | done |
| Lowering | `GraphLinearizer.cs`, `GraphIr.cs`, `GraphNameTable.cs` | done |
| Source emission | `PapyrusSourceWriter.cs` (+ `GraphSourceMap`) | done |
| Entry point | `GraphCompiler.cs` | done |
| Tool surface | `GraphToolService.cs` | done |
| Canvas | `web/src/Blueprint/` | done |

All of it is in `FO4RecordEditor.Core`, plain `net8.0`, so it builds and tests on Linux.

## Design stances worth keeping

**Pins are never serialized.** A saved node stores its definition id, not its pins; the pins are
re-derived from the palette on load. So a parameter renamed in a base script surfaces as a dangling
wire naming the node, instead of silently compiling a call with the wrong arguments.

**Nothing is inferred pure.** Papyrus annotates no function as side-effect free, so every call,
property set and array mutation carries control flow pins and binds its result to a local at its
point in the sequence. Evaluation order is then fixed by the exec graph rather than by an ordering
analysis. The cost is that one `Game.GetPlayer()` used three times emits one local, which is what a
person would have written anyway.

**An explicit conversion is refused, never inserted.** A downcast can yield `None` at run time and a
float-to-int conversion loses information. The author owns that by placing a Cast node. Silently
inserting one would hide a real risk inside generated source they never see.

**Locals are function-scoped and hoisted.** A value produced inside one branch and read after the
merge has to outlive the branch. Hoisting makes the emitted scoping match the analysis exactly, and
it is the honest answer to "how are names mangled across nested scopes": there are no nested scopes.

**Refuse at the first stage that errors.** Inherited from `PapyrusCompiler`. A later stage would
otherwise work from a tree it has already been told is wrong, and a wrong script is worse than none
because nothing downstream reports it until the game runs it.

## Rules that are not in the documentation

Found by measurement, each now covered by a test.

- **An unwired exec output is a path out of the function.** Without modelling that, a Branch whose
  Else pin is empty appears to have one successor, its own Then target post-dominates it, and
  everything after the branch is emitted *after* the `EndIf` rather than inside it. This produced a
  silently wrong script that still compiled.
- **A script cannot resolve its own type.** `PapyrusScriptIndex` reads disk roots and has no
  in-memory injection, so a script not on a root is unresolvable even while being compiled. As soon
  as a graph passes `Self` where a base type is wanted, the checker asks whether the script inherits
  from the parameter type, fails to find the script, and refuses a valid call. `GraphCompiler`
  therefore publishes generated source to a scratch root. The write has to precede `AddRoot`:
  `AddRoot` snapshots a directory's contents into its name map and returns early the second time it
  is given the same root.
- **A struct owned by another script is written `Owner:Struct`.** The shipped
  `ObjectReference.ApplyMaterialSwap` returns `MatSwap:RemapData[]`, and the bare name does not
  resolve from a different script.
- **`Length` is a keyword.** A local named `length` will not lex, which is why the name table
  reserves every keyword rather than only the obvious identifiers.
- **`CustomEventName` and `ScriptEventName` are lexer keywords, not scripts.** Nothing declares them,
  so `ScriptObject` reports an incomplete base chain against any root set, including a real game
  install.
- **A skipped middle optional forces named arguments** for every argument after it. A positional
  emitter gets this silently wrong.

## What is measured, and how

Run from `FO4RecordEditor/`:

```
dotnet test FO4RecordEditor.Core.Tests/FO4RecordEditor.Core.Tests.csproj
```

| What | How | Result |
|---|---|---|
| Suite total | ungated, Linux net8.0 | **879 passing, 0 failed** |
| Bare-checkout parity | same run with no env vars set | **879 / 879 identical** |
| Fixture graphs compiling clean | 28 patterns, stub base tree | **28 / 28** |
| Generated instructions vs hand-written Papyrus | `PexComparer`, 28 fixtures | **28 / 28 identical** |
| Compiled object decompiles and recompiles | 28 fixtures | **28 / 28** |
| Source through the graph and back | `GraphLiftRoundTripTests`, 28 fixtures | **28 / 28 instruction-identical** |
| A compiled `.pex` through the graph and back | `GraphLiftPexRoundTripTests`, 28 fixtures | **28 / 28 instruction-identical** |
| Decompiler fidelity on the real corpus | `FO4RE_PSC_ROOTS`, 1,498 scripts | **1,202 matched, 80.2%** |
| Corpus lifting into a graph | `FO4RE_PSC_ROOTS`, 1,500 scripts | **1,414 lifted, 94.3%** |
| Negative matrix | 19 invalid graphs | **19 / 19 correct code and node id** |
| Dataflow matrix | 15 graphs, refused and accepted | **15 / 15 correct code, node and pin** |
| State matrix | 10 graphs, emission and refusal | **10 / 10 correct block, code and node** |
| Event matrix | 10 graphs, remote and custom | **10 / 10 correct signature and refusal** |
| Loop exit matrix | 11 graphs, Break and Continue | **11 / 11 correct sentinel, scope and refusal** |
| Graph to source | `FO4RE_GRAPH_BENCH`, 28 fixtures, 125 nodes | **1.47 ms median, 2.18 ms p95** |
| Translation throughput | derived from the above | **11,764 ns per node, 85,005 nodes per second** |
| Graph to `.pex` end to end | same sweep | **4.56 ms median for 28, 0.163 ms per fixture** |
| Palette build over the real base tree | `FO4RE_PSC_ROOTS`, 7,854 scripts | **1.91 s cold, 5.0 ms median warm search** |

Benchmarks were taken on a **Debug** build, 6 CPUs, .NET 8.0.29, PikaOS 4. Debug is the conservative
direction; the machine context is printed by the harness itself so a figure is never quoted without
it.

## The traps in reading those numbers

**"28 / 28 identical" is Oracle 2, not a fixed point.** The comparison that carries weight is
generated output against *hand-written* Papyrus for the same behaviour, both through the same
compiler. That is what says the graph produces what a modder would have written.

**The decompile check is deliberately weaker than instruction identity.** It proves the emitted
object is well formed enough that the decompiler reads it, produces Papyrus the parser accepts, and
that source compiles again. It does **not** compare instructions, because measurement showed the
decompiler renders implicit zero-initialisation as an explicit assignment (so recompiling adds a
second one) and, on `08_SharedCallBindsOneLocal`, drops a call whose result is discarded. Both are
decompiler fidelity limits, which PAPYRUS.md already records as bodies being best-effort. Normalising
them away to reach a green fixed point would have been inventing a result.

**100 percent clean would also describe a checker that never fires.** The negative matrix exists for
that reason: 19 deliberately invalid graphs, each asserting the exact `GRA` code and the exact node
and pin id as structured fields, never by searching message text.

**The gate runs against a reduced stub tree, not the real base scripts.** See below.

## The stub tree, and its risk

`FO4RecordEditor.Core.Tests/Fixtures/BaseStubs/` holds 33 hand-authored stand-ins for the base
scripts. They exist because `PapyrusScriptIndex` resolves from disk and has no injection path, so
without them the whole gate would only run where a game install is present, and the headline result
could not be reproduced from a bare checkout.

They are declarations only: no bodies, no documentation comments, nothing taken from the shipped
sources. Signatures are API facts also published on the Creation Kit wiki.

This is a second source of truth and it can drift. `BaseStubFidelityTests`, gated on
`FO4RE_PSC_ROOTS`, checks every declared member against the real tree: currently **135 members, 0
missing, 0 shape mismatches**. The residual risk is that nobody runs the gated sweep.

## The mistakes that compile anyway

Every graph below produces a script Papyrus accepts. None of them does what the canvas shows, and the
failure only appears once the game runs it, so the graph compiler is the only place these can be
caught. Each was found by building the graph and reading the emitted source, not by inspection.

**A returning function that falls off the end.** Papyrus hands back the return type's zero value
rather than refusing. `GraphExecFlow.PathsLeavingWithoutReturn` answers this as a greatest fixpoint
over "this node returns on every path", and `GRA0040` names the entry plus the nodes control actually
leaves through. A loop is judged by its `Completed` pin alone: a body that always returns still does
not make the loop return, because a condition false on the first test skips the body entirely.

**A value wired out of one arm of a branch.** An impure node's output becomes a local, Papyrus
declares locals at function scope and zero-initialises them, so reading one on an arm that never ran
yields 0. `GraphDefiniteAssignment` refuses this with `GRA0041`, and with `GRA0044` for the special
case of a loop reading its own body, where the fix is different.

The question is answered with **dominance**, not with emission order. Lowering walks each arm before
the merge, so by the time it reaches the merge the local looks bound; only the control flow graph
knows one arm was optional. `GraphExecFlow` therefore computes forward dominators alongside the
post-dominators the region emitter already needed. Pure producers are exempt, because they are
rebuilt inline at each use and never become a local; a read through a chain of pure nodes is
attributed to the exec node at the end of the chain, which is where the expression is evaluated.

The order-based check inside `Pull` is kept as a backstop for a producer that is not sequenced on any
path at all, which dominance does not cover because the producer is not in the flow graph.

**A named state that never reaches the source.** `StateName` was carried from the entry node all the
way to `IrCallable` and then dropped by the writer, so every handler landed in the empty state and a
two state machine ran both arms at once. `PapyrusSourceWriter` now emits the empty state first and
then one `State` block per name, with `Auto State` for the one the header names. The auto state is a
header field rather than a per entry flag, because Papyrus allows exactly one and two entries could
otherwise both claim it with no non-arbitrary way to resolve that.

**Reachability cannot be asked one entry at a time.** `GRA0022` was raised per callable, and each
entry is its own root, so every node belonging to a sibling event looked unreachable. Any script with
two events, which is most of them, got a warning per node in the other one. It is now a document-wide
pass over the union of every entry's reachable set.

All 28 fixtures still compile clean under every pass above, which is what says the checks are not
merely strict.

**A custom event handler that decompiles to the wrong name.** The compiled object names a dotted
handler `::remote_<Type>_<Name>`, and `PapyrusDecompiler` split that on `_On` to find the boundary.
That reconstructs the built-in events, whose names all start with `On`, and produces a flat
`Type_Name` for every custom event, whose name the author chooses. Both halves can contain
underscores, so the name alone cannot be split at all. The sender parameter can: every dotted handler
takes the raising object first, typed as the owner, so its type is the boundary. This is a
decompiler fix, so it reaches `decompile_papyrus` on any hand-written script too, not only graphs.

## Events raised by other objects

`Event Owner.Name(...)` covers both remote events and custom events, and the shape was measured
rather than recalled: across 697 shipped and mod scripts, all 76 dotted handlers take the raising
object first, typed as the owner. The only difference between the two is the tail, which is the
source event's parameters for a remote event and a single `Var[]` for a custom one.

They are separate palette entries from the local override rather than a flag on it, because the pin
set genuinely differs by the sender pin and pins are derived from the definition id rather than
saved. That also means a script can carry `OnLoad()` and `ObjectReference.OnLoad()` at once, which is
legal, so the duplicate check is keyed on the emitted signature's identity rather than the bare name.

`FixtureEventSource.psc` lives in `Fixtures/GraphScripts/`, not in `BaseStubs/`. A custom event
handler needs a script that declares the custom event and no vanilla base script does, and everything
under `BaseStubs` is checked member by member against the real game tree, so a script with no real
counterpart cannot live there without making that check meaningless.

## Leaving a loop early

Papyrus has no `break`, no `continue` and no `goto`, and the rewrite this needs turned out to be far
smaller than expected, because the region emitter already does most of it.

**Both nodes are terminal**, the way `Return` is. A branch holding one therefore has no
post-dominator, so the rest of the loop body is lowered into the sibling arm on its own. There is no
trailing body to guard, which is the part a sentinel rewrite normally has to handle.

**Break** becomes one `bool` per loop: reset immediately before the loop, folded into the condition
as `cond && !broke`, set true at the node. The reset sits before the loop rather than at the top of
the function so a loop nested in another one can run again on each outer pass. It is allocated
lazily, on the first Break that targets that loop, so a loop nobody breaks out of emits exactly what
it emitted before this existed.

**Continue emits nothing at all.** Falling off the end of the body is precisely where it goes. In a
`ForEach` the index step is appended after the body region rather than inside it, so a Continue still
advances the loop. Both claims are pinned by fixtures 27 and 28 against hand-written Papyrus, because
"it should be equivalent" is not a measurement.

A Break or Continue with no enclosing loop is `GRA0025`, naming the node.

Neither counts as leaving the function for the all-paths-return check: every path through one arrives
at the loop's Completed target, which is judged on its own account. A loop whose Completed is unwired
is still refused, so the neutrality did not become leniency.

**An empty Then arm is inverted rather than emitted.** A Continue leaves `If (x) Else ... EndIf`,
which is valid and which nobody writes; it comes out as `If (!x) ... EndIf`.

## Arranging the canvas

**Tidy** lays the graph out left to right along its execution flow, and the minimap sits bottom right.

Layered rather than force directed, because the graph already carries the answer: an exec wire runs
from a statement to the one after it, so column index is depth along the flow and the arrangement
reads in the same direction as the Papyrus it becomes. A force directed layout has no reason to keep
the flow pointing one way, which is the only property that matters here.

Column index is the **longest** execution path reaching a node, so nothing ever sits to the left of
something that must run before it. Two things that took a test to get right:

- **Back edges are excluded from ranking.** Relaxing across one pushed a three node cycle out to
  column nine instead of column two, one column further on every pass. They are found by DFS, which
  is the same rule `GraphExecFlow` applies on the compiler side.
- **Expression ranks may go negative.** A node with no exec pins belongs just left of whatever reads
  it; clamping at zero collapsed a chain of expressions into one column on top of itself. Columns are
  shifted back to zero afterwards.

Ordering within a column is by the average row of what feeds the node, ties broken by the position it
already had and then by id, so laying out twice does not shuffle anything. `SET_POSITIONS` is
absolute, unlike `MOVE_NODES`, so a layout is one undoable step.

The minimap draws node boxes and the viewport rectangle, not wires: at that scale a wire is a couple
of pixels of clutter. Canvas size is measured into state with a `ResizeObserver` rather than read off
the ref during render, which would be both the `react-hooks/refs` error from #148 and wrong on a
resize, since nothing would re-render to update it.

## Opening a script into the canvas

`GraphScriptLoader.Load(path, roots)` is the whole path from a file to a document: `.psc` is read
straight, `.pex` goes through `PapyrusDecompiler` first, and both then parse and lift. It lives in
Core rather than in `GraphInterop` for one practical reason, that the interop is in the WPF project
and cannot be tested on a Linux checkout. The panel's Open dialog offers all three extensions, a
refusal goes to the problems drawer rather than to the status line because it names the construct
and the line, and a freshly lifted document is run through Tidy, since the lifter positions nodes in
a plain cascade.

Two decisions in there are worth keeping:

- **The script's own folder is put in front of the import roots.** Without it a script that passes
  `Self` where its base type is wanted cannot be checked, because nothing on the roots says what
  this script extends. Every compile path here already publishes to a scratch root for the same
  reason; this is that step, not a special case.
- **A lifted script is not marked saved.** It is a new graph that exists nowhere on disk, and saying
  otherwise would let it be closed without a prompt.

### What the `.pex` round trip cost to make true

It started at **12 of 28** fixtures, and the other 16 were four decompiler defects rather than an
inherent limit. Each is worth knowing because each produced output that looked fine:

- **Locals were all hoisted to a bare declaration at the top of the body.** A bare `Bool enabled` is
  not free: it compiles to `assign enabled false`, because that is what the Creation Kit does. So
  hoisting invented an instruction whenever the original wrote the slot straight from an expression,
  and duplicated one whenever the original really did zero it. The declaration now goes on the first
  write, unless that write is inside a branch or a loop, where scoping makes hoisting the only safe
  fallback.
- **A call whose result nothing reads was dropped entirely.** It writes a temporary, temporaries are
  inlined, and inlining a temporary nobody reads discards the call with it. `GetDistance(akActionRef)`
  as a statement decompiled to an empty event.
- **A short circuit read as two separate `If`s.** `A && B` tests one temporary and jumps to a second
  test of that same temporary, so read one jump at a time it looks like an empty `If` followed by an
  unguarded one, and the first operand stops guarding anything. This was silent wrongness, not a
  formatting difference. The pair is now folded back into one condition.
- **The compiler's own cast to `Bool` was written back as an author's cast.** Both operands of `&&`
  are cast in even when already `bool` (PAPYRUS.md records this), and re-emitting that as
  `(enabled as Bool)` compiles to an `assign` instead, losing the instruction it came from.

The corpus number moved with them: **42.1% to 80.2%** over 1,498 real scripts, measured by compiling
each, decompiling, recompiling and comparing instructions (`DecompilerSweep`, gated on
`FO4RE_PSC_ROOTS`). The remaining 275 differences and 21 failures are the work list, and that sweep
prints them.

One thing that sweep is not: it compiles both ends with our compiler, so it measures the decompiler
against the compiler. `PapyrusDifferentialTests` is what anchors the compiler to the Creation Kit,
and it needs Bethesda-built `.pex` files that a Linux checkout does not have.

## What is left

- **`web/src/` outside `Blueprint/` has 92 eslint errors**, long-standing, in panels the graph work
  never touched. `src/Blueprint/` itself is clean.
- **Decompiler fidelity is 80.2%, not 100%.** Opening a `.pex` whose body falls in the remaining
  20% puts a graph on the canvas that is not quite the program in the object. The differences are
  named per script by `DecompilerSweep`.

## The frontend gate

`web/` had no test runner at all, so every frontend fix was unpinned. It now runs vitest with jsdom:

```
cd web && npm test
```

**50 tests, 4 files.** `graphReducer.test.ts` covers the only place a document is ever mutated;
`clipboard.test.ts` covers the copy logic and, more importantly, that the keys actually reach the
reducer, since the defect it closes was wiring rather than logic. `useSavedDoc.test.ts` covers the
Save button's unsaved marker.

That last one is worth a note on how to read a lint error. The marker used to compare against a
`useRef` read during render, which `react-hooks/refs` rejects. Every assignment to that ref happened
inside a helper whose `finally` toggled an unrelated `busy` flag, and that re-render recomputed the
marker, so no failure was reachable and none was found by trying. The fix is still right, for a
reason the lint rule does not state: nothing in the code expressed the dependency, so an assignment
added outside that helper would have frozen the marker silently and no test could have caught it.
Holding it as state makes it a function of the render instead of a coincidence somewhere else.

Two things about running it here:

- Installed with `--no-bin-links`, and invoked as `node node_modules/vitest/vitest.mjs`. The repo
  lives on an NTFS mount where npm cannot create the `.bin` symlinks, and the plain install fails on
  `ENOENT ... symlink`.
- `vitest.config.ts` is deliberately separate from `vite.config.ts`. Sharing one would let a change
  to how the bundle is built break the tests for reasons unrelated to either.

## Gotchas

- Node ids are opaque strings, not GUIDs. The canvas serves from a loopback origin on Linux, and
  `crypto.randomUUID` is not dependable there.
- JSON is camelCase on both sides, set once in `GraphDocumentJson`. This is the most likely place
  the C# and TypeScript halves drift.
- `PapyrusResolver.ArrayMember` is private, so the array-builtin parity test asserts behaviourally
  by resolving each member on a real array rather than by reading a list.
- The generated header uses `;` line comments, never a `{ }` doc comment: a doc comment lands in the
  compiled object's `DocString` and would ship to anyone who opens the script.
