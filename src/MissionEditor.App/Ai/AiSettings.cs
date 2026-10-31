using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MissionEditor.App.Ai;

/// <summary>How "Frag KI" reaches a model.</summary>
public enum AiProvider
{
    /// <summary>Anthropic API with the user's own API key (billed per request).</summary>
    AnthropicApi,

    /// <summary>Installed Claude Code command line (<c>claude -p</c>), using the user's subscription.</summary>
    ClaudeCli,

    /// <summary>Installed Codex command line (<c>codex exec</c>), using the user's subscription.</summary>
    CodexCli,
}

/// <summary>
/// Settings for "Frag KI", stored per Windows user. The API key is encrypted with DPAPI (only this
/// Windows account can decrypt it) and never written in plain text.
/// </summary>
public sealed class AiSettings
{
    /// <summary>The model used by default.</summary>
    public const string DefaultModel = "claude-opus-5-5";

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreelancerMissionEditor", "ai-settings.json");

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FreelancerMissionEditor.AiSettings");

    /// <summary>The chosen provider.</summary>
    public AiProvider Provider { get; set; } = AiProvider.AnthropicApi;

    /// <summary>The model for the Anthropic API.</summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>Path to the command-line agent; empty means "find it on the PATH".</summary>
    public string CliPath { get; set; } = string.Empty;

    /// <summary>The stored API key in plain text (decrypted in memory only), or empty.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The key to use: the stored one, else the ANTHROPIC_API_KEY environment variable.</summary>
    public string? EffectiveApiKey => ApiKey.Length > 0 ? ApiKey : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    /// <summary>Loads the settings; missing or unreadable settings give the defaults.</summary>
    public static AiSettings Load(string? path = null)
    {
        var settings = new AiSettings();
        try
        {
            string file = path ?? SettingsPath;
            if (!File.Exists(file))
                return settings;
            JsonNode? node = JsonNode.Parse(File.ReadAllText(file));
            if (Enum.TryParse(node?["provider"]?.GetValue<string>(), out AiProvider provider))
                settings.Provider = provider;
            settings.Model = node?["model"]?.GetValue<string>() is { Length: > 0 } model ? model : DefaultModel;
            settings.CliPath = node?["cliPath"]?.GetValue<string>() ?? string.Empty;
            if (node?["apiKeyProtected"]?.GetValue<string>() is { Length: > 0 } protectedKey)
                settings.ApiKey = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedKey), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or FormatException or CryptographicException)
        {
            // Broken settings fall back to the defaults; the key has to be entered again.
        }
        return settings;
    }

    /// <summary>Saves the settings; the API key is stored DPAPI-encrypted.</summary>
    public void Save(string? path = null)
    {
        string file = path ?? SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var node = new JsonObject
        {
            ["provider"] = Provider.ToString(),
            ["model"] = Model,
            ["cliPath"] = CliPath,
        };
        if (ApiKey.Length > 0)
            node["apiKeyProtected"] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(ApiKey), Entropy, DataProtectionScope.CurrentUser));
        File.WriteAllText(file, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
