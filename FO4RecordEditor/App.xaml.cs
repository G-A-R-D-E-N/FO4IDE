using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace FO4RecordEditor;

public partial class App : Application
{
    // Startup trace + crash log land next to the exe, with an AppData fallback if that folder
    // is read-only (e.g. under an MO2 mod folder). The first writable location wins.
    private static readonly string[] LogCandidates =
    {
        Path.Combine(AppContext.BaseDirectory, "FO4RecordEditor.startup.log"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FO4RecordEditor", "startup.log"),
    };

    private static string? _logPath;

    static App()
    {
        // Earliest possible hook: catches exceptions thrown while App.xaml resources load
        // (InitializeComponent), which happens before OnStartup runs.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain", e.ExceptionObject as Exception);
        Services.DebugLog.Init();
        Services.DebugLog.Info("App", "=== FO4RecordEditor launching ===",
            $"base={AppContext.BaseDirectory} cwd={Environment.CurrentDirectory}");
        Trace("=== FO4RecordEditor launching ===");
        Trace($"BaseDirectory: {AppContext.BaseDirectory}");
        Trace($"WorkingDirectory: {Environment.CurrentDirectory}");
        Trace($"Debug log: {Services.DebugLog.Path}");
    }

    public App()
    {
        Trace("App() constructed");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Headless MCP mode: `--mcp [--data <folder>]`. Run a stdio JSON-RPC server exposing the
        // plugin tools (for the Claude Code CLI) and never show the GUI. Must run before
        // base.OnStartup so the StartupUri window is never created.
        if (Array.Exists(e.Args, a => a == "--mcp"))
        {
            RunMcpHeadless(e.Args);
            return;
        }

        Trace("OnStartup begin");
        DispatcherUnhandledException += (_, ev) => { LogCrash("Dispatcher", ev.Exception); ev.Handled = true; };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException +=
            (_, ev) => { LogCrash("UnobservedTask", ev.Exception); ev.SetObserved(); };

        base.OnStartup(e);
        Trace("base.OnStartup done");

        // Fluent dark theme + keep the project's teal accent (instead of the system accent).
        ApplicationThemeManager.Apply(
            ApplicationTheme.Dark,
            Wpf.Ui.Controls.WindowBackdropType.Mica,
            updateAccent: false);
        ApplicationAccentColorManager.Apply(
            Color.FromRgb(0x00, 0xA8, 0xA8),   // teal accent
            ApplicationTheme.Dark);
        Trace("theme applied -- startup OK");

        Services.Rendering.ElementRenderer.Init(
            Services.MutagenLoader.FormatFormLink,
            Services.MutagenLoader.FormatCondition);
    }

    /// <summary>
    /// Build the env lazily (on first tool use) and serve plugin tools over stdio until stdin
    /// closes, then exit. Lazy so startup is instant and a missing game install doesn't stop the
    /// server -- the failing tool just reports it.
    /// <para>
    /// Source of the load order, in order of precedence:
    /// <list type="bullet">
    /// <item><c>--mo2 &lt;instancePath&gt;</c> -- reconstruct an MO2 modlist (profile plugins.txt +
    /// mods/overwrite) so modlist plugins like FallenWorldCrafting.esp are visible/editable.</item>
    /// <item><c>--data &lt;folder&gt;</c> -- a plain Data folder override for the vanilla builder.</item>
    /// <item>neither -- the default Fallout 4 install (vanilla + DLC + CC only).</item>
    /// </list>
    /// </para>
    /// <c>--ck-wiki &lt;folder&gt;</c> is independent of the above: points papyrus_function_lookup /
    /// papyrus_script_info at an offline Creation Kit Wiki HTML mirror. Optional -- a copy ships bundled
    /// with the app (see ToolPaths.CkWiki), so those two tools work out of the box; pass this only to
    /// point at a different or newer mirror. Nothing else depends on it.
    /// </summary>
    private static void RunMcpHeadless(string[] args)
    {
        string? dataOverride = ArgValue(args, "--data");
        string? mo2Instance = ArgValue(args, "--mo2");
        string? ckWikiPath = ArgValue(args, "--ck-wiki");

        object? env = null;
        bool tried = false;
        Func<object?> envProvider = () =>
        {
            if (!tried)
            {
                tried = true;
                try
                {
                    if (!string.IsNullOrWhiteSpace(mo2Instance))
                    {
                        var info = Services.Mo2ProfileLoader.ReadInstanceInfo(mo2Instance);
                        env = Services.Mo2ProfileLoader.Load(info.InstancePath, info.Profile, info.GameDataFolder).env;
                        Trace($"--mcp MO2 env loaded: instance={info.InstancePath} profile={info.Profile}");
                    }
                    else
                    {
                        env = Services.MutagenLoader.BuildEnvironment(null, dataOverride).env;
                    }
                }
                catch (Exception ex) { Trace($"--mcp env build failed: {ex.Message}"); }
            }
            return env;
        };

        // --ck-wiki wins if given explicitly; otherwise fall through to ToolPaths' own chain, which
        // resolves to the bundled mirror shipped under tools\ckwiki\fallout4\ with no flag needed.
        var executor = new Services.PluginToolExecutor(envProvider, () => mo2Instance, () => ckWikiPath ?? Services.ToolPaths.CkWiki());
        try { Services.StdioMcpServer.Run(executor); }
        catch (Exception ex) { Trace($"--mcp loop ended with error: {ex.Message}"); }
        Environment.Exit(0);
    }

    /// <summary>Returns the value following <paramref name="name"/> in the arg list, or null.</summary>
    private static string? ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    /// <summary>Appends a timestamped breadcrumb to the startup log (best-effort, never throws).</summary>
    internal static void Trace(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
        if (_logPath != null) { try { File.AppendAllText(_logPath, line); } catch { } return; }

        foreach (var path in LogCandidates)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line);
                _logPath = path;
                return;
            }
            catch { /* try next candidate */ }
        }
    }

    private static void LogCrash(string source, Exception? ex)
    {
        if (ex != null) Services.DebugLog.Exception($"Unhandled {source}", ex);
        Trace($"!!! Unhandled {source} exception:{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 80)}");
        try
        {
            MessageBox.Show(
                $"FO4RecordEditor hit an unhandled error.\n\n{ex?.Message}\n\n" +
                $"Details written to:\n{_logPath ?? "(could not write log)"}",
                "FO4RecordEditor -- Crash", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* UI may be gone; the log is the source of truth */ }
    }
}
