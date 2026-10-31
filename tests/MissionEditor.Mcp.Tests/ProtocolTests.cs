using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MissionEditor.Mcp.Tests;

public sealed class ProtocolTests
{
  [Theory]
  [InlineData (null)]
  [InlineData ("2025-06-18")]
  public async Task RealStdioClientCanDiscoverAndCallTools (string? protocol)
  {
    string workspace = Path.Combine (Path.GetTempPath (), "mission-mcp-protocol-" + Guid.NewGuid ());
    Directory.CreateDirectory (workspace);
    File.WriteAllText (Path.Combine (workspace, "sample.ini"), "[Trigger]\nnickname = start\nCnd_True = no_params\nAct_ActTrig = missing");
    try
    {
      using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (30));
      var transport = new StdioClientTransport (new StdioClientTransportOptions
      {
        Name = "Mission Editor integration test",
        Command = "dotnet",
        Arguments = [ServerDll (), "--workspace", workspace],
      });
      await using McpClient client = await McpClient.CreateAsync (transport, new McpClientOptions { ProtocolVersion = protocol },
          cancellationToken: timeout.Token);

      IList<McpClientTool> tools = await client.ListToolsAsync (cancellationToken: timeout.Token);
      Assert.Contains (tools, tool => tool.Name == "inspect_mission");
      Assert.Contains (tools, tool => tool.Name == "export_mission_draft");
      Assert.Contains (tools, tool => tool.Name == "list_sections");
      // Every parameter of every tool is described, so an agent knows what to pass.
      foreach (McpClientTool tool in tools)
      {
        foreach (JsonProperty parameter in tool.JsonSchema.GetProperty ("properties").EnumerateObject ())
          Assert.True (parameter.Value.TryGetProperty ("description", out _), $"{tool.Name}.{parameter.Name} hat keine Beschreibung.");
      }
      Assert.Contains ("--allow-writes", client.ServerInstructions);

      CallToolResult inspected = await Call (client, "inspect_mission", new ()
      {
        ["path"] = "sample.ini"
      }, timeout.Token);
      Assert.False (inspected.IsError ?? false);
      using (JsonDocument summary = JsonDocument.Parse (TextOf (inspected)))
        Assert.Equal (1, summary.RootElement.GetProperty ("triggerCount").GetInt32 ());

      // A finding without a related line still matches the advertised output schema (null, not a missing property);
      // clients such as Claude Code reject the whole result otherwise.
      CallToolResult checkedMission = await Call (client, "check_mission", new ()
      {
        ["path"] = "sample.ini"
      }, timeout.Token);
      Assert.False (checkedMission.IsError ?? false);
      McpClientTool checkTool = tools.Single (tool => tool.Name == "check_mission");
      Assert.NotNull (checkTool.ProtocolTool.OutputSchema);
      Assert.NotNull (checkedMission.StructuredContent);
      AssertRequiredPropertiesPresent (checkTool.ProtocolTool.OutputSchema.Value,
          JsonSerializer.SerializeToElement (checkedMission.StructuredContent), "check_mission");

      CallToolResult outside = await Call (client, "inspect_mission", new ()
      {
        ["path"] = "../outside.ini"
      }, timeout.Token);
      Assert.True (outside.IsError);

      CallToolResult draft = await Call (client, "prepare_new_mission",
          new ()
          {
            ["text"] = "[Trigger]\nnickname = fresh\nCnd_True = no_params"
          }, timeout.Token);
      Assert.False (draft.IsError ?? false);
      string draftId;
      using (JsonDocument prepared = JsonDocument.Parse (TextOf (draft)))
        draftId = prepared.RootElement.GetProperty ("id").GetString ()!;

      // Started without --allow-writes: export is refused and nothing is written.
      CallToolResult export = await Call (client, "export_mission_draft",
          new ()
          {
            ["id"] = draftId,
            ["outputPath"] = "fresh.ini"
          }, timeout.Token);
      Assert.True (export.IsError);
      Assert.False (File.Exists (Path.Combine (workspace, "fresh.ini")));
    }
    finally
    {
      Directory.Delete (workspace, true);
    }
  }

  [Fact]
  public async Task ServerWithoutFolderChecksAndDraftsByAbsolutePath ()
  {
    string folder = Path.Combine (Path.GetTempPath (), "mission-mcp-nofolder-" + Guid.NewGuid ());
    Directory.CreateDirectory (folder);
    string mission = Path.Combine (folder, "sample.ini");
    File.WriteAllText (mission, "[Trigger]\nnickname = start\nCnd_True = no_params\nAct_ActTrig = missing");
    try
    {
      using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (30));
      var transport = new StdioClientTransport (new StdioClientTransportOptions
      {
        Name = "Mission Editor integration test",
        Command = "dotnet",
        Arguments = [ServerDll (), "--allow-writes"],
      });
      await using McpClient client = await McpClient.CreateAsync (transport, cancellationToken: timeout.Token);
      Assert.Contains ("absolutem Pfad", client.ServerInstructions);

      CallToolResult checkedMission = await Call (client, "check_mission", new ()
      {
        ["path"] = mission
      }, timeout.Token);
      Assert.False (checkedMission.IsError ?? false);
      Assert.Contains ("unknown-trigger", TextOf (checkedMission));

      CallToolResult inspected = await Call (client, "inspect_mission", new ()
      {
        ["path"] = mission
      }, timeout.Token);
      string sha256;
      using (JsonDocument summary = JsonDocument.Parse (TextOf (inspected)))
        sha256 = summary.RootElement.GetProperty ("sha256").GetString ()!;
      CallToolResult draft = await Call (client, "prepare_section_edit", new ()
      {
        ["path"] = mission,
        ["expectedSha256"] = sha256,
        ["sectionIndex"] = 0,
        ["replacementText"] = "[Trigger]\nnickname = start\nCnd_True = no_params",
      }, timeout.Token);
      Assert.False (draft.IsError ?? false);
      string draftId;
      using (JsonDocument prepared = JsonDocument.Parse (TextOf (draft)))
        draftId = prepared.RootElement.GetProperty ("id").GetString ()!;

      string copy = Path.Combine (folder, "fixed.ini");
      CallToolResult export = await Call (client, "export_mission_draft", new ()
      {
        ["id"] = draftId,
        ["outputPath"] = copy
      }, timeout.Token);
      Assert.False (export.IsError ?? false);
      Assert.True (File.Exists (copy));

      CallToolResult relative = await Call (client, "inspect_mission", new ()
      {
        ["path"] = "sample.ini"
      }, timeout.Token);
      Assert.True (relative.IsError);
      Assert.Contains ("kein absoluter Pfad", TextOf (relative));
    }
    finally
    {
      Directory.Delete (folder, true);
    }
  }

  private static Task<CallToolResult> Call (McpClient client, string tool, Dictionary<string, object?> arguments, CancellationToken cancellation) =>
      client.CallToolAsync (tool, arguments, cancellationToken: cancellation).AsTask ();

  // Checks the "required" lists of the schema recursively through objects and arrays; enough for the generated record schemas.
  private static void AssertRequiredPropertiesPresent (JsonElement schema, JsonElement value, string location)
  {
    if (value.ValueKind == JsonValueKind.Object)
    {
      if (schema.TryGetProperty ("required", out JsonElement required))
      {
        foreach (JsonElement name in required.EnumerateArray ())
          Assert.True (value.TryGetProperty (name.GetString ()!, out _), $"{location}.{name.GetString ()} fehlt, ist im Schema aber Pflicht.");
      }
      if (schema.TryGetProperty ("properties", out JsonElement properties))
      {
        foreach (JsonProperty property in properties.EnumerateObject ())
        {
          if (value.TryGetProperty (property.Name, out JsonElement child))
            AssertRequiredPropertiesPresent (property.Value, child, $"{location}.{property.Name}");
        }
      }
    }
    else if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty ("items", out JsonElement items))
    {
      int index = 0;
      foreach (JsonElement element in value.EnumerateArray ())
        AssertRequiredPropertiesPresent (items, element, $"{location}[{index++}]");
    }
  }

  private static string TextOf (CallToolResult result) => string.Concat (result.Content.OfType<TextContentBlock> ().Select (block => block.Text));

  // The server is started as a real process from the build output of the same configuration.
  private static string ServerDll ()
  {
    DirectoryInfo? root = new (AppContext.BaseDirectory);
    while (root is not null && !File.Exists (Path.Combine (root.FullName, "MissionEditor.slnx")))
      root = root.Parent;
#if DEBUG
    const string Configuration = "Debug";
#else
        const string Configuration = "Release";
#endif
    return Path.Combine (root!.FullName, "src", "MissionEditor.Mcp", "bin", Configuration, "net10.0", "MissionEditor.Mcp.dll");
  }
}
