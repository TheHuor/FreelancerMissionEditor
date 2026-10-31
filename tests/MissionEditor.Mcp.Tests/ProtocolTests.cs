using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MissionEditor.Mcp.Tests;

public sealed class ProtocolTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("2025-06-18")]
    public async Task RealStdioClientCanDiscoverAndCallTools(string? protocol)
    {
        string workspace = Path.Combine(Path.GetTempPath(), "mission-mcp-protocol-" + Guid.NewGuid());
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "sample.ini"), "[Trigger]\nnickname = start\nCnd_True = no_params\nAct_ActTrig = missing");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "Mission Editor integration test",
                Command = "dotnet",
                Arguments = [ServerDll(), "--workspace", workspace],
            });
            await using McpClient client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocol },
                cancellationToken: timeout.Token);

            IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            Assert.Contains(tools, tool => tool.Name == "inspect_mission");
            Assert.Contains(tools, tool => tool.Name == "export_mission_draft");
            Assert.Contains(tools, tool => tool.Name == "list_sections");
            // Every parameter of every tool is described, so an agent knows what to pass.
            foreach (McpClientTool tool in tools)
            {
                foreach (JsonProperty parameter in tool.JsonSchema.GetProperty("properties").EnumerateObject())
                    Assert.True(parameter.Value.TryGetProperty("description", out _), $"{tool.Name}.{parameter.Name} hat keine Beschreibung.");
            }
            Assert.Contains("--allow-writes", client.ServerInstructions);

            CallToolResult inspected = await Call(client, "inspect_mission", new() { ["path"] = "sample.ini" }, timeout.Token);
            Assert.False(inspected.IsError ?? false);
            using (JsonDocument summary = JsonDocument.Parse(TextOf(inspected)))
                Assert.Equal(1, summary.RootElement.GetProperty("triggerCount").GetInt32());

            CallToolResult outside = await Call(client, "inspect_mission", new() { ["path"] = "../outside.ini" }, timeout.Token);
            Assert.True(outside.IsError);

            CallToolResult draft = await Call(client, "prepare_new_mission",
                new() { ["text"] = "[Trigger]\nnickname = fresh\nCnd_True = no_params" }, timeout.Token);
            Assert.False(draft.IsError ?? false);
            string draftId;
            using (JsonDocument prepared = JsonDocument.Parse(TextOf(draft)))
                draftId = prepared.RootElement.GetProperty("id").GetString()!;

            // Started without --allow-writes: export is refused and nothing is written.
            CallToolResult export = await Call(client, "export_mission_draft",
                new() { ["id"] = draftId, ["outputPath"] = "fresh.ini" }, timeout.Token);
            Assert.True(export.IsError);
            Assert.False(File.Exists(Path.Combine(workspace, "fresh.ini")));
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    private static Task<CallToolResult> Call(McpClient client, string tool, Dictionary<string, object?> arguments, CancellationToken cancellation) =>
        client.CallToolAsync(tool, arguments, cancellationToken: cancellation).AsTask();

    private static string TextOf(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    // The server is started as a real process from the build output of the same configuration.
    private static string ServerDll()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MissionEditor.slnx")))
            root = root.Parent;
#if DEBUG
        const string Configuration = "Debug";
#else
        const string Configuration = "Release";
#endif
        return Path.Combine(root!.FullName, "src", "MissionEditor.Mcp", "bin", Configuration, "net10.0", "MissionEditor.Mcp.dll");
    }
}
