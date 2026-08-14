using System.Collections.ObjectModel;
using FO4RecordEditor.Models;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.ViewModels;

public sealed class ShellViewModel
{
    public ObservableCollection<RecordNode> Plugins { get; } = [];

    public KnowledgeGraph Graph { get; } = new();
    public LogService Log { get; } = new();
    public SettingsService Settings { get; } = new();
    public CommandRegistry Commands { get; } = new();
    public ErrorScanner Scanner { get; } = new();
    public DiffEngine Diff { get; } = new();

    public ChatService Chat { get; private set; } = null!;
    public AIContextBuilder Context { get; private set; } = null!;

    public ObservableCollection<PluginError> Errors { get; } = [];
    public RecordNode? SelectedNode { get; set; }

    public object? GameEnvironment { get; set; }



    public PluginToolExecutor ToolExecutor { get; }
    public AnthropicAgent? Agent { get; private set; }



    public PluginMcpServer McpServer { get; }

    public ShellViewModel()
    {
        Settings.Load();
        ToolExecutor = new PluginToolExecutor(() => GameEnvironment, () => Settings.Current.Mo2InstancePath,
            () => Settings.Current.CkWikiPath);
        McpServer = new PluginMcpServer(ToolExecutor);
        McpServer.Start();
        Context = new AIContextBuilder(Graph);
        Chat = new ChatService(CreateProvider());
        BuildAgent();


        WriteService.PluginChanged += OnWritePluginChanged;
        WriteService.OutputFolderOverride = Settings.Current.OutputFolder;
        UpdateOverwriteFolder(Settings.Current.Mo2InstancePath);
        Log.Log(LogCategory.App, LogLevel.Info,
            $"FO4IDE started. Plugin MCP server: {(McpServer.IsRunning ? McpServer.Url : "unavailable")}");
    }

    public void RebuildProvider()
    {
        Chat.SetProvider(CreateProvider());
        BuildAgent();
        WriteService.OutputFolderOverride = Settings.Current.OutputFolder;
    }

    private IAIProvider CreateProvider()
    {
        var s = Settings.Current;
        if (s.AiProvider == "claudecode")
            return new ClaudeCodeProvider(s.ClaudeCodePath,
                McpServer.IsRunning ? McpServer.Url : null, McpServer.ServerName, s.Model);
        if (s.AiProvider == "gemini")
            return new GeminiProvider(s.GeminiApiKey, s.GeminiModel, ToolExecutor);
        return AiProviderFactory.Create(s);
    }




    private void OnWritePluginChanged(string name)
    {
        ConflictScanner.InvalidateCache();
        var env = GameEnvironment;

        _ = Task.Run(() =>
        {
            try
            {

                RecordNode node = null!;
                HostServices.InvokeOnUiThread(() =>
                {
                    node = Plugins.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase))!;
                    if (node == null) { node = new RecordNode { Key = name }; Plugins.Add(node); }
                });


                var groups = MutagenLoader.GetGroups(name, env, node, null);

                HostServices.InvokeOnUiThread(() =>
                {
                    node.Children.Clear();
                    foreach (var g in groups) { g.Parent = node; node.Children.Add(g); }
                    node.IsExpanded = true;
                });
            }
            catch (Exception ex)
            {
                Log.Log(LogCategory.App, LogLevel.Error, "Tree refresh failed", ex.Message);
            }
        });
    }

    private void BuildAgent()
    {
        var s = Settings.Current;
        Agent = (s.AiProvider == "anthropic" && !string.IsNullOrWhiteSpace(s.AnthropicApiKey))
            ? new AnthropicAgent(s.AnthropicApiKey, s.Model, ToolExecutor)
            : null;
    }

    private bool _isLoadingEsp;
    public async Task LoadEspAsync(string path, IProgress<(string message, double? percent)>? progress = null)
    {
        if (_isLoadingEsp) return;
        _isLoadingEsp = true;
        try
        {
            Log.Log(LogCategory.App, LogLevel.Info, $"Loading {System.IO.Path.GetFileName(path)}...");
            var node = await Task.Run(() => MutagenLoader.LoadEsp(path, progress));
            Plugins.Add(node);

            int recCount = 0;
            if (node.Values.TryGetValue("_RecordCount", out var rcStr) && int.TryParse(rcStr, out var parsed))
                recCount = parsed;

            Log.Log(LogCategory.App, LogLevel.Info,
                $"Loaded {node.Key}: ~{recCount} records (lazy loaded).");
        }
        finally
        {
            _isLoadingEsp = false;
        }
    }







    private static void UpdateOverwriteFolder(string? instancePath)
    {
        WriteService.Mo2OverwriteFolder =
            string.IsNullOrWhiteSpace(instancePath) ? null
            : Mo2ProfileLoader.ResolveOverwriteFolder(instancePath);
    }


    private IReadOnlyList<string> _lastPlugins = System.Array.Empty<string>();

    private void PopulatePluginTree(IReadOnlyList<string> plugins)
    {
        _lastPlugins = plugins;
        ConflictScanner.InvalidateCache();
        Plugins.Clear();
        foreach (var name in plugins)
            Plugins.Add(MutagenLoader.MakeLazyNode(name));
    }




    public void RefreshPluginTree()
    {
        var names = _lastPlugins.ToList();
        foreach (var k in MutagenLoader.EditableMods.Keys)
            if (!names.Any(n => string.Equals(n, k, System.StringComparison.OrdinalIgnoreCase)))
                names.Add(k);
        PopulatePluginTree(names);
    }

    public async Task<List<string>> LoadEnvironmentAsync(IProgress<(string message, double? percent)>? progress = null)
    {
        if (_isLoadingEsp) return new();
        _isLoadingEsp = true;
        try
        {
            var dataFolder = Settings.Current.DataFolder;
            Log.Log(LogCategory.App, LogLevel.Info,
                string.IsNullOrWhiteSpace(dataFolder)
                    ? "Building Load Order environment (auto-detect / MO2 VFS)..."
                    : $"Building Load Order environment from data folder: {dataFolder}");
            var (env, plugins) = await Task.Run(() => MutagenLoader.BuildEnvironment(progress, dataFolder));
            GameEnvironment = env;
            PopulatePluginTree(plugins);
            Log.Log(LogCategory.App, LogLevel.Info, $"Environment ready: {plugins.Count} plugins.");
            return plugins;
        }
        catch (Exception ex)
        {
            Log.Log(LogCategory.App, LogLevel.Error, $"Environment load failed: {ex.Message}");


            throw;
        }
        finally
        {
            _isLoadingEsp = false;
        }
    }



    public async Task<List<string>> LoadMo2ProfileAsync(
        string instancePath, IProgress<(string message, double? percent)>? progress = null)
    {
        if (_isLoadingEsp) return new();
        _isLoadingEsp = true;
        try
        {
            var info = Mo2ProfileLoader.ReadInstanceInfo(instancePath);
            Log.Log(LogCategory.App, LogLevel.Info,
                $"Loading MO2 profile '{info.Profile}' from {instancePath} (data: {info.GameDataFolder})...");
            var (env, plugins) = await Task.Run(() =>
                Mo2ProfileLoader.Load(instancePath, info.Profile, info.GameDataFolder, progress));
            GameEnvironment = env;
            PopulatePluginTree(plugins);
            Settings.Current.Mo2InstancePath = instancePath;
            Settings.Save();
            UpdateOverwriteFolder(instancePath);
            Log.Log(LogCategory.App, LogLevel.Info, $"MO2 environment ready: {plugins.Count} plugins.");
            return plugins;
        }
        catch (Exception ex)
        {
            Log.Log(LogCategory.App, LogLevel.Error, $"MO2 profile load failed: {ex.Message}");
            return new();
        }
        finally
        {
            _isLoadingEsp = false;
        }
    }

    private bool _isScanning;
    public async Task RunErrorScanAsync()
    {
        if (_isScanning) return;
        _isScanning = true;
        try
        {
            Log.Log(LogCategory.Error, LogLevel.Info, "Running error scan...");
            var found = await Scanner.ScanAsync(Graph);
            Errors.Clear();
            foreach (var e in found) Errors.Add(e);
            Log.Log(LogCategory.Error, LogLevel.Info, $"Scan complete: {found.Count} issue(s).");
        }
        finally
        {
            _isScanning = false;
        }
    }







    public string SaveSelectedPlugin()
    {
        if (SelectedNode == null)
        {
            const string msg = "Nothing selected to save. Use the editor's Save action, or the " +
                               "save_plugin tool.";
            Log.Log(LogCategory.App, LogLevel.Warning, msg);
            return msg;
        }

        var root = SelectedNode;
        while (root.Parent != null) root = root.Parent;

        if (root.FilePath == null)
        {
            const string msg = "That node has no backing file to save.";
            Log.Log(LogCategory.App, LogLevel.Warning, msg);
            return msg;
        }

        Log.Log(LogCategory.App, LogLevel.Info, $"Saving {root.Key}...");





        var refuse = $"Saving from the tree is not supported. Use the editor's Save action, " +
                     "or call the save_plugin tool.";
        Log.Log(LogCategory.App, LogLevel.Warning, refuse);
        return refuse;
    }
}
