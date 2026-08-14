using System.IO;
using System.Text;
using System.Text.Json;

namespace FO4RecordEditor.Services;







public static class StdioMcpServer
{
    private const string ProtocolVersion = "2024-11-05";


    public static void Run(PluginToolExecutor executor)
    {
        using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var response = HandleLine(line, executor);
            if (response != null) writer.Write(response + "\n");
        }
    }





    public static string? HandleLine(string line, PluginToolExecutor executor)
    {
        JsonElement root;
        try { using var doc = JsonDocument.Parse(line); root = doc.RootElement.Clone(); }
        catch { return null; }

        var method = root.TryGetProperty("method", out var mEl) ? mEl.GetString() : null;


        if (!root.TryGetProperty("id", out var idEl)) return null;
        var id = idEl.Clone();

        switch (method)
        {
            case "initialize":
                return Result(id, new
                {
                    protocolVersion = ProtocolVersion,
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "fo4editor", version = "1.0.0" }
                });

            case "tools/list":
                return Result(id, new { tools = PluginToolExecutor.McpToolDefinitions() });

            case "tools/call":
            {
                var p = root.GetProperty("params");
                var toolName = p.GetProperty("name").GetString() ?? "";
                var args = p.TryGetProperty("arguments", out var aEl) ? aEl.GetRawText() : "{}";
                var toolResult = executor.ExecuteWithStatus(toolName, args);
                return Result(id, new
                {
                    content = new object[] { new { type = "text", text = toolResult.Text } },
                    isError = toolResult.IsError
                });
            }

            case "ping":
                return Result(id, new { });

            default:
                return JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new { code = -32601, message = $"Method not found: {method}" }
                });
        }
    }

    private static string Result(JsonElement id, object result) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
}
