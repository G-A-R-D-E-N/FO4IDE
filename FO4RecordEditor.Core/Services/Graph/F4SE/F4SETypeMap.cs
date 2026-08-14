using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FO4RecordEditor.Services.Graph.F4SE;

/// <summary>A C++ type as it appears in an F4SE native signature.</summary>
/// <remarks>
/// Only the three shapes F4SE signatures actually use are modelled: a plain name
/// (<c>BSFixedString</c>), a pointer to one (<c>TESForm*</c>), and <c>VMArray&lt;T&gt;</c>. Scoped
/// names such as <c>BGSMod::Attachment::Mod</c> are carried whole in <see cref="Name"/>.
/// </remarks>
public sealed record CppTypeRef(string Name, bool IsPointer = false, CppTypeRef? ElementType = null)
{
    /// <summary>The <c>VMArray</c> template name, the only generic the marshaller carries.</summary>
    public const string ArrayTemplate = "VMArray";

    public bool IsArray => ElementType != null;

    public static CppTypeRef ArrayOf(CppTypeRef element) => new(ArrayTemplate, false, element);

    public string Format() =>
        IsArray ? $"{ArrayTemplate}<{ElementType!.Format()}>" : Name + (IsPointer ? "*" : "");

    public override string ToString() => Format();

    /// <summary>
    /// Parses a signature type, tolerating the spacing real F4SE source contains.
    /// </summary>
    /// <remarks>
    /// <c>TESObjectMISC *</c> with a space before the star occurs in the shipped source, so the
    /// pointer marker is stripped after trimming rather than matched against a fixed spelling.
    /// </remarks>
    public static bool TryParse(string? text, out CppTypeRef? type)
    {
        type = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        if (trimmed.StartsWith(ArrayTemplate, StringComparison.Ordinal))
        {
            var open = trimmed.IndexOf('<');
            var close = trimmed.LastIndexOf('>');
            if (open > 0 && close > open && trimmed[..open].Trim() == ArrayTemplate)
            {
                if (!TryParse(trimmed[(open + 1)..close], out var element)) return false;
                type = ArrayOf(element!);
                return true;
            }
        }

        bool pointer = false;
        while (trimmed.EndsWith('*'))
        {
            // A second star would be a pointer to pointer, which the marshaller cannot carry.
            if (pointer) return false;
            pointer = true;
            trimmed = trimmed[..^1].TrimEnd();
        }

        if (trimmed.Length == 0 || trimmed.Contains('<') || trimmed.Contains('>')) return false;
        type = new CppTypeRef(trimmed, pointer);
        return true;
    }

    public static CppTypeRef Parse(string text) =>
        TryParse(text, out var type) ? type! : throw new FormatException($"Not a signature type: '{text}'.");
}

/// <summary>A Papyrus type as written, which is all an emitter or an extractor needs.</summary>
public readonly record struct PapyrusTypeText(string Name, bool IsArray = false)
{
    public override string ToString() => IsArray ? Name + "[]" : Name;
}

/// <summary>
/// Converts between Papyrus types and the C++ types F4SE marshals them as, in both directions.
/// </summary>
/// <remarks>
/// The tables here are not recalled: they were derived by aligning all 225 registrations in the
/// F4SE 0.6.23 source against the <c>native</c> declarations in its shipped <c>.psc</c>, which
/// matched 223 of them position for position and produced exactly one Papyrus type per C++ type.
/// <see cref="F4SETypeMapTests"/> pins every row, and the corpus sweep re-derives the alignment
/// against a real F4SE tree when one is available.
/// <para>
/// Two rows are deliberately many to one. <c>UInt32</c>, <c>SInt32</c> and bare <c>int</c> all carry
/// Papyrus <c>int</c>, because the marshaller has a specialisation for each; the forward direction
/// therefore needs to be told which is wanted, and the reverse direction collapses them.
/// <c>VMRefOrInventoryObj</c> carries <c>ObjectReference</c> like <c>TESObjectREFR*</c> does, but is
/// a receiver-only form and so is never chosen going forward.
/// </para>
/// </remarks>
public sealed class F4SETypeMap
{
    /// <summary>Papyrus class name to the C++ type F4SE uses for it, pointer-ness included.</summary>
    private static readonly (string Papyrus, string Cpp, bool Pointer)[] FormTypeRows =
    {
        ("Form", "TESForm", true),
        ("ObjectReference", "TESObjectREFR", true),
        ("Actor", "Actor", true),
        ("ActorBase", "TESNPC", true),
        ("ActorValue", "ActorValueInfo", true),
        ("Ammo", "TESAmmo", true),
        ("Armor", "TESObjectARMO", true),
        ("ArmorAddon", "TESObjectARMA", true),
        ("Cell", "TESObjectCELL", true),
        ("Component", "BGSComponent", true),
        ("ConstructibleObject", "BGSConstructibleObject", true),
        ("DefaultObject", "BGSDefaultObject", true),
        ("Enchantment", "EnchantmentItem", true),
        ("EncounterZone", "BGSEncounterZone", true),
        ("EquipSlot", "BGSEquipSlot", true),
        ("FormList", "BGSListForm", true),
        ("GlobalVariable", "TESGlobal", true),
        ("HeadPart", "BGSHeadPart", true),
        ("Keyword", "BGSKeyword", true),
        ("LeveledItem", "TESLevItem", true),
        ("Location", "BGSLocation", true),
        ("MatSwap", "BGSMaterialSwap", true),
        ("MiscObject", "TESObjectMISC", true),
        ("ObjectMod", "BGSMod::Attachment::Mod", true),
        ("Outfit", "BGSOutfit", true),
        ("Perk", "BGSPerk", true),
        ("Projectile", "BGSProjectile", true),
        ("Race", "TESRace", true),
        ("Spell", "SpellItem", true),
        ("WaterType", "TESWaterForm", true),
        ("Weapon", "TESObjectWEAP", true),

        // Not a form pointer: the marshaller carries a script handle by value.
        ("ScriptObject", "VMObject", false),
    };

    /// <summary>C++ spellings that carry a Papyrus type but are never emitted for it.</summary>
    private static readonly (string Cpp, string Papyrus)[] ReverseOnlyRows =
    {
        // A receiver that accepts either a placed reference or an inventory item.
        ("VMRefOrInventoryObj", "ObjectReference"),
        ("UInt32", "int"),
        ("int", "int"),
    };

    private static readonly Dictionary<string, (string Cpp, bool Pointer)> ByPapyrus =
        BuildByPapyrus();

    private static readonly Dictionary<string, string> ByCpp = BuildByCpp();

    private readonly HashSet<string> _structNames;

    /// <param name="structNames">
    /// Papyrus struct names in play. A struct marshals as a value of the same name, so the forward
    /// direction has to be told which unknown names are structs rather than script types.
    /// </param>
    public F4SETypeMap(IEnumerable<string>? structNames = null) =>
        _structNames = new HashSet<string>(structNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The tag type a global function takes in place of a receiver.</summary>
    public const string StaticFunctionTag = "StaticFunctionTag";

    /// <summary>Papyrus class name to C++ base type, for every form type the table knows.</summary>
    public static IReadOnlyDictionary<string, string> FormTypes =>
        FormTypeRows.ToDictionary(r => r.Papyrus, r => r.Cpp, StringComparer.OrdinalIgnoreCase);

    /// <summary>The struct names this map was told about.</summary>
    public IReadOnlyCollection<string> StructNames => _structNames;

    private static Dictionary<string, (string, bool)> BuildByPapyrus()
    {
        var map = new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase)
        {
            ["bool"] = ("bool", false),
            ["float"] = ("float", false),
            ["string"] = ("BSFixedString", false),
            ["var"] = ("VMVariable", false),
            ["none"] = ("void", false),
        };
        foreach (var (papyrus, cpp, pointer) in FormTypeRows) map[papyrus] = (cpp, pointer);
        return map;
    }

    private static Dictionary<string, string> BuildByCpp()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bool"] = "bool",
            ["float"] = "float",
            ["BSFixedString"] = "string",
            ["VMVariable"] = "Var",
            ["void"] = "None",
            ["SInt32"] = "int",
        };
        foreach (var (papyrus, cpp, _) in FormTypeRows) map[cpp] = papyrus;
        foreach (var (cpp, papyrus) in ReverseOnlyRows) map[cpp] = papyrus;
        return map;
    }

    /// <summary>
    /// The C++ type an F4SE native uses to carry <paramref name="papyrus"/>.
    /// </summary>
    /// <param name="unsigned">
    /// Emit <c>UInt32</c> rather than <c>SInt32</c> for <c>int</c>. F4SE uses the unsigned form for
    /// indices and bit fields and the signed one where negatives are meaningful, and nothing in the
    /// Papyrus type says which, so the caller decides.
    /// </param>
    /// <param name="refusal">Why the conversion was refused, when it was.</param>
    public bool TryToCpp(PapyrusTypeText papyrus, bool unsigned, out CppTypeRef? cpp, out string? refusal)
    {
        cpp = null;
        refusal = null;

        if (string.IsNullOrWhiteSpace(papyrus.Name))
        {
            refusal = "the Papyrus type has no name";
            return false;
        }

        if (papyrus.IsArray)
        {
            if (papyrus.Name.EndsWith("[]", StringComparison.Ordinal))
            {
                refusal = "Papyrus has no array of arrays, so there is no VMArray<VMArray<>> to emit";
                return false;
            }
            if (!TryToCpp(new PapyrusTypeText(papyrus.Name), unsigned, out var element, out refusal))
                return false;
            if (element!.Name == "void")
            {
                refusal = "None has no array form";
                return false;
            }
            cpp = CppTypeRef.ArrayOf(element);
            return true;
        }

        if (papyrus.Name.Equals("int", StringComparison.OrdinalIgnoreCase))
        {
            cpp = new CppTypeRef(unsigned ? "UInt32" : "SInt32");
            return true;
        }

        if (ByPapyrus.TryGetValue(papyrus.Name, out var row))
        {
            cpp = new CppTypeRef(row.Cpp, row.Pointer);
            return true;
        }

        if (_structNames.Contains(papyrus.Name))
        {
            // DECLARE_STRUCT typedefs a VMStruct to the struct's own name, carried by value.
            cpp = new CppTypeRef(papyrus.Name);
            return true;
        }

        refusal =
            $"'{papyrus.Name}' is not a marshallable type: it is neither a known form type nor a "
            + "declared struct, and F4SE has no specialisation to carry it";
        return false;
    }

    /// <summary>The Papyrus type an F4SE C++ type carries.</summary>
    public bool TryToPapyrus(CppTypeRef? cpp, out PapyrusTypeText papyrus, out string? refusal)
    {
        papyrus = default;
        refusal = null;

        if (cpp == null)
        {
            refusal = "no type given";
            return false;
        }

        if (cpp.IsArray)
        {
            if (!TryToPapyrus(cpp.ElementType, out var element, out refusal)) return false;
            if (element.IsArray)
            {
                refusal = "VMArray<VMArray<>> has no Papyrus type: the language has no jagged arrays";
                return false;
            }
            papyrus = new PapyrusTypeText(element.Name, IsArray: true);
            return true;
        }

        if (cpp.Name == StaticFunctionTag)
        {
            refusal = "StaticFunctionTag is a receiver tag, not a value type";
            return false;
        }

        if (ByCpp.TryGetValue(cpp.Name, out var name))
        {
            papyrus = new PapyrusTypeText(name);
            return true;
        }

        // An unmapped pointer is a form type nothing has named, and guessing its Papyrus class from
        // the C++ spelling is exactly the kind of invention that produces a wrong declaration.
        if (cpp.IsPointer)
        {
            refusal = $"'{cpp.Format()}' is a pointer to a type the form table does not name";
            return false;
        }

        if (_structNames.Contains(cpp.Name))
        {
            papyrus = new PapyrusTypeText(cpp.Name);
            return true;
        }

        refusal = $"'{cpp.Format()}' is not a known marshalled type or a declared struct";
        return false;
    }

    /// <summary>
    /// The include block an emitted module carries.
    /// </summary>
    /// <remarks>
    /// This reproduces the block real F4SE modules use rather than computing a minimal set. A
    /// minimal set would need each type's defining header, and the shipped headers forward declare
    /// most types in several places, so deriving that reliably is not possible from this repository
    /// and getting it wrong yields C++ that does not compile. Since nothing here can compile C++
    /// (see <c>docs/internal/GRAPH_F4SE.md</c>), matching observed practice is the honest option.
    /// </remarks>
    public static IReadOnlyList<string> CoreIncludes { get; } = new[]
    {
        "f4se/PapyrusVM.h",
        "f4se/PapyrusNativeFunctions.h",
        "f4se/PapyrusArgs.h",
    };

    /// <summary>Game headers added when a module names a form type or a struct.</summary>
    public static IReadOnlyList<string> GameIncludes { get; } = new[]
    {
        "f4se/GameForms.h",
        "f4se/GameReferences.h",
        "f4se/GameObjects.h",
        "f4se/GameData.h",
        "f4se/GameRTTI.h",
    };

    /// <summary>The include list for a module using <paramref name="types"/>.</summary>
    public IReadOnlyList<string> RequiredIncludesFor(IEnumerable<CppTypeRef> types)
    {
        var includes = new List<string>(CoreIncludes);
        var all = types.ToList();

        if (all.Any(NeedsStructHeader)) includes.Add("f4se/PapyrusStruct.h");
        if (all.Any(NeedsGameHeaders)) includes.AddRange(GameIncludes);
        return includes;

        bool NeedsStructHeader(CppTypeRef type) =>
            type.IsArray ? NeedsStructHeader(type.ElementType!) : _structNames.Contains(type.Name);

        bool NeedsGameHeaders(CppTypeRef type) =>
            type.IsArray ? NeedsGameHeaders(type.ElementType!) : type.IsPointer;
    }

    /// <summary>The <c>NativeFunctionN</c> template instantiation for one binding, as text.</summary>
    public static string TemplateInstantiation(
        bool latent, int arity, string cppBaseType, CppTypeRef returnType, IEnumerable<CppTypeRef> parameters)
    {
        var builder = new StringBuilder();
        builder.Append(latent ? "LatentNativeFunction" : "NativeFunction").Append(arity);
        builder.Append('<').Append(cppBaseType).Append(", ").Append(returnType.Format());
        foreach (var parameter in parameters) builder.Append(", ").Append(parameter.Format());
        builder.Append('>');
        return builder.ToString();
    }
}
