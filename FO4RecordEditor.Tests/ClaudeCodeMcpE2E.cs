using System.Diagnostics;
using System.Text;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

public class ClaudeCodeMcpE2E
{
    private readonly ITestOutputHelper _out;
    public ClaudeCodeMcpE2E(ITestOutputHelper o) => _out = o;

    [Fact(Skip = "Real Claude Code CLI call (needs sign-in, consumes quota). Remove Skip to run manually. Verified passing 2026-06-10 (14s, MCP transport OK).")]
    public async Task ClaudeCode_CallsMcpTool_AgainstLiveServer()
    {
        var exec = new PluginToolExecutor(() => null);
        using var server = new PluginMcpServer(exec);
        server.Start();
        server.IsRunning.Should().BeTrue();

        var cfg = $"{{\"mcpServers\":{{\"fo4editor\":{{\"type\":\"http\",\"url\":\"{server.Url}\"}}}}}}";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("claude");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--output-format"); psi.ArgumentList.Add("text");
        psi.ArgumentList.Add("--mcp-config"); psi.ArgumentList.Add(cfg);
        psi.ArgumentList.Add("--allowedTools"); psi.ArgumentList.Add("mcp__fo4editor");

        var proc = Process.Start(psi)!;
        await proc.StandardInput.WriteAsync(
            "Call the list_plugins tool from the fo4editor MCP server and reply with exactly the text it returned, nothing else.");
        proc.StandardInput.Close();

        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        _out.WriteLine("STDOUT:\n" + stdout);
        _out.WriteLine("STDERR:\n" + stderr);

        stdout.Should().Contain("No plugins loaded");
    }
}
