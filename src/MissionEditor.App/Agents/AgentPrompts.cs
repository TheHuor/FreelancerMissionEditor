using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MissionEditor.Core.Checks;

namespace MissionEditor.App;

/// <summary>
/// Ready-to-paste requests for an AI agent connected to the MCP server: they name the server, the file
/// (by its full path) and the tools to use, and ask for drafts instead of direct changes.
/// </summary>
public static partial class AgentPrompts
{
  private const string Opening = "Nutze den MCP-Server „" + AgentConnection.ServerName + "“ (Freelancer Mission Editor).";
  private const string Safety = "Ändere keine Datei ohne meine Zustimmung; Korrekturen nur als Entwurf (prepare_section_edit, vorher den SHA256 mit inspect_mission holen) und mit Vorher/Nachher zeigen.";

  /// <summary>The mission path relative to the workspace, or null when the file lies outside of it.</summary>
  public static string? RelativeToWorkspace (string missionPath, string workspace)
  {
    string relative = Path.GetRelativePath (Path.GetFullPath (workspace), Path.GetFullPath (missionPath));
    return relative == ".." || relative.StartsWith (".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted (relative)
        ? null
        : relative;
  }

  /// <summary>True when a server limited to the workspace (empty for no limit) can read the mission.</summary>
  public static bool IsReachable (string missionPath, string workspace) =>
      workspace.Length == 0 || RelativeToWorkspace (missionPath, workspace) is not null;

  /// <summary>
  /// The server setting that applies to the mission: of all folder limits registered at the clients (and the
  /// one the editor remembered; empty for no limit), one that lets the server read the mission; otherwise the
  /// first known one, or null when the server is not set up anywhere.
  /// </summary>
  public static (string? Workspace, bool ContainsMission) ChooseWorkspace (string? missionPath, IEnumerable<string> workspaces)
  {
    List<string> known = workspaces.Distinct (StringComparer.OrdinalIgnoreCase).ToList ();
    if (missionPath is not null)
    {
      // A folder that contains the mission is named before "no limit" (the empty string sorts last).
      string? containing = known.Where (workspace => IsReachable (missionPath, workspace))
          .OrderByDescending (workspace => workspace.Length).FirstOrDefault ();
      if (containing is not null)
        return (containing, true);
    }
    return (known.FirstOrDefault (), false);
  }

  /// <summary>Asks the agent to explain one finding and propose a fix.</summary>
  public static string ForFinding (string missionReference, Finding finding)
  {
    // A missing switch-off target never calls for a new trigger, only ActTrig/WatchTrigger/Save targets may be missing ones.
    if (finding.CheckId == "unknown-trigger" && !finding.Message.StartsWith ("Act_DeactTrig", StringComparison.Ordinal)
        && MissingTargetName (finding.Message) is { } missing)
      return ForMissingTrigger (missionReference, finding, missing);

    var text = new StringBuilder ();
    text.AppendLine (Opening);
    text.AppendLine ($"Datei: {missionReference}");
    text.AppendLine ($"Fund in Zeile {finding.LineNumber} ({SeverityName (finding.Severity)}, Prüfung {finding.CheckId}{SubjectPart (finding)}): {finding.Message}");
    if (finding.Hint is not null)
      text.AppendLine ($"Erklärung des Editors: {finding.Hint}");
    if (finding.RelatedLineNumber is int related)
      text.AppendLine ($"Bezugsstelle: Zeile {related}.");
    text.AppendLine ();
    text.AppendLine ("1. Lies die Stelle (read_mission_source um die Zeile, bei Triggern get_trigger_context).");
    text.AppendLine ("2. Erkläre mir in einfachen Worten, was dadurch im Spiel passiert.");
    text.AppendLine ("3. Schlag eine Korrektur vor und begründe sie; prüfe im Entwurf, dass der Fund verschwindet und kein neuer entsteht.");
    text.Append (Safety);
    return text.ToString ();
  }

  /// <summary>Asks the agent for an overview of all findings of a mission.</summary>
  public static string ForAllFindings (string missionReference)
  {
    var text = new StringBuilder ();
    text.AppendLine (Opening);
    text.AppendLine ($"Datei: {missionReference}");
    text.AppendLine ();
    text.AppendLine ("1. Prüfe die Mission mit check_mission (ab Warning, alle Seiten).");
    text.AppendLine ("2. Fasse die Funde nach Art zusammen und erkläre je Art, was im Spiel passiert und wie schwer es wiegt.");
    text.AppendLine ("3. Schlag eine Reihenfolge zum Beheben vor. Für die Fehler: konkrete Korrekturen mit Begründung.");
    text.Append (Safety);
    return text.ToString ();
  }

  /// <summary>Asks the agent to explain the mission's story and flow in plain language.</summary>
  public static string ForExplainMission (string missionReference)
  {
    var text = new StringBuilder ();
    text.AppendLine (Opening);
    text.AppendLine ($"Datei: {missionReference}");
    text.AppendLine ();
    text.AppendLine ("Erklär mir diese Mission, so wie ein Spieler sie erlebt.");
    text.AppendLine ("1. Verschaff dir einen Überblick: inspect_mission, list_triggers (alle Seiten) und list_sections für Objekte, NPCs, Dialoge und Ziele.");
    text.AppendLine ("2. Starte bei den Triggern mit InitState = ACTIVE und folge dem Ablauf mit get_trigger_context; lies unklare Stellen mit read_mission_source.");
    text.AppendLine ("3. Beschreibe die Mission in Abschnitten in Spielreihenfolge: Ausgangslage, Aufträge und Ziele, wer wann auftaucht, was den nächsten Abschnitt auslöst, Erfolg und Scheitern.");
    text.AppendLine ("4. Nenne zu jedem Abschnitt die tragenden Trigger mit Zeilennummer, damit ich sie im Editor finde.");
    text.AppendLine ("5. Fasse am Ende den Ablauf als kurze Kette zusammen (Trigger A → B → C) und nenne Nebenstränge und Speicherpunkte.");
    text.Append ("Nur lesen, nichts ändern. Was sich aus dem Skript nicht sicher ergibt (z. B. Dialoginhalte hinter IDs), als Annahme kennzeichnen.");
    return text.ToString ();
  }

  /// <summary>Asks the agent to look for gaps and breaks in the mission flow beyond the editor's checks.</summary>
  public static string ForGapsAndBreaks (string missionReference)
  {
    var text = new StringBuilder ();
    text.AppendLine (Opening);
    text.AppendLine ($"Datei: {missionReference}");
    text.AppendLine ();
    text.AppendLine ("Prüfe die Mission auf Lücken und Brüche im Ablauf – über die Prüfungen des Editors hinaus.");
    text.AppendLine ("1. check_mission (ab Info) als Ausgangspunkt – der Editor findet nie aktivierte Trigger, unbekannte oder nie gespawnte Objekte und ein fehlendes Erfolgsende schon selbst; bewerte diese Funde und such darüber hinaus, indem du den Ablauf ab den Triggern mit InitState = ACTIVE mit list_triggers und get_trigger_context nachverfolgst.");
    text.AppendLine ("2. Such gezielt nach:");
    text.AppendLine ("   - Triggern, die nie aktiviert werden (nicht ACTIVE und von keinem Act_ActTrig erreicht);");
    text.AppendLine ("   - Sackgassen: Abschnitte, nach denen nichts mehr ausgelöst wird und die Mission weder gelingt noch scheitert;");
    text.AppendLine ("   - Bedingungen, die nie eintreten können (z. B. Cnd_Destroyed oder Cnd_SpaceEnter für Objekte, die nie gespawnt werden);");
    text.AppendLine ("   - Triggern, die deaktiviert werden, bevor sie auslösen konnten, und Wettläufen zwischen parallel aktiven Triggern;");
    text.AppendLine ("   - Zielen und Wegpunkten, die gesetzt, aber nie erfüllt oder entfernt werden, und Objekten oder NPCs, die gespawnt, aber nie gebraucht werden;");
    text.AppendLine ("   - fehlenden Pfaden für Scheitern (Spieler stirbt, wichtiger NPC stirbt) und Speicherpunkten an Stellen, von denen aus die Mission nicht weitergeht.");
    text.AppendLine ("3. Liste die Befunde nach Schwere (blockiert die Mission / stört / Schönheitsfehler), je mit Zeile, Trigger, was im Spiel passiert und ob es belegt oder nur vermutet ist.");
    text.AppendLine ("4. Schlag zu den blockierenden Befunden eine Korrektur vor.");
    text.Append (Safety);
    return text.ToString ();
  }

  /// <summary>Asks the agent to write a new mission from a description.</summary>
  public static string ForNewMission (string description, string? templateReference, string targetReference, bool exportAllowed)
  {
    var text = new StringBuilder ();
    text.AppendLine (Opening);
    text.AppendLine ("Schreib eine neue Freelancer-Mission als INI-Text und prüfe sie mit dem Server.");
    text.AppendLine ();
    text.AppendLine ("Beschreibung:");
    text.AppendLine (description.Trim ());
    text.AppendLine ();
    text.AppendLine ("Vorgehen:");
    text.AppendLine ("1. get_mission_vocabulary für die Bedingungen und Aktionen, die das Spiel kennt.");
    if (templateReference is not null)
      text.AppendLine ($"2. {templateReference} als Vorlage für Aufbau und Stil ansehen (list_sections, read_mission_source, get_trigger_context).");
    else
      text.AppendLine ("2. Eine vorhandene Mission als Vorlage für Aufbau und Stil ansehen (list_sections, read_mission_source); frag mich nach dem Pfad, falls nötig.");
    text.AppendLine ("3. Regeln: [Trigger] beginnt mit nickname; pro Trigger genau eine Cnd_-Zeile; der Starttrigger hat InitState = ACTIVE; jeder Act_ActTrig zeigt auf einen vorhandenen Trigger.");
    text.AppendLine ("4. Mit prepare_new_mission prüfen und Funde beheben, bis keine Fehler mehr bleiben.");
    text.AppendLine (exportAllowed
        ? $"5. Mir den Entwurf zeigen und erst nach meiner Zustimmung als {targetReference} exportieren (export_mission_draft)."
        : "5. Mir den fertigen Text zeigen (Export ist am Server nicht freigeschaltet).");
    text.Append ("Nicknames für Systeme, Basen, Schiffe, Loadouts und Fraktionen nicht erfinden: mit list_game_nicknames nachschlagen oder aus der Vorlage übernehmen; was sich nicht belegen lässt, als Annahme kennzeichnen.");
    return text.ToString ();
  }

  private static string ForMissingTrigger (string missionReference, Finding finding, string missing)
  {
    var text = new StringBuilder ();
    text.AppendLine (Opening);
    text.AppendLine ($"Datei: {missionReference}");
    text.AppendLine ($"In Zeile {finding.LineNumber}{SubjectPart (finding)} wird der Trigger „{missing}“ verwendet, den es nicht gibt: {finding.Message}");
    text.AppendLine ();
    text.AppendLine ($"1. suggest_missing_trigger für „{missing}“ aufrufen; die aufrufende Stelle und die ähnlichen Trigger mit get_trigger_context ansehen.");
    text.AppendLine ("2. Begründe, ob es ein Tippfehler ist (dann den Verweis korrigieren) oder ob ein Trigger fehlt (dann einen passenden Trigger im Stil der Nachbarn entwerfen). Namensähnlichkeit ist kein Beweis – kennzeichne Annahmen.");
    text.AppendLine ("3. Einen neuen Trigger legst du an, indem du die Sektion des aufrufenden Triggers per prepare_section_edit durch sich selbst plus den neuen [Trigger] ersetzt (list_sections liefert den Index).");
    text.Append (Safety);
    return text.ToString ();
  }

  private static string SubjectPart (Finding finding) => finding.Subject is null ? string.Empty : $", Trigger/Objekt {finding.Subject}";

  private static string SeverityName (FindingSeverity severity) => severity switch
  {
    FindingSeverity.Error => "Fehler",
    FindingSeverity.Warning => "Warnung",
    _ => "Hinweis",
  };

  private static string? MissingTargetName (string message) =>
      MissingTargetPattern ().Match (message) is { Success: true } match ? match.Groups[1].Value : null;

  [GeneratedRegex ("unbekannten Trigger '([^']+)'")]
  private static partial Regex MissingTargetPattern ();
}
