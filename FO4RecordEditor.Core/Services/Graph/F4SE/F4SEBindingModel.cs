using System;
using System.Collections.Generic;
using System.Linq;

namespace FO4RecordEditor.Services.Graph.F4SE;

public enum F4SETarget
{

    Og_1_10_163 = 0,

    Ng_0_7_8 = 1,
}

public sealed record NativeParameter(
    string Name,
    PapyrusTypeText Type,
    string? DefaultLiteral = null,
    bool Unsigned = false)
{
    public bool IsOptional => DefaultLiteral != null;
}

public sealed record NativeBinding
{
    public required string FunctionName { get; init; }

    public required string PapyrusClass { get; init; }

    public required string CppBaseType { get; init; }

    public PapyrusTypeText ReturnType { get; init; } = new("None");

    public IReadOnlyList<NativeParameter> Parameters { get; init; } = Array.Empty<NativeParameter>();

    public bool ReturnUnsigned { get; init; }

    public bool IsGlobal => CppBaseType == F4SETypeMap.StaticFunctionTag;

    public bool IsLatent { get; init; }

    public bool NoWait { get; init; }

    public string CppFunctionName { get; init; } = "";

    public string? SourceFile { get; init; }

    public int SourceLine { get; init; }

    public int Arity => Parameters.Count;

    public (string Class, string Function) Key => (PapyrusClass, FunctionName);

    public override string ToString() => $"{ReturnType} {PapyrusClass}.{FunctionName}/{Arity}";
}

public sealed record StructMemberBinding(string Name, PapyrusTypeText Type, bool Unsigned = false);

public sealed record StructBinding
{
    public required string Name { get; init; }

    public required string OwnerScript { get; init; }

    public IReadOnlyList<StructMemberBinding> Members { get; init; } =
        Array.Empty<StructMemberBinding>();

    public string VmTypeName => OwnerScript + "#" + Name;
}

public sealed record ModuleBinding
{
    public required string Name { get; init; }

    public required string ScriptName { get; init; }

    public string? Extends { get; init; }

    public IReadOnlyList<StructBinding> Structs { get; init; } = Array.Empty<StructBinding>();

    public IReadOnlyList<NativeBinding> Natives { get; init; } = Array.Empty<NativeBinding>();

    public string CppNamespace { get; init; } = "";
}

public sealed record MessageSubscription(string MessageName, string HandlerName);

public sealed record PluginBinding
{
    public required string Name { get; init; }

    public string Author { get; init; } = "";

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

public sealed record EmittedFile(string RelativePath, string Text);
