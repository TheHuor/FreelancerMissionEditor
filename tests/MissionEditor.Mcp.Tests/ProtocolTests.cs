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
        string workspace = Path.Combine(Path.GetTempPath(),"mission-mcp-protocol-"+Guid.NewGuid());
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace,"sample.ini"),"[Trigger]\nnickname = start\nCnd_True = no_params\nAct_ActTrig = missing");
        try
        {
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            while(root is not null && !File.Exists(Path.Combine(root.FullName,"MissionEditor.slnx"))) root=root.Parent;
#if DEBUG
            const string configuration="Debug";
#else
            const string configuration="Release";
#endif
            string dll=Path.Combine(root!.FullName,"src","MissionEditor.Mcp","bin",configuration,"net10.0","MissionEditor.Mcp.dll");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name="Mission Editor integration test", Command="dotnet", Arguments=[dll,"--workspace",workspace],
            });
            await using var client=await McpClient.CreateAsync(transport,new McpClientOptions { ProtocolVersion=protocol }, cancellationToken:timeout.Token);
            var tools=await client.ListToolsAsync(cancellationToken:timeout.Token);
            Assert.Contains(tools,t=>t.Name=="inspect_mission");
            Assert.Contains(tools,t=>t.Name=="export_mission_draft");
            var result=await client.CallToolAsync("inspect_mission",new Dictionary<string,object?> {{"path","sample.ini"}},cancellationToken:timeout.Token);
            Assert.False(result.IsError ?? false);
            string text=string.Join("",result.Content.OfType<TextContentBlock>().Select(c=>c.Text));
            using var json=JsonDocument.Parse(text);
            Assert.Equal(1,json.RootElement.GetProperty("triggerCount").GetInt32());
            var failed=await client.CallToolAsync("inspect_mission",new Dictionary<string,object?> {{"path","../outside.ini"}},cancellationToken:timeout.Token);
            Assert.True(failed.IsError);
            var draft=await client.CallToolAsync("prepare_new_mission",new Dictionary<string,object?> {{"text","[Trigger]\nnickname = fresh\nCnd_True = no_params"}},cancellationToken:timeout.Token);
            Assert.False(draft.IsError ?? false);
            string draftJson=string.Join("",draft.Content.OfType<TextContentBlock>().Select(c=>c.Text));
            using var prepared=JsonDocument.Parse(draftJson);
            string id=prepared.RootElement.GetProperty("id").GetString()!;
            var disabled=await client.CallToolAsync("export_mission_draft",new Dictionary<string,object?> {{"id",id},{"outputPath","fresh.ini"}},cancellationToken:timeout.Token);
            Assert.True(disabled.IsError);
            Assert.False(File.Exists(Path.Combine(workspace,"fresh.ini")));
        }
        finally { Directory.Delete(workspace,true); }
    }
}
