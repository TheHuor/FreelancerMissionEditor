using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MissionEditor.App;

/// <summary>
/// Builds the client configuration that connects an AI agent (Claude Code, Claude Desktop, Codex) to
/// the MCP server, finds the server executable and remembers the user's choices.
/// </summary>
public static class AgentConnection
{
  /// <summary>The name the server is registered under in the agent's configuration.</summary>
  public const string ServerName = "freelancer-missions";

  /// <summary>The executable of the MCP server.</summary>
  public const string ServerExecutable = "MissionEditor.Mcp.exe";

  private static readonly string SettingsPath = Path.Combine (
      Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "FreelancerMissionEditor", "agent-connection.json");

  /// <summary>The server arguments; an empty workspace means no folder limit (missions are named by absolute path).</summary>
  public static IReadOnlyList<string> Arguments (string workspace, bool allowWrites)
  {
    var arguments = new List<string> ();
    if (workspace.Length > 0)
      arguments.AddRange (["--allow-root", workspace]);
    if (allowWrites)
      arguments.Add ("--allow-writes");
    return arguments;
  }

  /// <summary>The command line for <c>claude mcp add</c> (Claude Code), registered for all projects of the user.</summary>
  public static string ClaudeCodeCommand (string executable, string workspace, bool allowWrites) =>
      $"claude mcp add --scope user {ServerName} -- {Quote (executable)} "
      + string.Join (' ', Arguments (workspace, allowWrites).Select (argument => argument.StartsWith ("--", StringComparison.Ordinal) ? argument : Quote (argument)));

  /// <summary>The <c>mcpServers</c> JSON block (Claude Desktop and other clients with that format).</summary>
  public static string JsonConfiguration (string executable, string workspace, bool allowWrites) =>
      McpServersJson (executable, workspace, allowWrites, includeType: false);

  /// <summary>
  /// The <c>mcpServers</c> block as Claude Code stores it in <c>%USERPROFILE%\.claude.json</c>, for adding
  /// the server by hand when the <c>claude</c> command line is not installed (e.g. only the desktop app).
  /// </summary>
  public static string ClaudeCodeJson (string executable, string workspace, bool allowWrites) =>
      McpServersJson (executable, workspace, allowWrites, includeType: true);

  /// <summary>True when a <c>claude</c> command is on the PATH, so the <c>claude mcp add</c> command works.</summary>
  public static bool IsClaudeCommandAvailable () => ProgramLocator.Find ("claude") is not null;

  private static string McpServersJson (string executable, string workspace, bool allowWrites, bool includeType)
  {
    var server = new JsonObject ();
    if (includeType)
      server["type"] = "stdio";
    server["command"] = executable;
    server["args"] = new JsonArray (Arguments (workspace, allowWrites).Select (argument => (JsonNode?)JsonValue.Create (argument)).ToArray ());
    var root = new JsonObject { ["mcpServers"] = new JsonObject { [ServerName] = server } };
    return root.ToJsonString (new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
  }

  /// <summary>The <c>[mcp_servers]</c> TOML block for Codex (<c>~/.codex/config.toml</c>).</summary>
  public static string CodexConfiguration (string executable, string workspace, bool allowWrites)
  {
    var builder = new StringBuilder ();
    builder.Append ("[mcp_servers.").Append (ServerName).Append (']').AppendLine ();
    builder.Append ("command = ").Append (TomlString (executable)).AppendLine ();
    builder.Append ("args = [").Append (string.Join (", ", Arguments (workspace, allowWrites).Select (TomlString))).Append (']').AppendLine ();
    return builder.ToString ();
  }

  /// <summary>
  /// The server executable to suggest: next to the editor, in a sibling "mcp" folder of an extracted
  /// release, or in the build output of a source checkout. Null when none exists.
  /// </summary>
  public static string? FindServerExecutable ()
  {
    string baseDirectory = AppContext.BaseDirectory;
    var candidates = new List<string>
        {
            Path.Combine(baseDirectory, ServerExecutable),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "mcp", ServerExecutable)),
        };
    DirectoryInfo? repository = new (baseDirectory);
    while (repository is not null && !File.Exists (Path.Combine (repository.FullName, "MissionEditor.slnx")))
      repository = repository.Parent;
    if (repository is not null)
    {
      candidates.Add (Path.Combine (repository.FullName, "artifacts", "publish", "mcp", ServerExecutable));
      foreach (string configuration in new[] { "Release", "Debug" })
        candidates.Add (Path.Combine (repository.FullName, "src", "MissionEditor.Mcp", "bin", configuration, "net10.0", ServerExecutable));
    }
    return candidates.FirstOrDefault (File.Exists);
  }

  /// <summary>Starts the server with <c>--version</c> and returns its version, or an error text.</summary>
  public static (bool Success, string Text) ProbeServer (string executable)
  {
    if (!File.Exists (executable))
      return (false, "Datei nicht gefunden.");
    try
    {
      using var process = Process.Start (new ProcessStartInfo (executable, "--version")
      {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
      });
      if (process is null)
        return (false, "Server ließ sich nicht starten.");
      // The server writes everything except protocol messages to stderr.
      string output = process.StandardError.ReadToEnd ().Trim ();
      if (!process.WaitForExit (5000))
      {
        process.Kill ();
        return (false, "Server antwortet nicht.");
      }
      return process.ExitCode == 0 && output.Length > 0
          ? (true, output.Split ('+')[0])
          : (false, $"Unerwartete Antwort (Exitcode {process.ExitCode}).");
    }
    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
    {
      return (false, exception.Message);
    }
  }

  /// <summary>The remembered server path, workspace and export choice, or empty values.</summary>
  public static (string? Executable, string? Workspace, bool AllowWrites) LoadSettings ()
  {
    try
    {
      if (!File.Exists (SettingsPath))
        return (null, null, false);
      JsonNode? settings = JsonNode.Parse (File.ReadAllText (SettingsPath));
      return (settings?["executable"]?.GetValue<string> (), settings?["workspace"]?.GetValue<string> (),
          settings?["allowWrites"]?.GetValue<bool> () ?? false);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
    {
      return (null, null, false);
    }
  }

  /// <summary>Remembers the server path, workspace and export choice; failures are ignored (it is a convenience).</summary>
  public static void SaveSettings (string executable, string workspace, bool allowWrites)
  {
    try
    {
      Directory.CreateDirectory (Path.GetDirectoryName (SettingsPath)!);
      var settings = new JsonObject { ["executable"] = executable, ["workspace"] = workspace, ["allowWrites"] = allowWrites };
      File.WriteAllText (SettingsPath, settings.ToJsonString (new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
      // Not remembering the choice is harmless.
    }
  }

  // Windows paths cannot contain '"', so wrapping in quotes is enough for cmd and PowerShell.
  private static string Quote (string value) => $"\"{value}\"";

  private static string TomlString (string value) => "\"" + value.Replace ("\\", "\\\\").Replace ("\"", "\\\"") + "\"";
}
