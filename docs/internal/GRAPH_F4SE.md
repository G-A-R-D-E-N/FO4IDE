# The F4SE binding generator

Generates the boundary layer of an F4SE plugin: the C++ registrations, correctly typed signatures,
the paired Papyrus declarations, and the plugin shell. Sits beside [GRAPH.md](GRAPH.md) and uses the
same diagnostic scheme.

## The contract, stated up front

> The F4SE target emits the boundary, not the behaviour. Every signature, every registration, every
> marshalling type, the paired Papyrus declarations and the plugin shell are generated and kept
> consistent. Every function body arrives as a stub between preserved-region markers, guarded so it
> cannot ship unimplemented. What the native does is hand-written C++.

This is not a shortcut, it is the honest scope. A node graph describes a function body, and on the
Papyrus side that works because every node the palette offers corresponds to something the compiler
already knows how to emit. On the C++ side there is no equivalent: an F4SE native's body reaches
engine internals through hardcoded addresses, RTTI casts and structure layouts that differ per
runtime, and no machine-readable model of those exists in this repository.

The tempting subset is pure arithmetic over marshalled primitives. It is refused, and not because it
is hard. A native that only does arithmetic has no reason to be native, since Papyrus already does
arithmetic and the only motive for going native is speed a two-line function does not deliver.
Meanwhile emitting bodies would mean emitting C++ with semantics to be wrong about, in a repository
that cannot compile or run C++ at all.

## What a graph means here

An F4SE graph is a **declaration graph** (`GraphKind.F4SEBinding`), not a function-body graph. Its
nodes are declarations: a native function, a struct, a module, a plugin. Wires carry containment and
type reference only.

## Files emitted

For a plugin named `Sample` with modules `Actor` and `Util`:

```
src/PapyrusSampleActor.h        .cpp      registrations plus stubbed bodies
src/PapyrusSampleUtil.h         .cpp
src/SampleRegistrations.h       .cpp      one RegisterAllFuncs, so main.cpp never changes
src/main.cpp                              plugin entry points, target dependent
CMakeLists.txt                            find_package against an installed common + f4se
README.md                                 the four-command clone and build sequence, reproduced
Data/Scripts/Source/User/SampleActor.psc  the paired declarations
Data/Scripts/Source/User/SampleUtil.psc
```

`F4SEEmitter.Emit` returns text and **writes nothing**. That is what makes golden comparison and the
emit-then-read-back round trip trivial, and it keeps placement decisions with the caller.

## Facts the emitter is built on

All verified against the shipped source in this workspace, not recalled.

- Registration shape is
  `NativeFunction<N><CppBase, Ret, Args...>(fnName, papyrusClassName, fnPtr, vm)`.
- **The Papyrus class name is the second string constructor argument and is independent of the C++
  base type.** `TESObjectREFR` registers as `ObjectReference`, `TESNPC` as `ActorBase`,
  `BGSMod::Attachment::Mod` as `ObjectMod`. More sharply: `Form` and `DefaultObject` each register
  under *both* a real type and `StaticFunctionTag`, and `ObjectReference` under both `TESObjectREFR`
  and `VMRefOrInventoryObj`. Deriving one field from the other would be wrong for all four.
- Globals use `StaticFunctionTag` as template argument 0 and take `StaticFunctionTag*` as C++
  parameter 0.
- **Latency and `NoWait` are invisible on the Papyrus side.** `UI.psc` declares a plain native that
  `PapyrusUI.cpp` registers as `LatentNativeFunction3`. So the `.psc` is an oracle for name, class,
  arity and types only, never for either of those.
- `main.cpp` differs by target. Runtime 1.10.163 exports `F4SEPlugin_Query`; F4SE 0.7.x reads a
  `F4SEPluginVersionData` data export, introduced after the next-generation update broke plugin
  versioning. A plugin built for one will not load on the other, so the emitter has to pick. Default
  is 1.10.163, which is what this workspace targets.
- The type map was **derived, not recalled**: aligning all 225 registrations in the 0.6.23 source
  against the `native` declarations in its shipped `.psc` matched 223 of them position for position
  and produced exactly one Papyrus type per C++ type.

## Preserved regions

Bodies sit between `// >>> body: Name` and `// <<< body: Name`. Regeneration keeps everything inside
verbatim and overwrites everything outside. A body whose signature changed keeps its text and gains
a `SIGNATURE CHANGED` banner; a body whose function was deleted is reported as orphaned rather than
dropped silently. **The merge never deletes.** Losing hand-written C++ because a parameter was added
would be the worst failure this subsystem could have.

Stubs invoke `F4SE_BINDING_UNIMPLEMENTED`, which expands to a `static_assert(false, ...)` unless
`F4SE_BINDING_ALLOW_STUBS` is defined, so a half-finished plugin fails the build loudly rather than
shipping a native that silently returns zero.

## What is measured, and how

Gated sweeps opt in on `FO4RE_F4SE_SRC`:

```
FO4RE_F4SE_SRC=<dir holding f4se trees> dotnet test FO4RecordEditor.Core.Tests/FO4RecordEditor.Core.Tests.csproj
```

| What | How | Result |
|---|---|---|
| Registrations recovered, 0.7.8 | independent `RegisterFunction(` count as denominator | **228 / 228**, 0 problems |
| Registrations recovered, 0.6.23 | same | **225 / 225**, 0 problems |
| C++ types with no Papyrus mapping | both trees | **0 / 453** |
| Recovered vs `.psc` declarations, 0.7.8 | merged-minus-vanilla oracle, parsed not grepped | **227 matched, 0 mismatched** |
| Recovered vs `.psc` declarations, 0.6.23 | same | **224 matched, 0 mismatched** |
| Version delta 0.6.23 to 0.7.8 | `F4SEVersionDiff` | **3 added, 0 removed, 0 signature changes** |
| Emit then read back | 5 natives across 2 modules, 1 struct | **all matched, 0 C++-only, 0 psc-only** |
| Emitted `.psc` compiled for real | built-in Papyrus compiler | **all reach `.pex`** |

The recovery denominator counts `RegisterFunction(` rather than `new NativeFunctionN` on purpose:
keying it off the same token the scanner uses would make the ratio compare the scanner against
itself and report 100 percent regardless of what it missed.

## Named residues

Two things do not match, and both are real rather than defects:

- **`F4SE.TestInventoryFunc`** is registered in `PapyrusF4SE.cpp` and declared in no `.psc` at all.
  Reported as C++-only in both versions.
- **`ObjectReference.ApplyMaterialSwap`** returns `MatSwap:RemapData[]` in Papyrus against a C++
  `VMArray<RemapData>`. Both spellings are correct; the cross-check compares a qualified struct name
  against a bare one on the part after the colon.

One claim from the design phase was checked and found **false**: there are no commented-out
registrations in either shipped tree, line or block. Comment blanking is still done, because the
extractor also runs over generated plugins and third-party sources, but it is not fixing an observed
miscount and is not described as doing so.

## Not proven

Stated rather than implied:

- **The emitted C++ is not compiled, linked or loaded by this suite.** Nothing here can build C++.
- Latency and `NoWait` are unverifiable against a `.psc` and are reported as a separate C++-only
  census rather than cross-checked.
- The 0.7.8 `.psc` figures are **after synthetic-header repair of 19 headerless fragments**;
  `f4se-master/scripts/modified/` ships 19 of its 29 files without a `Scriptname` line.
- Struct members are not recovered by the extractor. `DECLARE_STRUCT` carries only a name and an
  owner; members are read and written by name inside function bodies.

A `g++ -fsyntax-only` stub tree was evaluated and rejected. Faithful stubs for the `NativeFunctionN`
family across eleven arities, plus `MEMBER_FN_PREFIX`, `DEFINE_MEMBER_FN` and MSVC packing, become a
second source of truth that nothing tests, and a green result against subtly wrong stubs reads as
"the C++ is fine" while proving less than the checks already in place. Every defect it would catch
is caught more directly by the emit-then-read-back round trip and the structural shape checks.

The only thing that closes this gap is a real MSVC build, run manually against an installed
`common` and `f4se`, with the date and toolchain version recorded here. That has **not** been run.
