using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MissionEditor.App;

/// <summary>The AI clients whose configuration the editor can update.</summary>
public enum AgentClient
{
  /// <summary>Claude Code (command line and desktop app), user scope in <c>%USERPROFILE%\.claude.json</c>.</summary>
  ClaudeCode,

  /// <summary>Claude Desktop, <c>%APPDATA%\Claude\claude_desktop_config.json</c>.</summary>
  ClaudeDesktop,

  /// <summary>Codex, <c>%USERPROFILE%\.codex\config.toml</c>.</summary>
  Codex,
}

/// <summary>
/// Adds the MCP server entry to a client's configuration file. Only the entry named
/// <see cref="AgentConnection.ServerName"/> is created or replaced; everything else in the file stays.
/// The previous file is kept as "*.mission-editor.bak" and the new one is written atomically.
/// </summary>
public static partial class AgentClientConfig
{
  /// <summary>The suffix of the backup written before a configuration file is changed.</summary>
  public const string BackupSuffix = ".mission-editor.bak";

  private static readonly JsonSerializerOptions WriteOptions = new ()
  {
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };

  /// <summary>The configuration file a client reads (the first of <see cref="ConfigPaths"/>).</summary>
  public static string ConfigPath (AgentClient client) => ConfigPaths (client)[0];

  /// <summary>
  /// The configuration files to update for a client. Claude Desktop from the Microsoft Store (MSIX) reads its
  /// own copy under %LOCALAPPDATA%\Packages once it has one; then both that copy and %APPDATA% are updated.
  /// </summary>
  public static IReadOnlyList<string> ConfigPaths (AgentClient client)
  {
    string userProfile = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);
    if (client == AgentClient.ClaudeCode)
      return [Path.Combine (userProfile, ".claude.json")];
    if (client == AgentClient.Codex)
      return [Path.Combine (userProfile, ".codex", "config.toml")];

    string roaming = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json");
    string packages = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "Packages");
    IEnumerable<string> packaged = Directory.Exists (packages)
        ? Directory.EnumerateDirectories (packages, "AnthropicPBC.Claude_*")
            .Select (package => Path.Combine (package, "LocalCache", "Roaming", "Claude", "claude_desktop_config.json"))
            .Where (File.Exists)
        : [];
    return [.. packaged, roaming];
  }

  /// <summary>True when the file already has an entry for the server (whatever its settings).</summary>
  public static bool IsConfigured (AgentClient client, string configPath)
  {
    try
    {
      if (!File.Exists (configPath))
        return false;
      string text = File.ReadAllText (configPath);
      return client == AgentClient.Codex
          ? CodexSectionHeader ().IsMatch (text)
          : JsonNode.Parse (text)?["mcpServers"]?[AgentConnection.ServerName] is not null;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
    {
      return false;
    }
  }

  /// <summary>
  /// The folder limit (empty for none) and export flag given to the server in a client's configuration, or null
  /// when it has no entry.
  /// </summary>
  public static (string Workspace, bool AllowWrites)? ConfiguredServer (AgentClient client, string configPath)
  {
    try
    {
      if (!File.Exists (configPath))
        return null;
      string text = File.ReadAllText (configPath);
      IEnumerable<string> arguments;
      if (client == AgentClient.Codex)
      {
        Match section = CodexSectionHeader ().Match (text);
        if (!section.Success)
          return null;
        string body = text[(section.Index + section.Length)..];
        int nextTable = Regex.Match (body, @"^\s*\[", RegexOptions.Multiline) is { Success: true } next ? next.Index : body.Length;
        Match args = Regex.Match (body[..nextTable], @"^\s*args\s*=\s*\[(.*)\]", RegexOptions.Multiline);
        arguments = args.Success
            ? Regex.Matches (args.Groups[1].Value, "\"((?:[^\"\\\\]|\\\\.)*)\"")
                .Select (match => match.Groups[1].Value.Replace ("\\\"", "\"").Replace ("\\\\", "\\"))
            : [];
      }
      else
      {
        if (JsonNode.Parse (text)?["mcpServers"]?[AgentConnection.ServerName] is not JsonObject server)
          return null;
        arguments = server["args"] is JsonArray array ? array.Select (item => item?.GetValue<string> () ?? string.Empty) : [];
      }
      // An empty workspace means the server runs without a folder limit.
      List<string> list = arguments.ToList ();
      int index = list.FindIndex (argument => argument is "--allow-root" or "--workspace");
      return (index >= 0 && index + 1 < list.Count ? list[index + 1] : string.Empty, list.Contains ("--allow-writes"));
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
    {
      return null;
    }
  }

  /// <summary>The server entries of all clients that have it registered.</summary>
  public static IReadOnlyList<(AgentClient Client, string Workspace, bool AllowWrites)> ConfiguredServers () =>
      Enum.GetValues<AgentClient> ()
          .Select (client => (Client: client, Server: ConfiguredServer (client, ConfigPath (client))))
          .Where (entry => entry.Server is not null)
          .Select (entry => (entry.Client, entry.Server!.Value.Workspace, entry.Server!.Value.AllowWrites))
          .ToList ();

  /// <summary>
  /// What to paste by hand and where, depending on the current state of the client's file: a whole
  /// file, a <c>"mcpServers"</c> member for an object without one, or just the server entry.
  /// </summary>
  public static (string Instruction, string Snippet) ManualSnippet (AgentClient client, string configPath, string executable, string workspace, bool allowWrites, string gameFolder = "")
  {
    if (client == AgentClient.Codex)
    {
      string toml = AgentConnection.CodexConfiguration (executable, workspace, allowWrites, gameFolder);
      return File.Exists (configPath)
          ? (IsConfigured (client, configPath)
              ? $"{configPath} enthält schon [mcp_servers.{AgentConnection.ServerName}]: diesen Abschnitt bis zur nächsten Zeile mit [ durch den folgenden ersetzen."
              : $"Ans Ende von {configPath} anhängen (mit einer Leerzeile davor).", toml)
          : ($"{configPath} gibt es noch nicht: Datei mit diesem Inhalt anlegen.", toml);
    }

    bool includeType = client == AgentClient.ClaudeCode;
    string whole = UpdateJson (null, executable, workspace, allowWrites, gameFolder, includeType);
    if (!File.Exists (configPath))
      return ($"{configPath} gibt es noch nicht: Datei mit diesem Inhalt anlegen.", whole);

    JsonObject? root = null;
    try
    {
      root = JsonNode.Parse (File.ReadAllText (configPath)) as JsonObject;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
    {
      // Unreadable or invalid: fall through to the generic instruction.
    }
    if (root is null)
      return ($"{configPath} ließ sich nicht als JSON lesen. Den Eintrag \"{AgentConnection.ServerName}\" aus diesem Beispiel unter \"mcpServers\" übernehmen.", whole);

    // The entry itself, indented as it sits inside "mcpServers".
    JsonNode entry = JsonNode.Parse (whole)!["mcpServers"]![AgentConnection.ServerName]!;
    string entryText = Indent ($"\"{AgentConnection.ServerName}\": {entry.ToJsonString (WriteOptions)}", "    ");
    if (root["mcpServers"] is JsonObject servers)
    {
      string comma = servers.Count > 0 ? "," : string.Empty;
      string verb = servers.ContainsKey (AgentConnection.ServerName) ? "Den vorhandenen Eintrag \"freelancer-missions\" darin löschen und dann diesen" : "Diesen";
      return ($"{configPath} hat schon \"mcpServers\": {{ … }}. {verb} Eintrag direkt nach der Zeile mit \"mcpServers\": {{ einfügen"
          + (comma.Length > 0 ? " – mit dem Komma am Ende, weil danach weitere Einträge folgen." : "."), entryText + comma);
    }
    string member = $"  \"mcpServers\": {{\n{entryText}\n  }}" + (root.Count > 0 ? "," : string.Empty);
    return ($"{configPath} hat noch kein \"mcpServers\". Diesen Teil direkt nach der ersten Zeile {{ einfügen"
        + (root.Count > 0 ? " – mit dem Komma am Ende, weil danach die bisherigen Einstellungen folgen." : "."), member);
  }

  /// <summary>Creates or replaces the server entry in the client's configuration file.</summary>
  /// <returns>The backup path, or null when the file did not exist before.</returns>
  /// <exception cref="InvalidDataException">The existing file is not valid JSON or not a JSON object.</exception>
  public static string? AddServer (AgentClient client, string configPath, string executable, string workspace, bool allowWrites, string gameFolder = "")
  {
    string? existing = File.Exists (configPath) ? File.ReadAllText (configPath) : null;
    string updated = client == AgentClient.Codex
        ? UpdateCodex (existing ?? string.Empty, executable, workspace, allowWrites, gameFolder)
        : UpdateJson (existing, executable, workspace, allowWrites, gameFolder, includeType: client == AgentClient.ClaudeCode);

    string directory = Path.GetDirectoryName (configPath)!;
    Directory.CreateDirectory (directory);
    string? backup = null;
    if (existing is not null)
    {
      backup = configPath + BackupSuffix;
      File.Copy (configPath, backup, overwrite: true);
    }
    string temporary = Path.Combine (directory, $".{Path.GetFileName (configPath)}.{Guid.NewGuid ():N}.tmp");
    try
    {
      File.WriteAllText (temporary, updated, new UTF8Encoding (false));
      File.Move (temporary, configPath, overwrite: true);
    }
    finally
    {
      if (File.Exists (temporary))
        File.Delete (temporary);
    }
    return backup;
  }

  private static string UpdateJson (string? existing, string executable, string workspace, bool allowWrites, string gameFolder, bool includeType)
  {
    JsonObject root;
    try
    {
      root = string.IsNullOrWhiteSpace (existing)
          ? new JsonObject ()
          : JsonNode.Parse (existing) as JsonObject ?? throw new InvalidDataException ("Die Konfigurationsdatei enthält kein JSON-Objekt.");
    }
    catch (JsonException exception)
    {
      throw new InvalidDataException ($"Die Konfigurationsdatei ist kein gültiges JSON ({exception.Message}). Sie wurde nicht verändert.", exception);
    }
    if (root["mcpServers"] is not JsonObject servers)
    {
      if (root["mcpServers"] is not null)
        throw new InvalidDataException ("\"mcpServers\" in der Konfigurationsdatei ist kein JSON-Objekt. Die Datei wurde nicht verändert.");
      servers = new JsonObject ();
      root["mcpServers"] = servers;
    }
    var server = new JsonObject ();
    if (includeType)
      server["type"] = "stdio";
    server["command"] = executable;
    server["args"] = new JsonArray (AgentConnection.Arguments (workspace, allowWrites, gameFolder).Select (argument => (JsonNode?)JsonValue.Create (argument)).ToArray ());
    servers[AgentConnection.ServerName] = server;
    return root.ToJsonString (WriteOptions);
  }

  // Replaces the [mcp_servers.freelancer-missions] table (header up to the next table header) or appends it.
  private static string UpdateCodex (string existing, string executable, string workspace, bool allowWrites, string gameFolder = "")
  {
    string block = AgentConnection.CodexConfiguration (executable, workspace, allowWrites, gameFolder).Replace ("\r\n", "\n").TrimEnd ('\n');
    string newline = existing.Contains ("\r\n") ? "\r\n" : "\n";
    var lines = existing.Replace ("\r\n", "\n").Split ('\n').ToList ();
    int start = lines.FindIndex (line => CodexSectionHeader ().IsMatch (line));
    if (start >= 0)
    {
      int end = start + 1;
      while (end < lines.Count && !lines[end].TrimStart ().StartsWith ('['))
        end++;
      // Keep blank lines that separate the table from the next one.
      while (end > start + 1 && lines[end - 1].Trim ().Length == 0)
        end--;
      lines.RemoveRange (start, end - start);
      lines.InsertRange (start, block.Split ('\n'));
      return string.Join (newline, lines);
    }
    string trimmed = existing.TrimEnd ('\r', '\n');
    string separator = trimmed.Length == 0 ? string.Empty : newline + newline;
    return trimmed + separator + block.Replace ("\n", newline) + newline;
  }

  private static string Indent (string text, string prefix) =>
      string.Join ("\n", text.Replace ("\r\n", "\n").Split ('\n').Select (line => prefix + line));

  [GeneratedRegex (@"^\s*\[\s*mcp_servers\s*\.\s*(""freelancer-missions""|freelancer-missions)\s*\]\s*$", RegexOptions.Multiline)]
  private static partial Regex CodexSectionHeader ();
}
