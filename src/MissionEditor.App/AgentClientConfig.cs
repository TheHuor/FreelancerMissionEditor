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

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The configuration file a client reads.</summary>
    public static string ConfigPath(AgentClient client) => client switch
    {
        AgentClient.ClaudeCode => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json"),
        AgentClient.ClaudeDesktop => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json"),
        _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml"),
    };

    /// <summary>True when the file already has an entry for the server (whatever its settings).</summary>
    public static bool IsConfigured(AgentClient client, string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
                return false;
            string text = File.ReadAllText(configPath);
            return client == AgentClient.Codex
                ? CodexSectionHeader().IsMatch(text)
                : JsonNode.Parse(text)?["mcpServers"]?[AgentConnection.ServerName] is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// What to paste by hand and where, depending on the current state of the client's file: a whole
    /// file, a <c>"mcpServers"</c> member for an object without one, or just the server entry.
    /// </summary>
    public static (string Instruction, string Snippet) ManualSnippet(AgentClient client, string configPath, string executable, string workspace, bool allowWrites)
    {
        if (client == AgentClient.Codex)
        {
            string toml = AgentConnection.CodexConfiguration(executable, workspace, allowWrites);
            return File.Exists(configPath)
                ? (IsConfigured(client, configPath)
                    ? $"{configPath} enthält schon [mcp_servers.{AgentConnection.ServerName}]: diesen Abschnitt bis zur nächsten Zeile mit [ durch den folgenden ersetzen."
                    : $"Ans Ende von {configPath} anhängen (mit einer Leerzeile davor).", toml)
                : ($"{configPath} gibt es noch nicht: Datei mit diesem Inhalt anlegen.", toml);
        }

        bool includeType = client == AgentClient.ClaudeCode;
        string whole = UpdateJson(null, executable, workspace, allowWrites, includeType);
        if (!File.Exists(configPath))
            return ($"{configPath} gibt es noch nicht: Datei mit diesem Inhalt anlegen.", whole);

        JsonObject? root = null;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable or invalid: fall through to the generic instruction.
        }
        if (root is null)
            return ($"{configPath} ließ sich nicht als JSON lesen. Den Eintrag \"{AgentConnection.ServerName}\" aus diesem Beispiel unter \"mcpServers\" übernehmen.", whole);

        // The entry itself, indented as it sits inside "mcpServers".
        JsonNode entry = JsonNode.Parse(whole)!["mcpServers"]![AgentConnection.ServerName]!;
        string entryText = Indent($"\"{AgentConnection.ServerName}\": {entry.ToJsonString(WriteOptions)}", "    ");
        if (root["mcpServers"] is JsonObject servers)
        {
            string comma = servers.Count > 0 ? "," : string.Empty;
            string verb = servers.ContainsKey(AgentConnection.ServerName) ? "Den vorhandenen Eintrag \"freelancer-missions\" darin löschen und dann diesen" : "Diesen";
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
    public static string? AddServer(AgentClient client, string configPath, string executable, string workspace, bool allowWrites)
    {
        string? existing = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        string updated = client == AgentClient.Codex
            ? UpdateCodex(existing ?? string.Empty, executable, workspace, allowWrites)
            : UpdateJson(existing, executable, workspace, allowWrites, includeType: client == AgentClient.ClaudeCode);

        string directory = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(directory);
        string? backup = null;
        if (existing is not null)
        {
            backup = configPath + BackupSuffix;
            File.Copy(configPath, backup, overwrite: true);
        }
        string temporary = Path.Combine(directory, $".{Path.GetFileName(configPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, updated, new UTF8Encoding(false));
            File.Move(temporary, configPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        return backup;
    }

    private static string UpdateJson(string? existing, string executable, string workspace, bool allowWrites, bool includeType)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(existing)
                ? new JsonObject()
                : JsonNode.Parse(existing) as JsonObject ?? throw new InvalidDataException("Die Konfigurationsdatei enthält kein JSON-Objekt.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Die Konfigurationsdatei ist kein gültiges JSON ({exception.Message}). Sie wurde nicht verändert.", exception);
        }
        if (root["mcpServers"] is not JsonObject servers)
        {
            if (root["mcpServers"] is not null)
                throw new InvalidDataException("\"mcpServers\" in der Konfigurationsdatei ist kein JSON-Objekt. Die Datei wurde nicht verändert.");
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }
        var server = new JsonObject();
        if (includeType)
            server["type"] = "stdio";
        server["command"] = executable;
        server["args"] = new JsonArray(AgentConnection.Arguments(workspace, allowWrites).Select(argument => (JsonNode?)JsonValue.Create(argument)).ToArray());
        servers[AgentConnection.ServerName] = server;
        return root.ToJsonString(WriteOptions);
    }

    // Replaces the [mcp_servers.freelancer-missions] table (header up to the next table header) or appends it.
    private static string UpdateCodex(string existing, string executable, string workspace, bool allowWrites)
    {
        string block = AgentConnection.CodexConfiguration(executable, workspace, allowWrites).Replace("\r\n", "\n").TrimEnd('\n');
        string newline = existing.Contains("\r\n") ? "\r\n" : "\n";
        var lines = existing.Replace("\r\n", "\n").Split('\n').ToList();
        int start = lines.FindIndex(line => CodexSectionHeader().IsMatch(line));
        if (start >= 0)
        {
            int end = start + 1;
            while (end < lines.Count && !lines[end].TrimStart().StartsWith('['))
                end++;
            // Keep blank lines that separate the table from the next one.
            while (end > start + 1 && lines[end - 1].Trim().Length == 0)
                end--;
            lines.RemoveRange(start, end - start);
            lines.InsertRange(start, block.Split('\n'));
            return string.Join(newline, lines);
        }
        string trimmed = existing.TrimEnd('\r', '\n');
        string separator = trimmed.Length == 0 ? string.Empty : newline + newline;
        return trimmed + separator + block.Replace("\n", newline) + newline;
    }

    private static string Indent(string text, string prefix) =>
        string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(line => prefix + line));

    [GeneratedRegex(@"^\s*\[\s*mcp_servers\s*\.\s*(""freelancer-missions""|freelancer-missions)\s*\]\s*$", RegexOptions.Multiline)]
    private static partial Regex CodexSectionHeader();
}
