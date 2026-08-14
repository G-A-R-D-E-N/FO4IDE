using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

public class PluginMcpServerTests
{
    private static StringContent Json(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    [Fact]
    public async Task Mcp_Initialize_ToolsList_And_Call_Work()
    {
        // Reset the process-global mod registries so other tests' created plugins don't leak into
        // the "No plugins loaded" assertion below.
        MutagenLoader.LooseMods.Clear();
        MutagenLoader.EditableMods.Clear();

        var exec = new PluginToolExecutor(() => null);   // no plugins loaded
        using var server = new PluginMcpServer(exec);
        server.Start();
        server.IsRunning.Should().BeTrue();

        using var http = new HttpClient();

        // initialize
        var init = await (await http.PostAsync(server.Url,
            Json(new { jsonrpc = "2.0", id = 1, method = "initialize" }))).Content.ReadAsStringAsync();
        init.Should().Contain("protocolVersion").And.Contain("fo4editor");

        // tools/list -> MCP-format tools (inputSchema, not input_schema)
        var list = await (await http.PostAsync(server.Url,
            Json(new { jsonrpc = "2.0", id = 2, method = "tools/list" }))).Content.ReadAsStringAsync();
        list.Should().Contain("list_plugins").And.Contain("search_records").And.Contain("inputSchema");
        list.Should().NotContain("input_schema");

        // tools/call list_plugins -> friendly "none" result wrapped in MCP content
        var call = await (await http.PostAsync(server.Url,
            Json(new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new { name = "list_plugins", arguments = new { } }
            }))).Content.ReadAsStringAsync();
        call.Should().Contain("No plugins loaded").And.Contain("\"type\":\"text\"");
    }
}
