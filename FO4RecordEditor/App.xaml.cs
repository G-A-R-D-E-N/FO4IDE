using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace FO4RecordEditor;

public partial class App : Application
{


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


        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain", e.ExceptionObject as Exception);
        Services.DebugLog.Init();
        Services.DebugLog.Info("App", "=== FO4IDE launching ===",
            $"base={AppContext.BaseDirectory} cwd={Environment.CurrentDirectory}");
        Trace("=== FO4IDE launching ===");
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


        ApplicationThemeManager.Apply(
            ApplicationTheme.Dark,
            Wpf.Ui.Controls.WindowBackdropType.Mica,
            updateAccent: false);
        ApplicationAccentColorManager.Apply(
            Color.FromRgb(0x00, 0xA8, 0xA8),
            ApplicationTheme.Dark);
        Trace("theme applied -- startup OK");

        Services.Rendering.ElementRenderer.Init(
            Services.MutagenLoader.FormatFormLink,
            Services.MutagenLoader.FormatCondition);
    }



















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



        var executor = new Services.PluginToolExecutor(envProvider, () => mo2Instance, () => ckWikiPath ?? Services.ToolPaths.CkWiki());
        try { Services.StdioMcpServer.Run(executor); }
        catch (Exception ex) { Trace($"--mcp loop ended with error: {ex.Message}"); }
        Environment.Exit(0);
    }


    private static string? ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }


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
            catch {  }
        }
    }

    private static void LogCrash(string source, Exception? ex)
    {
        if (ex != null) Services.DebugLog.Exception($"Unhandled {source}", ex);
        Trace($"!!! Unhandled {source} exception:{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 80)}");
        try
        {
            MessageBox.Show(
                $"FO4IDE hit an unhandled error.\n\n{ex?.Message}\n\n" +
                $"Details written to:\n{_logPath ?? "(could not write log)"}",
                "FO4IDE crash", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch {  }
    }
}
