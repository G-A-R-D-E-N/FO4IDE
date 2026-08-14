using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph.F4SE;

/// <summary>Which F4SE plugin ABI an emitted plugin targets.</summary>
/// <remarks>
/// The two differ in how the runtime learns a plugin's version, and a plugin built for one will not
/// load on the other. <c>F4SEPlugin_Query</c> is the original entry point;
/// <c>F4SEPluginVersionData</c> is a data export the next generation runtime introduced after plugin
/// versioning broke. Both templates ship, and the default is the runtime this workspace targets.
/// </remarks>
public enum F4SETarget
{
    /// <summary>Runtime 1.10.163, the original. Exports <c>F4SEPlugin_Query</c> and <c>F4SEPlugin_Load</c>.</summary>
    Og_1_10_163 = 0,

    /// <summary>F4SE 0.7.x and later. Exports <c>F4SEPluginVersionData</c> and <c>F4SEPlugin_Load</c>.</summary>
    Ng_0_7_8 = 1,
}

/// <summary>One parameter of a native binding.</summary>
public sealed record NativeParameter(
    string Name,
    PapyrusTypeText Type,
    string? DefaultLiteral = null,
    bool Unsigned = false)
{
    public bool IsOptional => DefaultLiteral != null;
}

/// <summary>
/// One Papyrus native function: the whole boundary, in the form both emitters render from.
/// </summary>
/// <remarks>
/// <see cref="PapyrusClass"/> and <see cref="CppBaseType"/> are separate fields on purpose. The
/// registration's first template argument is a C++ type and its second constructor argument is the
/// Papyrus class name, and they are genuinely independent: the shipped source registers
/// <c>ObjectReference</c> against both <c>TESObjectREFR</c> and <c>VMRefOrInventoryObj</c>, and
/// registers <c>Form</c> and <c>DefaultObject</c> against both a real type and
/// <c>StaticFunctionTag</c>. Deriving one from the other would be wrong for those.
/// </remarks>
public sealed record NativeBinding
{
    public required string FunctionName { get; init; }

    /// <summary>The Papyrus class the function is registered under.</summary>
    public required string PapyrusClass { get; init; }

    /// <summary>
    /// The C++ receiver type, or <see cref="F4SETypeMap.StaticFunctionTag"/> for a global.
    /// </summary>
    public required string CppBaseType { get; init; }

    public PapyrusTypeText ReturnType { get; init; } = new("None");

    public IReadOnlyList<NativeParameter> Parameters { get; init; } = Array.Empty<NativeParameter>();

    /// <summary>Whether the return value is emitted as <c>UInt32</c> rather than <c>SInt32</c>.</summary>
    public bool ReturnUnsigned { get; init; }

    public bool IsGlobal => CppBaseType == F4SETypeMap.StaticFunctionTag;

    /// <summary>
    /// Registered as <c>LatentNativeFunctionN</c>.
    /// </summary>
    /// <remarks>
    /// Invisible on the Papyrus side: the shipped <c>UI.psc</c> declares a plain native that the
    /// C++ registers as latent. So this can never be recovered from, or checked against, a
    /// <c>.psc</c>.
    /// </remarks>
    public bool IsLatent { get; init; }

    /// <summary>
    /// Carries <c>IFunction::kFunctionFlag_NoWait</c>, emitted as a separate
    /// <c>SetFunctionFlags</c> statement. Also invisible on the Papyrus side.
    /// </summary>
    public bool NoWait { get; init; }

    /// <summary>The C++ function the registration points at.</summary>
    public string CppFunctionName { get; init; } = "";

    /// <summary>Where this was recovered from, when it was recovered rather than authored.</summary>
    public string? SourceFile { get; init; }

    public int SourceLine { get; init; }

    /// <summary>The arity as the registration spells it, which excludes the receiver.</summary>
    public int Arity => Parameters.Count;

    /// <summary>The key two bindings are the same binding under.</summary>
    public (string Class, string Function) Key => (PapyrusClass, FunctionName);

    public override string ToString() => $"{ReturnType} {PapyrusClass}.{FunctionName}/{Arity}";
}

/// <summary>One member of a struct crossing the boundary.</summary>
public sealed record StructMemberBinding(string Name, PapyrusTypeText Type, bool Unsigned = false);

/// <summary>
/// A struct declared with <c>DECLARE_STRUCT</c> on the C++ side and <c>struct</c> on the Papyrus side.
/// </summary>
/// <remarks>
/// The macro bakes the owning script name into the VM type name as <c>Owner#Struct</c>, so a struct
/// cannot be emitted without knowing which script owns it. Members are addressed by name at run
/// time, so declaration order does not change behaviour, but it is preserved so the two emitted
/// files read the same way and so the round trip compares equal.
/// </remarks>
public sealed record StructBinding
{
    public required string Name { get; init; }

    /// <summary>The Papyrus script that owns the struct.</summary>
    public required string OwnerScript { get; init; }

    public IReadOnlyList<StructMemberBinding> Members { get; init; } =
        Array.Empty<StructMemberBinding>();

    /// <summary>The VM-side type name the macro produces.</summary>
    public string VmTypeName => OwnerScript + "#" + Name;
}

/// <summary>One emitted <c>Papyrus&lt;Plugin&gt;&lt;Module&gt;</c> translation unit and its <c>.psc</c>.</summary>
public sealed record ModuleBinding
{
    public required string Name { get; init; }

    /// <summary>The Papyrus script the module's declarations are written into.</summary>
    public required string ScriptName { get; init; }

    /// <summary>What the emitted script extends, or null for a <c>Native Hidden</c> globals module.</summary>
    public string? Extends { get; init; }

    public IReadOnlyList<StructBinding> Structs { get; init; } = Array.Empty<StructBinding>();

    public IReadOnlyList<NativeBinding> Natives { get; init; } = Array.Empty<NativeBinding>();

    /// <summary>The C++ namespace the module's functions live in.</summary>
    public string CppNamespace { get; init; } = "";
}

/// <summary>An F4SE message the emitted plugin subscribes to.</summary>
public sealed record MessageSubscription(string MessageName, string HandlerName);

/// <summary>A whole emitted plugin: modules, identity, and the shell that registers them.</summary>
public sealed record PluginBinding
{
    public required string Name { get; init; }

    public string Author { get; init; } = "";

    /// <summary>Plugin version, packed the way F4SE's version fields expect.</summary>
    public int VersionMajor { get; init; } = 1;
    public int VersionMinor { get; init; }
    public int VersionPatch { get; init; }

    public F4SETarget Target { get; init; } = F4SETarget.Og_1_10_163;

    public IReadOnlyList<ModuleBinding> Modules { get; init; } = Array.Empty<ModuleBinding>();

    public IReadOnlyList<MessageSubscription> Messages { get; init; } =
        Array.Empty<MessageSubscription>();

    public IEnumerable<NativeBinding> AllNatives => Modules.SelectMany(m => m.Natives);

    public IEnumerable<StructBinding> AllStructs => Modules.SelectMany(m => m.Structs);
}

/// <summary>One generated file, as text. The emitter never writes to disk.</summary>
/// <remarks>
/// Returning text rather than writing keeps the emitter free of I/O, which is what makes golden
/// file comparison and the emit-then-extract round trip trivial to test.
/// </remarks>
public sealed record EmittedFile(string RelativePath, string Text);
