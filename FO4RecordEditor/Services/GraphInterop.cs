using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FO4RecordEditor.Services.Graph;
using FO4RecordEditor.Services.Papyrus;
using FO4RecordEditor.ViewModels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class GraphInterop
{
    private readonly ShellViewModel _shell;
    private GraphCompiler? _compiler;
    private string _rootsKey = "";

    public GraphInterop(ShellViewModel shell) => _shell = shell;

    private GraphCompiler Compiler()
    {
        var roots = Roots();
        var key = string.Join("|", roots);
        if (_compiler == null || key != _rootsKey)
        {
            _compiler = new GraphCompiler(GraphCompiler.IndexFor(roots));
            _rootsKey = key;
        }
        return _compiler;
    }

    private static IReadOnlyList<string> Roots() =>
        ToolPaths.PapyrusBaseImports().Where(Directory.Exists).ToList();

    public void Refresh()
    {
        _compiler = null;
        _rootsKey = "";
    }

    public Task<string> GetCorePalette() => Run(() =>
    {
        var compiler = Compiler();
        return JsonConvert.SerializeObject(new
        {
            version = GraphDocument.CurrentSchema,
            builtins = compiler.Palette.Builtins.Select(Describe),
            scripts = compiler.Palette.ScriptNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
            wiki = new
            {
                available = compiler.Palette.WikiStats.Available,
                pages = compiler.Palette.WikiStats.PagesIndexed,
            },
        }, GraphDocumentJson.Settings);
    });

    public Task<string> SearchPalette(string kind, string query, string scriptFilter, int limit) => Run(() =>
    {
        var result = Compiler().Palette.Search(query, limit <= 0 ? 50 : limit,
            string.IsNullOrWhiteSpace(scriptFilter) ? null : scriptFilter);

        var entries = result.Entries.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(kind) && kind != "any")
        {
            entries = entries.Where(e =>
                string.Equals(e.Kind.ToString(), kind, StringComparison.OrdinalIgnoreCase));
        }

        return JsonConvert.SerializeObject(new
        {
            entries = entries.Select(e => new
            {
                id = e.Id, title = e.Title, category = e.Category,
                kind = e.Kind.ToString(), signature = e.Signature, isPure = e.IsPure,
            }),
            total = result.Total,
        }, GraphDocumentJson.Settings);
    });

    public Task<string> GetNodeSignature(string nodeType) => Run(() =>
    {
        var definition = Compiler().Palette.Find(nodeType);
        return definition == null
            ? "Error: no node type '" + nodeType + "' on this palette"
            : JsonConvert.SerializeObject(Describe(definition), GraphDocumentJson.Settings);
    });

    public Task<string> ValidateGraph(string documentJson) => Run(() =>
    {
        if (!GraphDocumentJson.TryDeserialize(documentJson, out var document, out var error))
            return Diagnostics(new[] { error! });

        return Diagnostics(Compiler().Validate(document!).Diagnostics);
    });

    public Task<string> CompileToSource(string documentJson) => Run(() =>
    {
        if (!GraphDocumentJson.TryDeserialize(documentJson, out var document, out var error))
            return Diagnostics(new[] { error! });

        var result = Compiler().Compile(document!, new GraphCompileOptions { StopAfterSource = true });
        return JsonConvert.SerializeObject(new
        {
            source = result.Source,
            diagnostics = result.Diagnostics.Select(Describe),
            ok = !result.Errors.Any(),
        }, GraphDocumentJson.Settings);
    });

    public Task<string> CompileToPex(string documentJson, string outputDirectory) => Run(() =>
    {
        if (!GraphDocumentJson.TryDeserialize(documentJson, out var document, out var error))
            return Diagnostics(new[] { error! });

        var result = Compiler().Compile(document!);
        string? written = null;

        if (result.Success && !string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            written = Path.Combine(outputDirectory, document!.Header.ScriptName + ".pex");
            result.Pex!.WriteFile(written);
            File.WriteAllText(
                Path.Combine(outputDirectory, document.Header.ScriptName + ".psc"), result.Source!);
        }

        return JsonConvert.SerializeObject(new
        {
            source = result.Source,
            diagnostics = result.Diagnostics.Select(Describe),
            ok = result.Success,
            written,
        }, GraphDocumentJson.Settings);
    });

    public Task<string> LoadGraph(string path) => Run(() =>
    {
        if (!File.Exists(path)) return "Error: no file at " + path;
        return GraphDocumentJson.TryDeserialize(File.ReadAllText(path), out _, out var error)
            ? File.ReadAllText(path)
            : "Error: " + error!.Message;
    });

    public Task<string> LoadScript(string path) => Run(() =>
    {
        var result = GraphScriptLoader.Load(path, Roots());
        if (result.Failure != null) return "Error: " + result.Failure;

        return JsonConvert.SerializeObject(new
        {
            ok = result.Success,
            document = result.Document,
            diagnostics = result.Diagnostics.Select(Describe),
        }, GraphDocumentJson.Settings);
    });

    public string BrowseForScript() =>
        HostServices.PickFile("Open graph or script",
            "Graph or script (*.fograph;*.psc;*.pex)|*.fograph;*.psc;*.pex"
            + "|Node graph (*.fograph)|*.fograph"
            + "|Papyrus source (*.psc)|*.psc"
            + "|Compiled Papyrus (*.pex)|*.pex");

    public Task<string> SaveGraph(string path, string documentJson) => Run(() =>
    {
        if (!GraphDocumentJson.TryDeserialize(documentJson, out var document, out var error))
            return "Error: " + error!.Message;

        GraphDocumentJson.SaveFile(document!, path);
        return "";
    });

    public string BrowseForGraph(bool save) =>
        save
            ? HostServices.PickSavePath("Save graph", "Node graph (*.fograph)|*.fograph")
            : HostServices.PickFile("Open graph", "Node graph (*.fograph)|*.fograph");

    private static object Describe(NodeDefinition definition) => new
    {
        type = definition.Id,
        label = definition.Title,
        category = definition.Category,
        kind = definition.Kind.ToString(),
        summary = definition.Summary,
        isPure = definition.IsPure,
        isGlobal = definition.IsGlobal,
        script = definition.OwnerScript,
        pins = definition.Pins.Select(p => new
        {
            id = p.Id,
            name = p.Label.Length > 0 ? p.Label : p.Id,
            kind = p.Kind == PinKind.Exec ? "exec" : "data",
            dir = p.Direction == PinDirection.In ? "in" : "out",
            dataType = p.Type?.ToString() ?? "",
            optional = p.IsOptional,
            defaultLiteral = p.DeclaredDefault,
            description = p.Description,
        }),
    };

    private static object Describe(GraphDiagnostic diagnostic) => new
    {
        code = diagnostic.Code,
        severity = diagnostic.Severity == GraphSeverity.Error ? "error" : "warning",
        message = diagnostic.Message,
        nodeId = diagnostic.NodeId,
        pinId = diagnostic.PinId,
        wireId = diagnostic.WireId,
        relatedNodes = diagnostic.RelatedNodes,
        line = diagnostic.SourceLine,
    };

    private static string Diagnostics(IEnumerable<GraphDiagnostic> diagnostics) =>
        JsonConvert.SerializeObject(new
        {
            diagnostics = diagnostics.Select(Describe),
            ok = !diagnostics.Any(d => d.Severity == GraphSeverity.Error),
        }, GraphDocumentJson.Settings);

    private static Task<string> Run(Func<string> work) => Task.Run(() =>
    {
        try
        {
            return work();
        }
        catch (Exception ex)
        {
            DebugLog.Exception("Graph", ex);
            return "Error: " + ex.Message;
        }
    });
}
