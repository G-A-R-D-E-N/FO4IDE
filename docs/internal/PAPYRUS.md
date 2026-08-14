# The Papyrus subsystem

Everything the tool owns for reading, understanding and writing Papyrus, with no Creation Kit
involved. It lives in `FO4RecordEditor.Core/Services/Papyrus/` (about 6,500 lines) and it is Core, so
it builds and tests on Linux.

This is the engineering record for issue #78. The issue thread is the decision history; this is the
current state.

## Why it exists

`compile_papyrus` shells out to the Creation Kit's `PapyrusCompiler.exe`. No CK, no Papyrus work at
all. That dependency is the same shape as the `Archive2.exe` one, and it is why the tool could not
take a mod from source to shippable on a machine without the CK installed.

The work was split into a front end (phase 1) and a semantic half plus back end (phase 2), so value
landed before any commitment to a whole compiler. Both are built: `PapyrusCompiler.CompileFile` takes
a `.psc` and a set of roots and writes a `.pex`, with no Creation Kit anywhere in the path.

## The layers

Read top to bottom; each consumes the one above.

| File | What it is | State |
|---|---|---|
| `PapyrusToken.cs`, `PapyrusLexer.cs` | Tokens. 45 keywords, case-insensitive. | done |
| `PapyrusParser.cs`, `PapyrusAst.cs` | Hand-written recursive descent over 56 BNF productions. | done |
| `PapyrusScriptIndex.cs` | Script name to file, namespace colons as folder separators, roots in priority order, first match wins. Parse cache keyed on size + mtime. | done |
| `PapyrusFileWalk.cs` | The recursive directory walk everything else uses. | done |
| `PapyrusType.cs` | The type model and `PapyrusConversions`, the cast rules. | done |
| `PapyrusBinding.cs` | What a name turned out to be; `PapyrusResolution`, the per-script result. | done |
| `PapyrusResolver.cs` | Binds every name, types every expression. | done |
| `PapyrusTypeChecker.cs` | Judges what the resolver resolved. | done |
| `PapyrusUserFlags.cs` | `Institute_Papyrus_Flags.flg`, parsed or built in. Flag name to bit. | done |
| `PapyrusCodeGenerator.cs` | Resolved AST to `List<PexInstruction>` and a whole `PexObject`. | done |
| `PapyrusCompiler.cs` | The one entry point: parse, resolve, check, generate, write. | done |
| `PexFile.cs` | `.pex` object model and reader. 47 opcodes including the FO4 additions. | done |
| `PexStringTable.cs` | Rebuilds the string table for a file that was generated rather than read. | done |
| `PexWriter.cs` | `.pex` writer. | done |
| `PapyrusDecompiler.cs` | `.pex` to `.psc`. | done |
| `PapyrusSymbols.cs`, `PapyrusAnalysisService.cs` | Outline, hover, go-to-definition for the panel and the MCP tools. | done |

Codegen was bracketed on both sides, which is why it was worth doing last: a resolved, type-checked
AST sits above it and a writer that reproduces real compiler output byte for byte sits below it.

**`.psc` to `.pex` with no `PapyrusCompiler.exe` is now a thing this tool does**, and it is wired up:
`compile_papyrus` has two engines and picks the built-in one when no Creation Kit is installed, and
`papyrus_check` resolves names and checks types rather than only parsing. See "The surface" below.

## What is measured, and how

Every number here comes from a real corpus sweep, not from a unit test. The sweeps are opt-in on
environment variables holding roots separated by `Path.PathSeparator` (`:` on Linux, `;` on Windows),
and they no-op when unset so a bare checkout stays green.

| Sweep | Variable | Result |
|---|---|---|
| Parse | `FO4RE_PSC_CORPUS` | 18,802 files, 0 crashes, 18,782 clean; all 20 failures hand-verified as genuinely malformed source |
| Resolve | `FO4RE_PSC_CORPUS` | 7,854 vanilla files, 7,832 with complete sources, **100.00% clean** |
| Type check | `FO4RE_PSC_CORPUS` | 7,832 scripts with complete sources, **100.00% clean** |
| `.pex` round trip | `FO4RE_PEX_CORPUS` | 5,976 found, 4,581 read, **4,581 byte-identical, 0 differing** |
| Compile, differential | `FO4RE_PEX_CORPUS` + `FO4RE_PSC_ROOTS` | 198 (`.psc`, `.pex`) pairs, 161 compiled, **158 instruction-for-instruction identical to the Creation Kit (98.1%)**, 37 refused, 3 differing |
| Compile, differential, vanilla | `FO4RE_PEX_CORPUS` + `FO4RE_PSC_ROOTS` + `FO4RE_PEX_RELEASE=1` | 7,613 pairs, 7,585 compiled, **7,067 identical (93.2%)**, 28 refused |
| Decompile fidelity | `FO4RE_PSC_ROOTS` | 1,498 scripts compiled, decompiled and recompiled, **1,202 instruction-identical (80.2%)**, 275 differing, 21 not recompiled |

`FO4RecordEditor.Core.Tests` is 286 tests and runs on Linux. `FO4RecordEditor.Tests` targets
`net9.0-windows` through its WPF reference and its host does not start here, so put anything testable
without WPF in Core.Tests.

### Building the vanilla corpus, and the flag that decides what it means

The shipped `.pex` are not loose on disk: they are inside `Fallout4 - Misc.ba2`. Everything needed
to stage them is in `tools/build-vanilla-pex-corpus.sh`:

```
tools/build-vanilla-pex-corpus.sh "<FO4>/Data" papyrus/Base /tmp/vanilla-corpus
```

It extracts with the tool's own BA2 reader, then repairs two things the extractor leaves behind on
Linux: entry paths are written as literal file names with backslashes in them rather than as
directories, and the archive spells the folder both `Scripts` and `scripts`, which is two trees on a
case-sensitive filesystem. Sources are staged as `scripts/Source/` so the pairing rule finds them.
Verified end to end: **7,875 `.pex` and 7,854 `.psc`**, and a rebuild reproduces the differential
figure exactly.

**`FO4RE_PEX_RELEASE=1` is not optional on this corpus.** Bethesda's scripts are release builds with
`DebugOnly` and `BetaOnly` stripped; a mod is usually shipped debug. Rebuild a release binary in
debug mode and every script calling `Debug.Trace` reports a difference that is not a compiler
defect: `WorkshopSwitchDelayScript.OnPowerOn` begins at L28 in the shipped object because the
`debug.trace` on the line before is simply absent. Measured, getting this wrong costs **11.7 points,
59.5% against 71.2%**, and the noise buries the real differences.

### Two traps in reading those numbers

**A whole-drive root set is not a root set.** Resolving against both drives at once scores 58.8%, and
that measures the corpus rather than the resolver: 98.2% of the 20,330 reachable `.psc` share a bare
script name with another file (29 copies of `Game.psc`, 35 of `ObjectReference.psc`), so `Game` binds
to whichever copy the walk reached first. **A differential harness must compile each script against
its own roots, never against everything at once.**

**100% clean would also be a checker that never fires.** The corpus number alone proves nothing about
a judging layer. The type checker's other half is 25 tests: one per diagnostic proving it fires, one
per silence rule proving it stays quiet.

**And the compile figure is a fraction of what compiled, not of what exists.** The 37 refusals are
scripts whose roots were incomplete on this machine, every one of them reporting
`SourcesComplete == false`; they are the back end doing what it is supposed to do, not files it got
wrong. The denominator that matters is 161.

## What the differential test compares, and why it is not bytes

Compile a `.psc`, compare against the `.pex` the Creation Kit produced from the same source. The
writer's byte-identity result means that comparison *could* be exact. It is not, and the reason is
worth stating plainly rather than reading as a softened bar.

The writer is held to byte-identity because its input is a file that already exists. A code
generator's input is source, and **the Creation Kit's output is not a function of the source alone**:

- Struct members, object variables, properties and functions are written in a **hash order** that
  differs between files. `Permadeath:GraveManager`'s 21-member struct comes out scrambled, and the
  source order survives only in the debug `structOrder` table.
- Temporaries are numbered from an **object-wide counter with gaps** in it, incremented in source
  order of the functions and reused from a per-type free pool released at statement end.
- The corpus holds output from at least **four compiler builds that disagree with each other**:
  about whether `int x = f()` writes the call's result straight into `x`, whether comparing to `None`
  casts it to the target type first, and whether `struct_create` writes direct or through a
  temporary.

So the comparison is per-function instruction sequences with temporary names collapsed, which is
exactly the part that *is* a function of the source. Everything else -- operand order, operand roles,
opcode choice, jump targets, argument counts -- is compared exactly.

Two results are worth keeping:

- **`ObjectReference.psc`, Bethesda's own 55 KB script, matches instruction for instruction** when
  `DebugOnly` stripping is turned on, which is the mode it was built in. Every function, every jump
  offset, every materialised default.
- The **three remaining differences are all compiler-build variance**, each traced to a specific
  build: `PulowskShelterScript` and `RadStormFX` cast `None` to the target type before comparing,
  `NAF` routes `struct_create` through a temporary. None is a shape this generator could adopt
  without breaking the files that do it the other way.

## Rules that are not in the documentation

The published Papyrus Language Reference under-describes what the compiler accepts. Every one of
these was found by running against the corpus, not by reading harder, and each is real code the
Creation Kit compiled. Expect more.

- **A local variable may carry `const`**: `string filename = "S7System" const`. The in-function
  production has no flags at all.
- **A float literal may carry an `f` suffix**: `60.0f`. Absent from the Literals production, present
  in the wiki's own Statement Reference example.
- **A `bool` casts to `int` and to `float`**. The Cast Reference says "Floats, strings, and vars can
  be cast to integers" and omits bool, but `false as int` and `bStartAtTop as float` are in eleven
  vanilla scripts, `LightbulbOnOffScript`, `SimpleElevatorMasterScript` and `MQ206Script` among them.

### Rules read off the shipped binaries

These came from counting instructions across the whole vanilla corpus rather than from any
documentation, and each one is recorded with the count that established it, because "the Creation
Kit folds constants" is not a fact anyone can check and "28,073 against 1" is.

- **A `propset` always takes a temporary as its value.** Of the 2,520 `propset` instructions in the
  corpus, 2,520 carry a `::temp`. None carries a literal, and none carries a plain identifier, even
  where the source assigns one straight across. This is the same shape `struct_set` has, and getting
  it wrong was the largest single disagreement with the Creation Kit: 928 of 2,188 differing scripts.
- **A literal is folded into the wanted type, never cast into it.** Of 28,074 `cast` instructions,
  28,073 take an identifier as their source and exactly one takes a literal
  (`WorkshopSwitchDelayScript`, a string). So `GetStageDone(100) == 1` compares against the operand
  `true` with no cast instruction at all, the same way an int literal in a float slot is written
  `5.0`. A computed value still casts; only literals fold. A **string** in a bool slot is left to
  cast here, because what the runtime makes of it is not written down anywhere checked, and folding
  on a guess would produce a wrong value rather than a longer one.
- **Reads go left to right; writes put the value first.** Measured by tracing every temporary
  operand back to the instruction that defined it, across the whole corpus. Everything that reads is
  left to right with **no exception in more than 3,200 cases**: 2,652 for a method call's receiver
  and arguments, 99 for a static call's arguments, and the rest over every binary operator and
  `array_getelement`. Every form that writes is value first, with **no exception in 490 cases**: 447
  `propset`, 22 `array_setelement`, 21 `struct_set`. So `(GetOwningQuest() as X).Prop = 0` emits the
  `0` before it calls `GetOwningQuest`.
  <br>Because all three write forms agree, this is one rule at the assignment layer rather than three
  opcode patches. Compound assignment is excluded and has to be: it cannot compute the value before
  reading the target.
  <br>**What the corpus does not settle** is whether the write ordering is observable. For calls it
  plainly is: 495 of those 2,652 have a call on both sides, so the order the game runs them in is
  fixed and visible. For assignment there is no such case, because not one shipped script assigns a
  call's result through a receiver that is also a call. Matching the Creation Kit is right either
  way, and with no such case shipped, no vanilla script can change behaviour from it. That is a
  narrower claim than the one the call side supports, and it is worth keeping narrow.

- **A custom event name is qualified with the script that declares it, lowercased.** All 232
  custom event operands in the corpus are qualified and none is bare, and all 232 resolve to a
  script that really does declare that `CustomEvent`, across the 51 declarations in 26 scripts. The
  qualifier is lowercased in every one; the event half keeps the casing **the call site** used, not
  the declaration's.
  <br>The zero-unmatched result is what identifies *which* script: were the qualifier the target's
  concrete type, an inherited event would produce a prefix that declares nothing, and there is not
  one such case.
  <br>The event half was swept separately, and it is the reason to sweep rather than sample: 229 of
  232 match the declaration's casing, and the 3 that do not settle the rule. `BirdSpawnerScript`
  declares `customEvent flee`, `BirdCritterScript` calls it with `"Flee"`, and the shipped operand is
  `birdspawnerscript_Flee`. The call site wins. A rule read off the 229 would have been backwards for
  the other 3.

- **`Conditional` belongs to the storage, not the accessor.** It reads like a property-access
  semantic, and it is declared on the property, but the Creation Kit records it on the backing
  variable and never on the property record. Counted over the shipped corpus: the conditional bit
  appears on a property **0** times and on a backing variable **846** times, while the only bits a
  property ever carries are `Hidden` (381) and `Mandatory` (2,440). That is the reason, not merely
  the observation: `Conditional` is what lets a condition function read the slot, so it describes
  the variable the condition reads. Emitting it on both was worth 503 of 22,587 properties, and
  correcting it closed that bucket exactly.

### An observation whose mechanism is unresolved: string table case

Recorded because the measurements are solid and the conclusion is not, and because without this note
the same four experiments are cheap to repeat and expensive to finish.

**Measured, and not in doubt:**

- **A string table is case-insensitively unique.** Across all **7,875** shipped objects, not one
  table holds two entries differing only by case. Zero exceptions. So every reference to a name,
  whether a declaration, a literal or an operand, shares one spelling within an object.
- **The spelling is not chosen by any local rule.** Over 6,635 case-collapsed groups, measured
  against the source: first occurrence 67.1%, last occurrence 43.3%, declaration in the local file
  20.1%, first literal 1.4%, most frequent spelling 52.9%, lowercase 25.2%. Every previous rule in
  this document landed at or near 100%, so 67% is not a rule that fits, it is a rule that does not.
- **It is not per-API canonicalisation and not the state subsystem.** `GotoState` operands are 42%
  lowercase against `RegisterForRemoteEvent` at 0%, which no per-API rule explains. State records
  themselves disagree with their own source spelling in 170 of 656 cases, so they are a consumer of
  whatever the rule is rather than its source.

**Current hypothesis, unproven.** The spelling appears to travel with the symbol from wherever it is
declared, which is frequently another file: the kept spelling is often absent from the calling source
entirely, and the cases that lose to a local first occurrence are type and function names such as
`Keyword`, `Bool` and `Enable`, whose declarations live in the scripts that define them.

**Status: intentionally not implemented.** This investigation was concluded without a change because
every measured local winner rule was disproved. Further work means modelling compiler-wide symbol
canonicalisation rather than local string interning, which is a front end question and not a table
question.

Two further reasons it was not worth carrying on at the time. The remaining difference is about 123
scripts of 7,585, roughly 1.6%, and it is cosmetic: Papyrus compares strings and resolves states case
insensitively, so `"Off"` and `"off"` behave alike at run time. And the change would land in
`PexStringTable`, which every string in every object passes through, so a wrong winner rule would
move mismatches rather than remove them. `PexStringTable` interns with `StringComparer.Ordinal`
today, which is the concrete difference from the Creation Kit if anyone picks this up.

Two limits in the sweep that produced the percentages above, so nobody reads them as tighter than
they are: it treated keywords as names, which inflates the group count, and it only read the calling
file, which is precisely where the evidence says the answer is not.

Codegen found four more, all by disassembling real output rather than by reading:

- **A script that extends nothing still names `ScriptObject` as its parent.** `Extends` is described
  as optional and the reference stops there. 59 files in the corpus omit it and every one carries
  `ScriptObject` in the object header. `ScriptObject` itself is the single object with an empty
  parent, because it is the root.
- **`AutoReadOnly` is a constant, not storage.** It compiles to a read-only property with flags
  `0x01`, *no* backing variable and *no* autovar flag, and a generated getter whose whole body is
  `return <the literal>`. All 32 of `Form.psc`'s `kSlotMask` properties are this shape. An `Auto`
  property is the opposite: flags `0x07` and a `::Name_var` backing variable.
- **A local with no initialiser is still explicitly zeroed.** `int payment` on its own line in
  `ObjectReference.SellItem` compiles to `assign payment 0`; the slot is not assumed to arrive clean.
- **A call into `DebugOnly` or `BetaOnly` code is not compiled at all.** Not to a no-op -- it is
  absent. `ObjectReference.psc` has sixteen `debug.` calls in source and zero `callstatic debug` in
  Bethesda's compiled object, because `Debug` is declared `Scriptname Debug Native DebugOnly Hidden`.
  It is a build mode, not a language rule: three of the four compiler builds in the corpus kept such
  calls. `PapyrusCodeGenOptions.EmitDebugOnlyCode` defaults to keeping them, because silently
  deleting an author's logging is the worse failure, and only a call written as a whole statement can
  be dropped safely -- one whose value is used is refused instead.

### Instruction shapes that a spec will not tell you

These are the ones a back end has to get right and cannot derive. Each is cited to the file it was
read off.

- **An if/elseif chain ends every branch with an unconditional jump to the end, including the last
  branch when there is no else -- but never after the `else` body.** A lone `If` is therefore
  `jmpf`, body, `jmp 1`. `PulowskShelterScript.OnLoad` and `.OnActivate` nest all three shapes.
- **`&&` and `||` short-circuit through one shared bool slot.** Evaluate the left into it, `jmpf`
  (or `jmpt`) past the right, evaluate the right into the same slot; both sides are `cast` in even
  when already `bool`. `MetroCurrency:Manager` lines 71 and 84.
- **Implicit conversions are materialised.** Passing an int where a float is wanted, or a child
  object where a parent is wanted, emits an explicit `cast` into a typed temporary. `None` is the
  exception and passes raw. An int *literal* in a float slot folds to a float literal instead
  (`Utility.Wait(5)` emits the operand `5.0`); a computed int still casts.
- **Optional arguments are materialised at the call site.** `akPlayer.PlaceAtMe(GraveMarker)` emits
  five operands because `PlaceAtMe` declares five. This is the reason an unresolvable callee has to
  be refused: its arity is not derivable from the call.
- **The receiver is evaluated before the arguments.** `GetPlayer().RemoveItem(GetCaps(), n)` calls
  `GetPlayer` first (`Game.RemovePlayerCaps`), and the order is observable.
- **An auto property is its backing variable inside its own script** and `propget`/`propset` from
  anywhere else, because the backing variable is private to the declaring script.
- **A remote or custom event handler is a function named `::remote_<Type>_<Event>`**, which is also
  how `PapyrusDecompiler` recognises one coming back.
- **A struct type is written `owner#struct`** in the `.pex`, lowercased in practice --
  `hydra:events#itemaddremoveparams`. It is the one type name the format does not spell the way the
  source does.
- **`struct_set` always takes a temporary as its value operand**, even where the source assigns a
  parameter straight across: `kVector.fX = afX` is `assign ::temp1 afX` then `struct_set`.
- **There is no `cmp_ne`.** Inequality is `cmp_eq` followed by `not` into the same slot.
- **Type-name case is not canonical and never was.** The same compiler writes `ObjectReference` in
  one slot and `objectreference` in another. Papyrus is case-insensitive throughout; do not chase it.

And two language facts that are documented but easy to get backwards:

- **A global function has no `Self`, but its own script's global functions stay callable
  unqualified.** Reading "no Self variable" as "no members at all" reported 104 vanilla call sites as
  undefined. `Debug`, `Game` and `Utility` are entirely globals calling each other by bare name.
- **A name in call position is never a local.** Papyrus has no function values and is
  case-insensitive, so a call can match a local by accident.
  `Inst307_ZoneQuestRespawnScript` calls `RespawnCollection(...)` from a function whose parameter is
  named `respawnCollection`.

### The cast table, which is not guessable

From the Cast Reference's "Compiler auto-cast from" line per target type. Implemented once in
`PapyrusConversions` and shared by the resolver and the checker.

| Target | Implicit from | Explicit also from |
|---|---|---|
| `bool` | anything | |
| `string` | anything | |
| `float` | `int` | `string`, `bool`, `var` |
| `int` | nothing | `float`, `string`, `bool`, `var` |
| object | a child object, `None` | a parent object (may be `None` at runtime), `var` |
| array | `None` | another array whose elements cast |
| struct | `None` | nothing |
| `var` | everything but arrays | |

## Design stances worth keeping

**Missing sources are never errors.** A script whose parent or import the index cannot find has
reporting switched off, and `PapyrusResolution.BaseChainComplete` says so. Otherwise every inherited
member looks undefined. This is the single decision that makes the layer usable on a real modlist.

**Silence beats a false positive.** The checker reports nothing when sources were incomplete, when
either side is the `Error` type, or when a callee has no source declaration to check against. Array
built-ins are that last case: real members with real signatures declared in no `.psc`, so their
arguments go unchecked rather than wrongly checked. A checker that cries wolf on working code gets
ignored, and an ignored checker is worth nothing.

**Restated for the back end: emit nothing you cannot justify.** Codegen refuses rather than guesses.
A callee with no source declaration on the roots has unknown arity the moment optional parameters
exist, so it produces a diagnostic and no file instead of an instruction that is the right shape and
the wrong length. `GetMatchingStructs` is documented on the Arrays page and has no Fallout 4 opcode,
so it is refused rather than approximated. A wrong `.pex` is worse than none, because nothing
downstream will tell you it is wrong until the game runs it.

**Do not build on Mutagen's `Pex` layer.** It has a writer and looks like a free back end. It fails
**65%** of the corpus on read, and of what it does read, 5 files round-trip byte-identical out of
2,735. Ours reads 100% and the writer inverts it.

## `.pex` format facts

Established by making a writer and demanding byte-identity, not from a spec.

- **The per-object `size` field has two conventions in the wild.** 1,480 of 1,496 loose `.pex`
  measured count the field's own four bytes; 16 count only the body. `PexObject.SizeIncludesItself`
  records which, and the default is the majority. The producing toolchain is not identified: the
  `Papyrus`/`Compiler` user/machine stamp does not separate them, since 1,321 differently-stamped
  files still use the majority convention.
- **Some files carry bytes past the last object.** `PexFile.TrailingBytes`.
- **The string table is reused verbatim rather than rebuilt**, so indices land where they were. No
  file in the corpus has a duplicate string, so first-occurrence lookup is safe.
- **Other games share the magic.** Starfield is game id 4 at format 3.12 and has value type tags
  Fallout 4 never emits; Skyrim LE is big-endian. Both are rejected up front by name now, because a
  Starfield file used to fail hundreds of bytes late as `Invalid value type tag 14`. One such file
  exists on the development machine: a Fallout 4 script built with a Starfield compiler.

## The Creation Kit dependency is gone

`Institute_Papyrus_Flags.flg` declares the user flags (`Hidden`, `Conditional`, `Default`,
`CollapsedOnRef`, `CollapsedOnBase`, `Mandatory`) and the `.pex` user-flag table is built from it. It
ships with the Creation Kit and is **not** in the game archives, so it was the last thing standing
between this subsystem and "compiles with no CK installed".

`PapyrusUserFlags.cs` closes it. The file is sixty lines of declarations in a three-form grammar its
own header comment states, so it is parsed when it is present, and otherwise supplied from a built-in
table. That table is not a guess: it is the one every real `.pex` measured carries, and it agrees bit
for bit with the shipped file. A composite (`Flag Collapsed CollapsedOnRef & CollapsedOnBase`) owns no
bit of its own and expands to a mask, which the file itself says -- "This flag will NOT appear in the
object, only the ones it is made up of".

Real files write their flag table in a hash order that differs between them; this writes ascending
bit order, which is deterministic.

## The surface

Two existing tools grew; **no tool was added, so the count is still 105.** A new
`papyrus_compile_native` beside `compile_papyrus` would have meant two ways to do one thing and a
caller having to know which, which is the failure mode the Workshop Menu Editor note in KNOWLEDGE.md
is about.

**`compile_papyrus` has an `engine`.** `auto` is the default and prefers an installed Creation Kit,
so nothing about an existing setup changes; it uses the built-in compiler when there is no
`PapyrusCompiler.exe`, which is the case this whole issue existed for. `builtin` and `creationkit`
force one. `release` means the same thing to both: strip `DebugOnly` and `BetaOnly` calls, the CK's
`-r`. `optimize` is a CK switch and is reported as ignored rather than silently dropped.

**`papyrus_check` resolves names and checks types**, not just parses. `semantic=true` is the default.
The summary counts three things separately -- clean, syntax errors, name or type errors -- and counts
files whose sources were incomplete separately again, because that layer switches its reporting off
for those and "clean" must never quietly mean "could not tell".

**The Papyrus panel** gained an engine picker in compile mode, and hides the compiler-path field and
`-op` when the built-in engine is selected, since neither applies to it.

Both routes go through `PapyrusAnalysisService`, which is Core, so the native Linux build
(`FO4RecordEditor.Server`) gets all of it.

### The one honest caveat

**No `PapyrusCompiler.exe` is not the same as no Creation Kit install.** The built-in engine still
needs the vanilla base script *sources* on the import path to resolve `Form`, `ObjectReference`,
`Actor` and the rest, and those ship with the CK rather than in the game archives.

That is a much weaker requirement than the old one and worth being precise about rather than
overclaiming. The sources are plain `.psc` text, they are redistributed with plenty of modding
resource packs, and they are not Windows-only -- whereas `PapyrusCompiler.exe` is a non-redistributable
Windows binary. So the tool now compiles on a Linux box, in CI, or on any machine that has the script
sources lying around; it does not compile out of thin air. When no base root is detected at all,
`compile_papyrus` says exactly that instead of listing thirty unresolved type names.

Roots come from three places, in order: the source's own folder plus its `Source/User` and `Source`
ancestors (`PapyrusAnalysisService.NaturalRootsFor`, because a namespaced script is named relative to
`Source/User`, not to its own folder), then the caller's `imports`, then
`ToolPaths.PapyrusBaseImports()`.

## Two declarations of one name

The compiler used to accept these with no diagnostic at all, and the code generator wrote **both**
into the object. A state whose function list holds two entries called `OnLoad` is not a valid `.pex`,
and which one the game runs is not something the source says. Merging two scripts by hand is the
ordinary way to produce one.

`PapyrusDeclarationCheck` raises `PAP0020` for it, and runs **before** name resolution: a duplicate
makes the symbol table ambiguous, so resolving first would bury the real problem under a cascade of
its own consequences.

The scopes are the ones the emitted object actually uses, not the ones the source suggests. Functions
and events share a single scope **per state**, because both land in that state's function list; the
same event name in two different states is the point of a state machine and stays legal.

The uniqueness key is the name a declaration will be **emitted** under, not the name as written. A
remote handler compiles to `::remote_Type_Name`, so `Event OnLoad()` and
`Event ObjectReference.OnLoad(...)` are two different functions that legitimately sit side by side:
one overrides this script's own event, the other listens to another object's. Keying on the bare name
refuses a script the game accepts, and that mistake was made here first and caught by the graph
suite before it shipped.

`VanillaSweep`, gated on `FO4RE_PSC_ROOTS`, points the check at the shipped sources: **7,854 files
parsed, 0 unparseable, 0 flagged.** A refusal added to a compiler is only worth as much as the
evidence that it refuses nothing real, and the 33-file stub tree cannot supply that.

## What is left

Known gaps in the layers that are built, stated rather than hidden:

- Array built-in argument **types** are unchecked (nothing declares them in any `.psc`). Their
  **counts** are checked, but only at the back end, in `PapyrusCodeGenerator.ArrayBuiltinArity`, so
  `papyrus_check` will not catch a miscounted one -- `compile_papyrus` will.
- Override checking compares parameter **count**, not parameter types or return type.
- No definite-assignment and no all-paths-return analysis.
- `GetMatchingStructs` has no Fallout 4 opcode and is refused rather than approximated.
- The `array_findstruct` member-name operand is emitted as the source's string literal. No file in
  the corpus uses that opcode, so the operand's value type is the one shape here that is reasoned
  from the format rather than read off real output.
- Named states, `callparent` and full property handlers are likewise unrepresented in the corpus.
  They are emitted from the format and covered by unit tests, not by the differential sweep.
- **Decompiled bodies are right 80.2% of the time, measured** (`DecompilerSweep`), up from 42.1%
  once four defects were fixed: locals hoisted to a declaration that compiles to a zero assignment
  the original never had, a call whose result nothing reads dropped along with its temporary, a
  short circuit read as two separate `If`s so the first operand stopped guarding anything, and the
  compiler's own cast to `Bool` written back as an author's cast. GRAPH.md carries the detail,
  because opening a `.pex` into the canvas is what made them matter. The remaining 275 differences
  are named per script by that sweep. Declarations are still exact; it is bodies that are best
  effort, and that has not changed.

## Gotchas

- `dotnet build` on `FO4RecordEditor.csproj` needs `-p:EnableWindowsTargeting=true` on Linux.
- An XML comment in a `.csproj` cannot contain `--` (MSB4025).
- .NET's directory enumeration skips `Hidden` and `System` by default, and its recursive form aborts
  the whole walk on the first directory it cannot open. A drive that has run a game under Proton
  carries compatdata symlinks into `/proc` that throw, and a prefix's `dosdevices/z:` points at the
  filesystem root, so following symlinks turns a per-drive walk into a whole-machine one. Use
  `PapyrusFileWalk`.
- The Papyrus panel gutter's CSS `line-height` must equal `EDITOR_LINE_HEIGHT` in `PapyrusPanel.tsx`
  (19px), or every jump-to-line drifts.
- A generated `PexFile` has no string table until `RebuildStringTable()` is called. `PexWriter`
  throws on a string it cannot index rather than appending one, deliberately: a silently-added entry
  would shift no index but would let a genuinely inconsistent model through.
- **Anything that lists `.psc` files must use `PapyrusFileWalk`, including the file list, not just
  the index.** `PapyrusAnalysisService.ResolveSources` used the framework's recursive enumeration and
  therefore skipped Hidden and System, so a scripts tree inside any dot-prefixed folder -- a git
  checkout, a worktree -- was invisible to a folder compile while the resolver read it happily. The
  two front doors disagreed about which files exist. Fixed, and there is a test that puts a script
  under a dotted directory and demands both see it.
- **Array built-ins are the one call shape with no arity check above the back end.** They are
  declared in no `.psc`, which is exactly why the type checker leaves their arguments unchecked, so
  `xs.Add()` parses, resolves, type-checks and reaches codegen. Reading operand zero of an empty
  argument list is a crash, not a refusal: the counts live in
  `PapyrusCodeGenerator.ArrayBuiltinArity` and anything outside them is reported.
- **Fallout 4 auto-detection could never work on Linux.** `ToolPaths.Fallout4Root`'s Steam probe
  joined its relative paths with backslashes, which are ordinary filename characters off Windows, so
  it looked for one file literally named `SteamLibrary\steamapps\common\Fallout 4` in each mount
  root. It builds them from segments now and also probes the two standard Linux Steam locations
  under the home directory.
