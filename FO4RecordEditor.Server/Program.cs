using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FO4RecordEditor.Server;
using FO4RecordEditor.Services;
using FO4RecordEditor.Services.Rendering;
using FO4RecordEditor.ViewModels;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Cross-platform host for FO4RecordEditor. Serves the same React bundle the WPF shell loads into
// WebView2, and exposes the same interop objects over HTTP instead of AddHostObjectToScript.

DebugLog.Init();
DebugLog.Info("App", "=== FO4RecordEditor (server) launching ===",
    $"base={AppContext.BaseDirectory} cwd={Environment.CurrentDirectory}");

string? ArgValue(string name)
{
    for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
    return null;
}
bool HasFlag(string name) => Array.Exists(args, a => a == name);

// Headless MCP mode, same contract as the Windows build's `--mcp`. This is what makes the Wine
// setup unnecessary on Linux: the MCP server now runs natively.
if (HasFlag("--mcp"))
{
    RunMcpHeadless();
    return;
}

LinuxHostServices.Install();
ElementRenderer.Init(MutagenLoader.FormatFormLink, MutagenLoader.FormatCondition);

var shell = new ShellViewModel();
var events = new EventStream();

// Same twelve names MainWindow registers with AddHostObjectToScript. The frontend addresses these
// by name, so they must not drift apart.
var rpc = new RpcDispatcher();
rpc.Register("appInterop", new AppInterop(shell, (msg, pct) =>
{
    if (!string.IsNullOrEmpty(msg)) events.Push(new { Type = "SetStatus", Text = msg });
    events.Push(new { Type = "SetProgress", Value = pct ?? -1 });
}));
rpc.Register("backend", new BackendInterop(shell));
rpc.Register("chat", new ChatInterop(shell, payload => events.Push(payload)));
rpc.Register("settings", new SettingsInterop(shell));
rpc.Register("papyrus", new PapyrusInterop(shell));
rpc.Register("nif", new NifInterop());
rpc.Register("material", new MaterialInterop());
rpc.Register("masters", new MastersInterop(shell));
rpc.Register("archive", new ArchiveInterop());
rpc.Register("audio", new AudioInterop());
rpc.Register("cell", new CellInterop(shell));
rpc.Register("graph", new GraphInterop(shell));

PluginToolExecutor.ToolCompleted += ev => events.Push(new
{
    Type = "McpLive",
    ev.Tool, ev.Plugin, ev.Record, ev.Field, ev.Summary, ev.IsWrite,
});

var host = ArgValue("--host") ?? "127.0.0.1";
var port = int.TryParse(ArgValue("--port"), out var p) && p > 0 ? p : FreePort(host);

var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://{host}:{port}");
var app = builder.Build();

var webRoot = Path.Combine(AppContext.BaseDirectory, "web", "dist");
if (!Directory.Exists(webRoot))
{
    Console.Error.WriteLine($"UI bundle missing: {webRoot}\nBuild it with: cd web && npm ci && npm run build");
    return;
}
var files = new PhysicalFileProvider(webRoot);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ServeUnknownFileTypes = true });

app.MapPost("/rpc", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    try
    {
        var j = JObject.Parse(body);
        var req = new RpcDispatcher.Request(
            j.Value<string>("target") ?? "",
            j.Value<string>("method") ?? "",
            j["args"] as JArray);
        var value = await rpc.InvokeAsync(req);
        return Results.Content(JsonConvert.SerializeObject(new { ok = true, value }), "application/json");
    }
    catch (Exception ex)
    {
        // Unwrap reflection's wrapper so the browser console shows the interop method's own error.
        var real = (ex as System.Reflection.TargetInvocationException)?.InnerException ?? ex;
        DebugLog.Exception("rpc", real);
        return Results.Content(
            JsonConvert.SerializeObject(new { ok = false, error = real.Message, type = real.GetType().Name }),
            "application/json");
    }
});

// The WebView2 shell pushes progress/chat/MCP events with PostWebMessageAsJson. Here they arrive as
// server-sent events and the frontend shim re-raises them as the same 'message' events.
app.MapGet("/events", async (HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
    var queue = events.Subscribe();
    try
    {
        while (!ct.IsCancellationRequested)
        {
            var payload = await queue.Reader.ReadAsync(ct);
            await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { }
    finally { events.Unsubscribe(queue); }
});

app.MapGet("/api/health", () => Results.Json(new { ok = true, targets = rpc.TargetNames }));

// Hash routing: every non-file path has to fall through to index.html or a refresh 404s. But a
// stray top-level *navigation* to a hash-less path (a manual URL, a refresh on a deep path, or the
// WebView breaking out of the SPA) would boot index.html onto its default '/' route and read as a
// blank/secondary page. Photino/WebKitGTK cannot veto a same-window navigation, so redirect browser
// navigations back to the SPA entry here, at the one point every same-origin navigation funnels
// through (this covers WebView2 and WKWebView too). fetch/EventSource/asset loads are not
// navigations and keep serving index.html unchanged.
app.MapFallback(async ctx =>
{
    if (SpaFallback.ShouldRedirect(ctx.Request.Path.Value, ctx.Request.Method, ctx.Request.Headers.Accept.ToString()))
    {
        ctx.Response.Redirect(SpaFallback.AppEntryPath);
        return;
    }
    ctx.Response.ContentType = "text/html";
    await ctx.Response.SendFileAsync(Path.Combine(webRoot, "index.html"));
});

var url = $"http://{host}:{port}/#/main";
Console.WriteLine($"FO4RecordEditor is running at {url}");
Console.WriteLine($"Log: {DebugLog.Path}");

if (HasFlag("--headless"))
{
    // Serve only. Useful for attaching a browser from elsewhere, or for running under a debugger.
    app.Run();
    return;
}

await app.StartAsync();

if (HasFlag("--browser")) { OpenInBrowser(url); await app.WaitForShutdownAsync(); }
else OpenNativeWindow(url);

await app.StopAsync();
return;

// ---------------------------------------------------------------------------

/// <summary>Ask the OS for a free port rather than guessing one that may be taken.</summary>
static int FreePort(string host)
{
    var l = new TcpListener(IPAddress.Parse(host), 0);
    l.Start();
    var port = ((IPEndPoint)l.LocalEndpoint).Port;
    l.Stop();
    return port;
}

/// <summary>
/// The app's own window: WebKitGTK on Linux, WebView2 on Windows, WKWebView on macOS. This is the
/// counterpart of the WPF shell's WebView2 control, so the app gets a real titlebar, its own
/// taskbar entry and its own icon rather than being a page inside somebody's browser.
/// <para>
/// Must run on the main thread, and blocks until the window closes, which is what ends the process.
/// </para>
/// </summary>
static void OpenNativeWindow(string url)
{
    // WebKitGTK's DMA-BUF renderer is the usual cause of a blank or black window on proprietary
    // drivers and on some compositors. Opt out unless the user has already made a choice.
    if (OperatingSystem.IsLinux() &&
        Environment.GetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER") == null)
        Environment.SetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER", "1");

    new Photino.NET.PhotinoWindow()
        .SetTitle("FO4RecordEditor")
        .SetUseOsDefaultSize(false)
        .SetSize(1600, 1000)
        .SetUseOsDefaultLocation(true)
        .SetContextMenuEnabled(false)
        .Load(new Uri(url))
        .WaitForClose();
}

/// <summary>
/// The `--browser` fallback: an app-mode browser window. Kept for machines with no working
/// WebKitGTK, and for debugging with real devtools.
/// </summary>
static void OpenInBrowser(string url)
{
    string[][] candidates =
    [
        ["chromium", $"--app={url}"], ["chromium-browser", $"--app={url}"],
        ["google-chrome", $"--app={url}"], ["brave-browser", $"--app={url}"],
        ["microsoft-edge", $"--app={url}"],
        ["firefox", "--new-window", url],
        ["xdg-open", url],
    ];
    foreach (var c in candidates)
    {
        try
        {
            var psi = new ProcessStartInfo(c[0]) { UseShellExecute = false };
            for (int i = 1; i < c.Length; i++) psi.ArgumentList.Add(c[i]);
            if (Process.Start(psi) != null) return;
        }
        catch { /* not installed; try the next one */ }
    }
    Console.WriteLine("Could not open a browser automatically. Open the URL above yourself.");
}

void RunMcpHeadless()
{
    string? dataOverride = ArgValue("--data");
    string? mo2Instance = ArgValue("--mo2");
    string? ckWikiPath = ArgValue("--ck-wiki");

    object? env = null;
    bool tried = false;
    Func<object?> envProvider = () =>
    {
        if (tried) return env;
        tried = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(mo2Instance))
            {
                var info = Mo2ProfileLoader.ReadInstanceInfo(mo2Instance);
                env = Mo2ProfileLoader.Load(info.InstancePath, info.Profile, info.GameDataFolder).env;
            }
            else env = MutagenLoader.BuildEnvironment(null, dataOverride).env;
        }
        catch (Exception ex) { DebugLog.Exception("--mcp env build", ex); }
        return env;
    };

    ElementRenderer.Init(MutagenLoader.FormatFormLink, MutagenLoader.FormatCondition);
    var executor = new PluginToolExecutor(envProvider, () => mo2Instance,
        () => ckWikiPath ?? ToolPaths.CkWiki());
    try { StdioMcpServer.Run(executor); }
    catch (Exception ex) { DebugLog.Exception("--mcp loop", ex); }
}
