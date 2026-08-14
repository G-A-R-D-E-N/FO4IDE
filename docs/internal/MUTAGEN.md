# Mutagen Reference

Mutagen is the C# record library that Spriggit is built on. Use it directly when
you want to **generate** records programmatically with FormKey validation and
automatic link resolution, rather than hand/script-authoring JSON.

Source: the vendored Mutagen tree (solutions, generators, per-game projects). For FO4 work
the relevant solution is `Mutagen.Records.Fallout4.sln` and the package
`Mutagen.Bethesda.Fallout4`.

The copy FO4RecordEditor actually builds against is the trimmed one at
`FO4RecordEditor\Mutagen\` (Kernel/Core/Fallout4 only).

## Spriggit (JSON) vs Mutagen (C# API) - when to use which

| | Spriggit | Mutagen |
|---|---|---|
| Approach | text serialization | typed C# API |
| FormID validation | manual | automatic (`FormKey.TryFactory`, resolve) |
| Generation | author/script JSON | type-safe record objects |
| Link resolution | manual references | auto-resolve against load order |
| Best for | diffing, copying vanilla records, git | bulk generation, integrity checks |

Use Spriggit when you are editing or copying existing records and want clean git
diffs. Use Mutagen when you are generating many records and want the compiler +
load order to catch broken FormKeys before you ship.

## Minimal generator shape (C#)

```csharp
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;

// 1. New plugin
var mod = new Fallout4Mod(ModKey.FromNameAndExtension("MyMod.esp"));

// 2. Reference a vanilla record by FormKey ("XXXXXX:Plugin.esm")
var endurance = FormKey.Factory("0002C4:Fallout4.esm");

// 3. Create a typed record (auto-assigns next FormID in the new mod)
var glob = mod.Globals.AddNewFloat();
glob.EditorID = "MyMod_SomeFloat";
glob.Data = 0f;

var mgef = mod.MagicEffects.AddNew();
mgef.EditorID = "MyMod_ReduceEND";
mgef.Name = "Radiation Sickness";
mgef.Archetype = new MagicEffect.PeakValueModArchetype { ActorValue = endurance };

// 4. Write binary
mod.WriteToBinary("E:\\out\\MyMod.esp");
```

## FormKey validation pattern (from the Rad Mod generator)

```csharp
// Parse "XXXXXX:Plugin.esp" safely
if (!FormKey.TryFactory("023742:Fallout4.esm", out var key)) { /* malformed */ }

// Verify it actually exists by resolving against a load order / env
using var env = GameEnvironment.Typical.Fallout4(Fallout4Release.Fallout4);
if (env.LinkCache.TryResolve<IIngestibleGetter>(key, out var rec)) { /* valid */ }
else { /* log missing FormKey, do not emit a broken reference */ }
```

Validate every referenced FormKey before writing so the build fails loudly
instead of shipping a dangling reference.

## Build/run a Mutagen generator project

```powershell
cd <generator project>
dotnet restore
dotnet build -c Release
dotnet run --project RadiationESPGenerator.csproj --configuration Release
```

## Notes
- `AddNew*` assigns the next free FormID in the new mod automatically; set your
  own only when a FormID is part of a contract with a DLL (see PIPELINE/FormID map).
- Declare master references explicitly when overriding records owned by DLCs.
- Mutagen and Spriggit must agree on the record schema; both here track FO4 / the
  0.40.1-era record layout. If you regenerate JSON with one and compile with the
  other, keep versions consistent.
- Synthesis is the Mutagen-based runtime patcher framework if
  you want load-order-aware patches rather than a standalone plugin.
