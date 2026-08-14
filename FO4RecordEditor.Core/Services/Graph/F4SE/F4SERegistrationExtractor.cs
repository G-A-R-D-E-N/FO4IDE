using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FO4RecordEditor.Services.Graph.F4SE;

/// <summary>What one translation unit registers.</summary>
public sealed record F4SEModuleSchema
{
    public required string SourcePath { get; init; }

    public IReadOnlyList<NativeBinding> Natives { get; init; } = Array.Empty<NativeBinding>();

    /// <summary>
    /// Structs the file declares.
    /// </summary>
    /// <remarks>
    /// Name and owning script only. <c>DECLARE_STRUCT</c> carries nothing else: a struct's members
    /// are read and written by name inside function bodies, so recovering them would mean
    /// understanding the bodies. <see cref="StructBinding.Members"/> is therefore empty on anything
    /// this class produces, and the <c>.psc</c> is the place to learn a struct's shape.
    /// </remarks>
    public IReadOnlyList<StructBinding> Structs { get; init; } = Array.Empty<StructBinding>();

    /// <summary>Problems found while reading, each naming the file and line.</summary>
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Recovers Papyrus native registrations out of F4SE C++ source.
/// </summary>
/// <remarks>
/// This is the reverse of <c>F4SECppEmitter</c> and is built first on purpose. Running it over a
/// real F4SE tree says whether the type vocabulary in <see cref="F4SETypeMap"/> is complete before
/// a single line of C++ is emitted, and afterwards it is what proves the emitter and the reader
/// agree: emit a plugin, read it back, compare.
/// <para>
/// It is a scanner, not a parser. The three things that make that safe are comment blanking,
/// depth-counted template argument lists, and whole-file matching. The last matters most: the
/// majority form in the shipped source puts <c>vm-&gt;RegisterFunction(</c> and the
/// <c>new NativeFunctionN</c> on separate lines, so anything working line by line would recover
/// almost nothing.
/// </para>
/// </remarks>
public sealed class F4SERegistrationExtractor
{
    // The opening of a registration. Everything after the '<' is taken by depth counting, because a
    // template argument can itself be a template.
    private static readonly Regex Opening = new(
        @"new\s+(?<latent>Latent)?NativeFunction(?<arity>\d+)\s*(?=<)",
        RegexOptions.Compiled);

    // The constructor arguments that follow the template list: "name", "class", function pointer.
    private static readonly Regex Constructor = new(
        @"\G\s*\(\s*""(?<fn>[^""]*)""\s*,\s*""(?<cls>[^""]*)""\s*,\s*(?<ptr>[A-Za-z_][\w:]*)",
        RegexOptions.Compiled);

    private static readonly Regex FunctionFlags = new(
        @"SetFunctionFlags(?:Ex)?\s*\(\s*""(?<cls>[^""]*)""\s*,\s*(?:[^,]*,\s*)?""(?<fn>[^""]*)""\s*,\s*"
        + @"(?:IFunction\s*::\s*)?(?<flag>\w+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex StructDeclaration = new(
        @"\bDECLARE_STRUCT\s*\(\s*(?<name>\w+)\s*,\s*""(?<owner>[^""]*)""\s*\)",
        RegexOptions.Compiled);

    // A struct declared in another translation unit. It carries no owner, so it contributes a name
    // the type map needs but no StructBinding.
    private static readonly Regex ExternStructDeclaration = new(
        @"\bDECLARE_EXTERN_STRUCT\s*\(\s*(?<name>\w+)\s*\)",
        RegexOptions.Compiled);

    /// <summary>Reads one file.</summary>
    public F4SEModuleSchema ExtractFile(string path, IEnumerable<string>? knownStructs = null) =>
        Extract(File.ReadAllText(path), path, knownStructs);

    /// <summary>
    /// Reads every <c>Papyrus*.cpp</c> under a directory, in two passes.
    /// </summary>
    /// <remarks>
    /// The first pass collects struct names across the whole directory, the second reads
    /// registrations knowing them. One pass is not enough: a struct declared in one translation
    /// unit is used from others through <c>DECLARE_EXTERN_STRUCT</c>, and a struct type the map has
    /// not been told about is indistinguishable from an unmappable one.
    /// </remarks>
    public IReadOnlyList<F4SEModuleSchema> ExtractDirectory(string directory, string pattern = "Papyrus*.cpp")
    {
        if (!Directory.Exists(directory)) return Array.Empty<F4SEModuleSchema>();

        var files = Directory.GetFiles(directory, pattern).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var texts = files.ToDictionary(f => f, f => F4SECppScanner.BlankComments(File.ReadAllText(f)));

        var structNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts.Values) CollectStructNames(text, structNames);

        return files.Select(f => ExtractBlanked(texts[f], f, structNames)).ToList();
    }

    /// <summary>Reads source text that has already been loaded.</summary>
    /// <param name="knownStructs">
    /// Struct names declared elsewhere. Names this text declares itself are always included.
    /// </param>
    public F4SEModuleSchema Extract(
        string source, string sourcePath = "", IEnumerable<string>? knownStructs = null)
    {
        var text = F4SECppScanner.BlankComments(source ?? "");
        var names = new HashSet<string>(knownStructs ?? Array.Empty<string>(), StringComparer.Ordinal);
        CollectStructNames(text, names);
        return ExtractBlanked(text, sourcePath, names);
    }

    private static void CollectStructNames(string blankedText, HashSet<string> into)
    {
        foreach (Match m in StructDeclaration.Matches(blankedText)) into.Add(m.Groups["name"].Value);
        foreach (Match m in ExternStructDeclaration.Matches(blankedText)) into.Add(m.Groups["name"].Value);
    }

    private static F4SEModuleSchema ExtractBlanked(
        string text, string sourcePath, IReadOnlyCollection<string> structNames)
    {
        var problems = new List<string>();

        var natives = ReadRegistrations(text, sourcePath, structNames, problems);
        ApplyFunctionFlags(text, natives, sourcePath, problems);

        var structs = StructDeclaration.Matches(text)
            .Select(m => new StructBinding
            {
                Name = m.Groups["name"].Value,
                OwnerScript = m.Groups["owner"].Value,
            })
            .ToList();

        return new F4SEModuleSchema
        {
            SourcePath = sourcePath,
            Natives = natives,
            Structs = structs,
            Problems = problems,
        };
    }

    private static List<NativeBinding> ReadRegistrations(
        string text, string sourcePath, IReadOnlyCollection<string> structNames, List<string> problems)
    {
        var natives = new List<NativeBinding>();
        var map = new F4SETypeMap(structNames);

        foreach (Match opening in Opening.Matches(text))
        {
            int angle = opening.Index + opening.Length;
            int close = F4SECppScanner.FindMatchingAngle(text, angle);
            int line = F4SECppScanner.LineAt(text, opening.Index);

            if (close < 0)
            {
                problems.Add($"{sourcePath}({line}): template argument list is never closed");
                continue;
            }

            var constructor = Constructor.Match(text, close + 1);
            if (!constructor.Success || constructor.Index != close + 1)
            {
                problems.Add($"{sourcePath}({line}): registration is not followed by (name, class, function)");
                continue;
            }

            var arguments = F4SECppScanner.SplitTemplateArguments(text[(angle + 1)..close]);
            if (arguments.Count < 2)
            {
                problems.Add($"{sourcePath}({line}): fewer than the two required template arguments");
                continue;
            }

            int arity = int.Parse(opening.Groups["arity"].Value);
            var declared = arguments.Count - 2;
            if (declared != arity)
            {
                // The arity is in the type name, so a mismatch means the scan drifted. Report it
                // rather than record a binding whose parameter list disagrees with its own name.
                problems.Add(
                    $"{sourcePath}({line}): NativeFunction{arity} carries {declared} parameter types");
                continue;
            }

            if (!CppTypeRef.TryParse(arguments[1], out var returnType))
            {
                problems.Add($"{sourcePath}({line}): return type '{arguments[1]}' is not a signature type");
                continue;
            }

            var parameters = new List<NativeParameter>();
            bool ok = true;
            for (int i = 0; i < arity; i++)
            {
                var text_ = arguments[i + 2];
                if (!CppTypeRef.TryParse(text_, out var cpp))
                {
                    problems.Add($"{sourcePath}({line}): parameter {i} type '{text_}' is not a signature type");
                    ok = false;
                    break;
                }

                // An unmapped type is kept rather than dropped: a silently missing native is worse
                // than one the caller can see and count.
                var unsigned = cpp!.Name == "UInt32";
                var type = map.TryToPapyrus(cpp, out var papyrus, out _)
                    ? papyrus
                    : new PapyrusTypeText(UnknownTypeName, false);

                parameters.Add(new NativeParameter($"a{i}", type, Unsigned: unsigned));
            }
            if (!ok) continue;

            var returnUnsigned = returnType!.Name == "UInt32";
            var returnPapyrus = map.TryToPapyrus(returnType, out var mapped, out _)
                ? mapped
                : new PapyrusTypeText(UnknownTypeName, false);

            natives.Add(new NativeBinding
            {
                FunctionName = constructor.Groups["fn"].Value,
                PapyrusClass = constructor.Groups["cls"].Value,
                CppBaseType = arguments[0],
                ReturnType = returnPapyrus,
                ReturnUnsigned = returnUnsigned,
                Parameters = parameters,
                IsLatent = opening.Groups["latent"].Success,
                CppFunctionName = constructor.Groups["ptr"].Value,
                SourceFile = sourcePath,
                SourceLine = line,
            });
        }

        return natives;
    }

    private static void ApplyFunctionFlags(
        string text, List<NativeBinding> natives, string sourcePath, List<string> problems)
    {
        foreach (Match match in FunctionFlags.Matches(text))
        {
            var flag = match.Groups["flag"].Value;
            if (!flag.Equals("kFunctionFlag_NoWait", StringComparison.Ordinal)) continue;

            var cls = match.Groups["cls"].Value;
            var fn = match.Groups["fn"].Value;
            int index = natives.FindIndex(n =>
                n.PapyrusClass.Equals(cls, StringComparison.OrdinalIgnoreCase)
                && n.FunctionName.Equals(fn, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                problems.Add(
                    $"{sourcePath}({F4SECppScanner.LineAt(text, match.Index)}): "
                    + $"flags set on {cls}.{fn}, which this file does not register");
                continue;
            }

            natives[index] = natives[index] with { NoWait = true };
        }
    }

    /// <summary>The type name recorded when a C++ type has no Papyrus mapping.</summary>
    /// <remarks>
    /// Kept rather than dropped so an unmapped type is a number the corpus sweep can report, not a
    /// native that quietly vanishes from the count.
    /// </remarks>
    public const string UnknownTypeName = "<unmapped>";
}
