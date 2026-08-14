using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FO4RecordEditor.Models;
using FO4RecordEditor.Services;
using FO4RecordEditor.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace FO4RecordEditor;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly ShellViewModel _shell = new();
    private readonly AppInterop _appInterop;


    private readonly object _logLock = new();
    private readonly object _errorsLock = new();

    public MainWindow()
    {
        Services.WpfHostServices.Install();
        InitializeComponent();
        DataContext = _shell;

        System.Windows.Data.BindingOperations.EnableCollectionSynchronization(_shell.Log.Entries, _logLock);
        System.Windows.Data.BindingOperations.EnableCollectionSynchronization(_shell.Errors, _errorsLock);


        _appInterop = new AppInterop(_shell, (msg, pct) =>
        {
            if (!string.IsNullOrEmpty(msg)) SetStatus(msg);
            SetProgress(pct ?? -1);
        });

        RegisterCommands();
        InitializeAsync();
        Services.PluginToolExecutor.ToolCompleted += OnMcpToolCompleted;
    }

    private void OnMcpToolCompleted(Services.PluginToolExecutor.McpToolEvent ev)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var msg = new
            {
                Type = "McpLive",
                Tool = ev.Tool,
                Plugin = ev.Plugin,
                Record = ev.Record,
                Field = ev.Field,
                Summary = ev.Summary,
                IsWrite = ev.IsWrite,
            };
            AppWebView.CoreWebView2?.PostWebMessageAsJson(
                Newtonsoft.Json.JsonConvert.SerializeObject(msg));
        });
    }

    private async void InitializeAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync(null, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FO4RecordEditor_Main_WebView2"));
        await AppWebView.EnsureCoreWebView2Async(env);

        AppWebView.CoreWebView2.AddHostObjectToScript("appInterop", _appInterop);
        AppWebView.CoreWebView2.AddHostObjectToScript("backend", new BackendInterop(_shell));


        AppWebView.CoreWebView2.AddHostObjectToScript("chat", new Services.ChatInterop(_shell,
            payload => Dispatcher.BeginInvoke(() =>
                AppWebView.CoreWebView2?.PostWebMessageAsJson(Newtonsoft.Json.JsonConvert.SerializeObject(payload)))));

        AppWebView.CoreWebView2.AddHostObjectToScript("settings", new Services.SettingsInterop(_shell));

        AppWebView.CoreWebView2.AddHostObjectToScript("papyrus", new Services.PapyrusInterop(_shell));

        AppWebView.CoreWebView2.AddHostObjectToScript("nif", new Services.NifInterop());

        AppWebView.CoreWebView2.AddHostObjectToScript("material", new Services.MaterialInterop());

        AppWebView.CoreWebView2.AddHostObjectToScript("masters", new Services.MastersInterop(_shell));

        AppWebView.CoreWebView2.AddHostObjectToScript("archive", new Services.ArchiveInterop());

        AppWebView.CoreWebView2.AddHostObjectToScript("audio", new Services.AudioInterop());

        AppWebView.CoreWebView2.AddHostObjectToScript("cell", new Services.CellInterop(_shell));

        AppWebView.CoreWebView2.AddHostObjectToScript("graph", new Services.GraphInterop(_shell));

#if DEBUG
        AppWebView.Source = new Uri("http://localhost:5173/#/main");
#else
        var path = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "web", "dist");
        AppWebView.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", path, CoreWebView2HostResourceAccessKind.Allow);
        AppWebView.Source = new Uri("https://app.local/index.html#/main");
#endif
    }

    private void RegisterCommands()
    {
        var c = _shell.Commands;
        c.Register("scan.errors", "Run Error Scan", "Errors", async () => await _shell.RunErrorScanAsync());
        c.Register("settings", "Open Settings", "App", () => new Views.SettingsDialog(_shell) { Owner = this }.ShowDialog());
        c.Register("save", "Save Plugin", "File", () => SetStatus(_shell.SaveSelectedPlugin()));
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            { TogglePalette(); e.Handled = true; }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            { SetStatus(_shell.SaveSelectedPlugin()); e.Handled = true; }
        else if (e.Key == Key.Escape)
            PaletteOverlay.Visibility = Visibility.Collapsed;
    }

    private void TogglePalette()
    {
        if (PaletteOverlay.Visibility == Visibility.Visible)
            { PaletteOverlay.Visibility = Visibility.Collapsed; return; }
        var palette = new Views.CommandPalette(_shell.Commands);
        palette.Dismiss += () => PaletteOverlay.Visibility = Visibility.Collapsed;
        PaletteHost.Content = palette;
        PaletteOverlay.Visibility = Visibility.Visible;
    }

    public void SetStatus(string text)
    {

        var msg = new { Type = "SetStatus", Text = text };
        AppWebView.CoreWebView2?.PostWebMessageAsJson(Newtonsoft.Json.JsonConvert.SerializeObject(msg));
    }

    public void SetProgress(double value)
    {

        var msg = new { Type = "SetProgress", Value = value };
        AppWebView.CoreWebView2?.PostWebMessageAsJson(Newtonsoft.Json.JsonConvert.SerializeObject(msg));
    }
}
