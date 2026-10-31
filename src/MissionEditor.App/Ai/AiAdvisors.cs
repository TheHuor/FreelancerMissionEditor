using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace MissionEditor.App.Ai;

/// <summary>Sends an <see cref="AdviceRequest"/> to a model and returns the raw answer text (JSON).</summary>
public interface IAiAdvisor
{
    /// <summary>A short name for the status line, e.g. "Claude (API)".</summary>
    string DisplayName { get; }

    /// <summary>Asks the model.</summary>
    /// <exception cref="InvalidOperationException">The provider is not usable (missing key or program) or the request failed.</exception>
    Task<string> AskAsync(AdviceRequest request, CancellationToken cancellation);
}

/// <summary>Creates the advisor for the configured provider.</summary>
public static class AiAdvisorFactory
{
    /// <summary>The advisor for the settings.</summary>
    public static IAiAdvisor Create(AiSettings settings) => settings.Provider switch
    {
        AiProvider.AnthropicApi => new AnthropicApiAdvisor(settings),
        AiProvider.ClaudeCli => new CliAdvisor(settings, AiProvider.ClaudeCli),
        _ => new CliAdvisor(settings, AiProvider.CodexCli),
    };
}

/// <summary>Anthropic API via the official SDK, with structured output so the answer is valid JSON.</summary>
public sealed class AnthropicApiAdvisor(AiSettings settings) : IAiAdvisor
{
    /// <inheritdoc/>
    public string DisplayName => $"Claude über die API ({settings.Model})";

    /// <inheritdoc/>
    public async Task<string> AskAsync(AdviceRequest request, CancellationToken cancellation)
    {
        if (settings.EffectiveApiKey is not { Length: > 0 } apiKey)
            throw new InvalidOperationException("Kein API-Schlüssel hinterlegt. Unter „KI-Einstellungen“ eintragen.");

        AnthropicClient client = new() { ApiKey = apiKey };
        using JsonDocument schema = JsonDocument.Parse(AiAdvice.AnswerSchemaJson);
        var parameters = new MessageCreateParams
        {
            Model = settings.Model,
            MaxTokens = 16000,
            // A refused request is re-served by Anthropic's recommended fallback model instead of failing.
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            System = request.System,
            Messages = [new BetaUserMessageParam { Content = request.User }],
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.Medium,
                Format = new BetaJsonOutputFormat
                {
                    Schema = schema.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()),
                },
            },
        };

        BetaMessage response;
        try
        {
            response = await client.Beta.Messages.Create(parameters, cancellation);
        }
        catch (AnthropicUnauthorizedException)
        {
            throw new InvalidOperationException("Der API-Schlüssel wurde abgelehnt. Unter „KI-Einstellungen“ prüfen.");
        }
        catch (AnthropicRateLimitException)
        {
            throw new InvalidOperationException("Zu viele Anfragen (Rate-Limit). Bitte kurz warten und erneut versuchen.");
        }
        catch (AnthropicApiException exception)
        {
            throw new InvalidOperationException($"Die API meldet einen Fehler: {exception.Message}");
        }

        if (response.StopReason == "refusal")
            throw new InvalidOperationException("Das Modell hat die Anfrage abgelehnt.");
        string text = string.Concat(response.Content.Select(block => block.TryPickText(out BetaTextBlock? textBlock) ? textBlock.Text : string.Empty));
        if (response.StopReason == "max_tokens")
            throw new InvalidOperationException("Die Antwort wurde abgeschnitten (Längenlimit).");
        return text;
    }
}

/// <summary>
/// A locally installed agent command line (Claude Code or Codex) in non-interactive mode. The prompt goes
/// through stdin; the agent runs read-only and is asked to answer with the JSON object only.
/// </summary>
public sealed class CliAdvisor(AiSettings settings, AiProvider provider) : IAiAdvisor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <inheritdoc/>
    public string DisplayName => provider == AiProvider.ClaudeCli ? "Claude Code (lokal)" : "Codex (lokal)";

    /// <summary>The program to start, or null when it cannot be found.</summary>
    public string? FindProgram()
    {
        if (settings.CliPath.Length > 0)
            return File.Exists(settings.CliPath) ? settings.CliPath : null;
        return ProgramLocator.Find(provider == AiProvider.ClaudeCli ? "claude" : "codex");
    }

    /// <inheritdoc/>
    public async Task<string> AskAsync(AdviceRequest request, CancellationToken cancellation)
    {
        string program = FindProgram()
            ?? throw new InvalidOperationException($"{DisplayName} wurde nicht gefunden. Installieren oder den Pfad unter „KI-Einstellungen“ angeben.");
        string prompt = request.System + "\n\n" + request.User
            + "\n\nGib ausschließlich ein JSON-Objekt nach diesem Schema aus, ohne Text davor oder danach und ohne Dateien zu ändern:\n"
            + AiAdvice.AnswerSchemaJson;
        string? answerFile = provider == AiProvider.CodexCli ? Path.Combine(Path.GetTempPath(), $"mission-editor-{Guid.NewGuid():N}.txt") : null;
        string arguments = provider == AiProvider.ClaudeCli
            ? "-p --output-format json"
            : $"exec --skip-git-repo-check --sandbox read-only --output-last-message \"{answerFile}\" -";

        var start = new ProcessStartInfo(program, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetTempPath(),
        };
        // The agent (and e.g. Node.js behind an npm-installed claude.cmd) needs the current PATH, not the
        // one this process was started with.
        start.Environment["PATH"] = ProgramLocator.CurrentPath();
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{DisplayName} ließ sich nicht starten.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.StandardInput.WriteAsync(prompt.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string stdout = await output;
            string stderr = await errors;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{DisplayName} endete mit Fehler {process.ExitCode}: {LastLine(stderr.Length > 0 ? stderr : stdout)}");
            if (answerFile is not null)
                return File.Exists(answerFile) ? await File.ReadAllTextAsync(answerFile, timeout.Token) : stdout;
            // claude -p --output-format json wraps the answer: {"result": "...", ...}
            using JsonDocument wrapper = JsonDocument.Parse(stdout);
            return wrapper.RootElement.TryGetProperty("result", out JsonElement result) ? result.GetString() ?? string.Empty : stdout;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException($"{DisplayName} hat nicht innerhalb von {Timeout.TotalMinutes:0} Minuten geantwortet.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{DisplayName} lieferte eine unerwartete Ausgabe ({exception.Message}).");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            if (answerFile is not null && File.Exists(answerFile))
                File.Delete(answerFile);
        }
    }

    private static string LastLine(string text) => text.Trim().Split('\n').LastOrDefault()?.Trim() ?? string.Empty;
}
