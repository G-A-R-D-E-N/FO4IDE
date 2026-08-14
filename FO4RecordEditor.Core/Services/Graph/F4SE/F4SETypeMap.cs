using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FO4RecordEditor.Services.Graph.F4SE;

public sealed record CppTypeRef(string Name, bool IsPointer = false, CppTypeRef? ElementType = null)
{

    public const string ArrayTemplate = "VMArray";

    public bool IsArray => ElementType != null;

    public static CppTypeRef ArrayOf(CppTypeRef element) => new(ArrayTemplate, false, element);

    public string Format() =>
        IsArray ? $"{ArrayTemplate}<{ElementType!.Format()}>" : Name + (IsPointer ? "*" : "");

    public override string ToString() => Format();

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

public readonly record struct PapyrusTypeText(string Name, bool IsArray = false)
{
    public override string ToString() => IsArray ? Name + "[]" : Name;
}

public sealed class F4SETypeMap
{

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

        ("ScriptObject", "VMObject", false),
    };

    private static readonly (string Cpp, string Papyrus)[] ReverseOnlyRows =
    {

        ("VMRefOrInventoryObj", "ObjectReference"),
        ("UInt32", "int"),
        ("int", "int"),
    };

    private static readonly Dictionary<string, (string Cpp, bool Pointer)> ByPapyrus =
        BuildByPapyrus();

    private static readonly Dictionary<string, string> ByCpp = BuildByCpp();

    private readonly HashSet<string> _structNames;

    public F4SETypeMap(IEnumerable<string>? structNames = null) =>
        _structNames = new HashSet<string>(structNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

    public const string StaticFunctionTag = "StaticFunctionTag";

    public static IReadOnlyDictionary<string, string> FormTypes =>
        FormTypeRows.ToDictionary(r => r.Papyrus, r => r.Cpp, StringComparer.OrdinalIgnoreCase);

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

            cpp = new CppTypeRef(papyrus.Name);
            return true;
        }

        refusal =
            $"'{papyrus.Name}' is not a marshallable type: it is neither a known form type nor a "
            + "declared struct, and F4SE has no specialisation to carry it";
        return false;
    }

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

    public static IReadOnlyList<string> CoreIncludes { get; } = new[]
    {
        "f4se/PapyrusVM.h",
        "f4se/PapyrusNativeFunctions.h",
        "f4se/PapyrusArgs.h",
    };

    public static IReadOnlyList<string> GameIncludes { get; } = new[]
    {
        "f4se/GameForms.h",
        "f4se/GameReferences.h",
        "f4se/GameObjects.h",
        "f4se/GameData.h",
        "f4se/GameRTTI.h",
    };

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
