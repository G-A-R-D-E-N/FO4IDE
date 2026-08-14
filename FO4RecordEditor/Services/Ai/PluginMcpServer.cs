using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace FO4RecordEditor.Services;

/// <summary>
/// In-process MCP server (Streamable HTTP / JSON-RPC) exposing the plugin tools so the
/// Claude Code CLI can read the live loaded plugin data -- the same tools the Anthropic
/// agent uses, backed by the same executor against the same in-memory mods.
/// </summary>
public sealed class PluginMcpServer : IDisposable
{
    private const string ProtocolVersion = "2024-11-05";

    private readonly PluginToolExecutor _executor;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();

    public string ServerName => "fo4editor";
    public string Url { get; }
    public bool IsRunning { get; private set; }

    public PluginMcpServer(PluginToolExecutor executor)
    {
        _executor = executor;
        int port = FreePort();
        Url = $"http://localhost:{port}/mcp";
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void Start()
    {
        if (IsRunning) return;
        try
        {
            _listener.Start();
            IsRunning = true;
            _ = Task.Run(() => LoopAsync(_cts.Token));
        }
        catch { IsRunning = false; }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(body))
            {
                ctx.Response.StatusCode = 202;
                ctx.Response.Close();
                return;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var method = root.TryGetProperty("method", out var mEl) ? mEl.GetString() : null;

            // Notifications (no id) -> just acknowledge.
            if (!root.TryGetProperty("id", out var idEl))
            {
                ctx.Response.StatusCode = 202;
                ctx.Response.Close();
                return;
            }
            var id = idEl.Clone();

            switch (method)
            {
                case "initialize":
                    await WriteResultAsync(ctx, id, new
                    {
                        protocolVersion = ProtocolVersion,
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = ServerName, version = "1.0.0" }
                    });
                    break;

                case "tools/list":
                    await WriteResultAsync(ctx, id, new { tools = PluginToolExecutor.McpToolDefinitions() });
                    break;

                case "tools/call":
                {
                    var p = root.GetProperty("params");
                    var toolName = p.GetProperty("name").GetString() ?? "";
                    var args = p.TryGetProperty("arguments", out var aEl) ? aEl.GetRawText() : "{}";
                    var toolResult = _executor.ExecuteWithStatus(toolName, args);
                    await WriteResultAsync(ctx, id, new
                    {
                        content = new object[] { new { type = "text", text = toolResult.Text } },
                        isError = toolResult.IsError
                    });
                    break;
                }

                case "ping":
                    await WriteResultAsync(ctx, id, new { });
                    break;

                default:
                    await WriteErrorAsync(ctx, id, -32601, $"Method not found: {method}");
                    break;
            }
        }
        catch (Exception ex)
        {
            try
            {
                ctx.Response.StatusCode = 500;
                var b = Encoding.UTF8.GetBytes(ex.Message);
                await ctx.Response.OutputStream.WriteAsync(b);
                ctx.Response.Close();
            }
            catch { }
        }
    }

    private static Task WriteResultAsync(HttpListenerContext ctx, JsonElement id, object result) =>
        WriteJsonAsync(ctx, new { jsonrpc = "2.0", id, result });

    private static Task WriteErrorAsync(HttpListenerContext ctx, JsonElement id, int code, string message) =>
        WriteJsonAsync(ctx, new { jsonrpc = "2.0", id, error = new { code, message } });

    private static async Task WriteJsonAsync(HttpListenerContext ctx, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { if (_listener.IsListening) _listener.Stop(); } catch { }
        IsRunning = false;
    }
}
