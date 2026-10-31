using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// The shape of [Trigger] sections as content.dll reads them: a nickname is required, values before
/// it are skipped, exactly one Cnd_* is expected (none crashes on activation, only the last of
/// several counts) and misspelt Cnd_/Act_ keys are silently ignored.
/// </summary>
public sealed class TriggerStructureCheck : IMissionCheck
{
  /// <inheritdoc/>
  public string Id => "trigger-structure";

  /// <inheritdoc/>
  public string Title => "Aufbau der Trigger";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    foreach (IniSection section in script.TriggerSectionsWithoutNickname)
    {
      yield return new Finding (FindingSeverity.Error, Id, section.FirstLineNumber, null,
          "[Trigger] ohne nickname – die Engine überspringt die ganze Sektion.",
          "Ohne nickname legt die Engine keinen Trigger an; Bedingung und Aktionen der Sektion gehen verloren. "
          + "Abhilfe: 'nickname = <name>' als erste Zeile der Sektion eintragen.");
    }

    // Shadowed duplicates never reach the engine; the duplicate check reports them.
    foreach (Trigger trigger in script.EffectiveTriggers)
    {
      if (trigger.Conditions.Count == 0)
      {
        yield return new Finding (FindingSeverity.Error, Id, trigger.NicknameLine.Number, trigger.Nickname,
            $"Trigger '{trigger.Nickname}' hat keine Cnd_*-Bedingung – das Spiel stürzt ab, sobald er aktiviert wird.",
            "Die Engine prüft einen aktiven Trigger über seine Bedingung; ohne Bedingung greift sie ins Leere. "
            + "Abhilfe: eine Bedingung ergänzen, z. B. 'Cnd_True = no_params' (löst sofort aus) oder 'Cnd_Timer = 1'.");
      }
      else if (trigger.Conditions.Count > 1)
      {
        IniLine last = trigger.Conditions[^1];
        yield return new Finding (FindingSeverity.Warning, Id, trigger.Conditions[0].Number, trigger.Nickname,
            $"Trigger '{trigger.Nickname}' hat {trigger.Conditions.Count} Bedingungen; nur die letzte gilt ({last.Key} = {last.RawValue}, Zeile {last.Number}).",
            "Jede Cnd_*-Zeile ersetzt die vorige, es gibt kein UND. Abhilfe: nur eine Bedingung behalten; "
            + "für 'erst A, dann B' zwei Trigger hintereinander schalten (der erste aktiviert per Act_ActTrig den zweiten).");
      }

      foreach (IgnoredEntry ignored in trigger.IgnoredEntries)
      {
        string key = ignored.Line.Key ?? string.Empty;
        if (ignored.Reason == IgnoreReason.BeforeNickname)
        {
          yield return new Finding (FindingSeverity.Warning, Id, ignored.Line.Number, trigger.Nickname,
              $"'{key}' steht vor nickname – die Engine ignoriert die Zeile.",
              "Die Engine liest eine [Trigger]-Sektion erst ab der Zeile 'nickname'; alles davor wird übersprungen. "
              + EffectOfLostLine (key)
              + " Abhilfe: 'nickname' als erste Zeile der Sektion schreiben, die übrigen Zeilen darunter.");
          continue;
        }
        IReadOnlyList<string> suggestions = NameSimilarity.FindClosest (key,
            EngineVocabulary.LooksLikeCondition (key) ? EngineVocabulary.Conditions : EngineVocabulary.Actions);
        yield return new Finding (FindingSeverity.Warning, Id, ignored.Line.Number, trigger.Nickname,
            $"Unbekannter Schlüssel '{key}' – die Engine ignoriert die Zeile (Tippfehler?).",
            "Die Engine kennt nur eine feste Liste von Cnd_*/Act_*-Namen (Groß-/Kleinschreibung egal); andere werden still übergangen. "
            + (suggestions.Count > 0 ? $"Gemeint vielleicht {string.Join (" oder ", suggestions.Select (name => $"'{name}'"))}?" : "Schreibweise prüfen."));
      }
    }
  }

  // What the game loses when a line before the nickname is skipped.
  private static string EffectOfLostLine (string key)
  {
    if (key.Equals ("repeatable", StringComparison.OrdinalIgnoreCase))
      return "Folge: der Trigger ist nicht wiederholbar und löst nur einmal aus.";
    if (key.Equals ("InitState", StringComparison.OrdinalIgnoreCase))
      return "Folge: der Trigger startet nicht im angegebenen Zustand (sondern INACTIVE).";
    if (EngineVocabulary.LooksLikeCondition (key))
      return "Folge: diese Bedingung fehlt dem Trigger.";
    if (EngineVocabulary.LooksLikeAction (key))
      return "Folge: diese Aktion wird nie ausgeführt.";
    return "Folge: die Angabe hat keine Wirkung.";
  }
}
