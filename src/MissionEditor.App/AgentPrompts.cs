using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MissionEditor.Core.Checks;

namespace MissionEditor.App;

/// <summary>
/// Ready-to-paste requests for an AI agent connected to the MCP server: they name the server, the file
/// (relative to the shared workspace) and the tools to use, and ask for drafts instead of direct changes.
/// </summary>
public static partial class AgentPrompts
{
    private const string Opening = "Nutze den MCP-Server „" + AgentConnection.ServerName + "“ (Freelancer Mission Editor).";
    private const string Safety = "Ändere keine Datei ohne meine Zustimmung; Korrekturen nur als Entwurf (prepare_section_edit, vorher den SHA256 mit inspect_mission holen) und mit Vorher/Nachher zeigen.";

    /// <summary>The mission path relative to the workspace, or null when the file lies outside of it.</summary>
    public static string? RelativeToWorkspace(string missionPath, string workspace)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(workspace), Path.GetFullPath(missionPath));
        return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? null
            : relative;
    }

    /// <summary>Asks the agent to explain one finding and propose a fix.</summary>
    public static string ForFinding(string missionReference, Finding finding)
    {
        if (finding.CheckId == "unknown-trigger" && MissingTargetName(finding.Message) is { } missing)
            return ForMissingTrigger(missionReference, finding, missing);

        var text = new StringBuilder();
        text.AppendLine(Opening);
        text.AppendLine($"Datei: {missionReference}");
        text.AppendLine($"Fund in Zeile {finding.LineNumber} ({SeverityName(finding.Severity)}, Prüfung {finding.CheckId}{SubjectPart(finding)}): {finding.Message}");
        if (finding.Hint is not null)
            text.AppendLine($"Erklärung des Editors: {finding.Hint}");
        if (finding.RelatedLineNumber is int related)
            text.AppendLine($"Bezugsstelle: Zeile {related}.");
        text.AppendLine();
        text.AppendLine("1. Lies die Stelle (read_mission_source um die Zeile, bei Triggern get_trigger_context).");
        text.AppendLine("2. Erkläre mir in einfachen Worten, was dadurch im Spiel passiert.");
        text.AppendLine("3. Schlag eine Korrektur vor und begründe sie; prüfe im Entwurf, dass der Fund verschwindet und kein neuer entsteht.");
        text.Append(Safety);
        return text.ToString();
    }

    /// <summary>Asks the agent for an overview of all findings of a mission.</summary>
    public static string ForAllFindings(string missionReference)
    {
        var text = new StringBuilder();
        text.AppendLine(Opening);
        text.AppendLine($"Datei: {missionReference}");
        text.AppendLine();
        text.AppendLine("1. Prüfe die Mission mit check_mission (ab Warning, alle Seiten).");
        text.AppendLine("2. Fasse die Funde nach Art zusammen und erkläre je Art, was im Spiel passiert und wie schwer es wiegt.");
        text.AppendLine("3. Schlag eine Reihenfolge zum Beheben vor. Für die Fehler: konkrete Korrekturen mit Begründung.");
        text.Append(Safety);
        return text.ToString();
    }

    /// <summary>Asks the agent to write a new mission from a description.</summary>
    public static string ForNewMission(string description, string? templateReference, string targetReference, bool exportAllowed)
    {
        var text = new StringBuilder();
        text.AppendLine(Opening);
        text.AppendLine("Schreib eine neue Freelancer-Mission als INI-Text und prüfe sie mit dem Server.");
        text.AppendLine();
        text.AppendLine("Beschreibung:");
        text.AppendLine(description.Trim());
        text.AppendLine();
        text.AppendLine("Vorgehen:");
        text.AppendLine("1. get_mission_vocabulary für die Bedingungen und Aktionen, die das Spiel kennt.");
        if (templateReference is not null)
            text.AppendLine($"2. {templateReference} als Vorlage für Aufbau und Stil ansehen (list_sections, read_mission_source, get_trigger_context).");
        else
            text.AppendLine("2. Eine vorhandene Mission im Workspace als Vorlage für Aufbau und Stil ansehen (list_sections, read_mission_source).");
        text.AppendLine("3. Regeln: [Trigger] beginnt mit nickname; pro Trigger genau eine Cnd_-Zeile; der Starttrigger hat InitState = ACTIVE; jeder Act_ActTrig zeigt auf einen vorhandenen Trigger.");
        text.AppendLine("4. Mit prepare_new_mission prüfen und Funde beheben, bis keine Fehler mehr bleiben.");
        text.AppendLine(exportAllowed
            ? $"5. Mir den Entwurf zeigen und erst nach meiner Zustimmung als {targetReference} exportieren (export_mission_draft)."
            : "5. Mir den fertigen Text zeigen (Export ist am Server nicht freigeschaltet).");
        text.Append("Nicknames für Systeme, Basen, Schiffe, Loadouts und Fraktionen nicht erfinden: aus der Vorlage übernehmen oder als Annahme kennzeichnen.");
        return text.ToString();
    }

    private static string ForMissingTrigger(string missionReference, Finding finding, string missing)
    {
        var text = new StringBuilder();
        text.AppendLine(Opening);
        text.AppendLine($"Datei: {missionReference}");
        text.AppendLine($"In Zeile {finding.LineNumber}{SubjectPart(finding)} wird der Trigger „{missing}“ verwendet, den es nicht gibt: {finding.Message}");
        text.AppendLine();
        text.AppendLine($"1. suggest_missing_trigger für „{missing}“ aufrufen; die aufrufende Stelle und die ähnlichen Trigger mit get_trigger_context ansehen.");
        text.AppendLine("2. Begründe, ob es ein Tippfehler ist (dann den Verweis korrigieren) oder ob ein Trigger fehlt (dann einen passenden Trigger im Stil der Nachbarn entwerfen). Namensähnlichkeit ist kein Beweis – kennzeichne Annahmen.");
        text.AppendLine("3. Einen neuen Trigger legst du an, indem du die Sektion des aufrufenden Triggers per prepare_section_edit durch sich selbst plus den neuen [Trigger] ersetzt (list_sections liefert den Index).");
        text.Append(Safety);
        return text.ToString();
    }

    private static string SubjectPart(Finding finding) => finding.Subject is null ? string.Empty : $", Trigger/Objekt {finding.Subject}";

    private static string SeverityName(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Error => "Fehler",
        FindingSeverity.Warning => "Warnung",
        _ => "Hinweis",
    };

    private static string? MissingTargetName(string message) =>
        MissingTargetPattern().Match(message) is { Success: true } match ? match.Groups[1].Value : null;

    [GeneratedRegex("unbekannten Trigger '([^']+)'")]
    private static partial Regex MissingTargetPattern();
}
