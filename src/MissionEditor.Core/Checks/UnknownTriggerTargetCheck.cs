using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// References to trigger nicknames that no [Trigger] defines. The engine ignores such an
/// Act_ActTrig/Act_DeactTrig silently; a Cnd_WatchTrigger never fires and an Act_Save resumes
/// into nothing. A similar existing nickname is suggested.
/// </summary>
public sealed class UnknownTriggerTargetCheck : IMissionCheck
{
  /// <inheritdoc/>
  public string Id => "unknown-trigger";

  /// <inheritdoc/>
  public string Title => "Unbekanntes Trigger-Ziel";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    string[] names = script.EffectiveTriggers.Select (trigger => trigger.Nickname).ToArray ();
    foreach (Trigger trigger in script.Triggers)
    {
      foreach (TriggerReference reference in trigger.References)
      {
        if (reference.Target is not null)
          continue;
        FindingSeverity severity = trigger.IsShadowed ? FindingSeverity.Info
            : reference.Kind == TriggerReferenceKind.Deactivate ? FindingSeverity.Warning
            : FindingSeverity.Error;
        string effect = reference.Kind switch
        {
          TriggerReferenceKind.Activate => "die Aktivierung passiert nie",
          TriggerReferenceKind.Deactivate => "die Deaktivierung hat keine Wirkung",
          TriggerReferenceKind.Watch => "die Bedingung wird nie erfüllt",
          _ => "der Spielstand setzt an keinem Trigger fort",
        };
        string message = $"{TriggerReference.KeyOf (reference.Kind)} auf unbekannten Trigger '{reference.TargetName}' – {effect}.";
        IReadOnlyList<string> suggestions = NameSimilarity.FindClosest (reference.TargetName, names);
        if (suggestions.Count > 0)
          message += $" Gemeint vielleicht {string.Join (" oder ", suggestions.Select (name => $"'{name}'"))}?";
        // A name of another section type: the line means something the trigger actions cannot do.
        ScriptObject? other = script.Objects.FirstOrDefault (item => item.Nickname.Equals (reference.TargetName, StringComparison.OrdinalIgnoreCase));
        if (other is not null)
          message += $" '{reference.TargetName}' ist eine [{ScriptObject.SectionNameOf (other.Kind)}] (Zeile {other.LineNumber}), kein Trigger.";
        // A misspelt Act_DeactTrig is not harmless when the trigger it meant keeps running and spawns or repeats. Which
        // of several similar names was meant is the author's call, so all running ones are named.
        string[] keepRunning = reference.Kind != TriggerReferenceKind.Deactivate ? []
            : suggestions.Select (name => script.FindTrigger (name)).OfType<Trigger> ()
                .Where (candidate => candidate.Repeatable || candidate.Actions.Any (action => action.Key!.StartsWith ("Act_Spawn", StringComparison.OrdinalIgnoreCase)))
                .Select (candidate => $"'{candidate.Nickname}'").ToArray ();
        bool meantKeepsRunning = keepRunning.Length > 0;
        if (meantKeepsRunning && !trigger.IsShadowed)
        {
          severity = FindingSeverity.Error;
          message += keepRunning.Length == 1
              ? $" Ist {keepRunning[0]} gemeint, bleibt er dadurch aktiv und spawnt bzw. wiederholt weiter."
              : $" Welcher auch gemeint ist ({string.Join (", ", keepRunning)}), er bleibt dadurch aktiv und spawnt bzw. wiederholt weiter.";
        }
        if (trigger.IsShadowed)
          message += " (Der Trigger ist doppelt definiert und wird ohnehin ignoriert.)";
        string hint = reference.Kind switch
        {
          TriggerReferenceKind.Activate => "Act_ActTrig auf einen Namen ohne [Trigger] ist im Spiel ein stiller No-op – es gibt keine Fehlermeldung, der Ablauf geht dort einfach nicht weiter.",
          TriggerReferenceKind.Deactivate when other?.Kind == ScriptObjectKind.ObjList =>
            "Act_DeactTrig schaltet nur Trigger ab; eine laufende Befehlsliste beendet es nicht. Dafür dem Schiff per Act_GiveObjList eine andere [ObjList] geben.",
          TriggerReferenceKind.Deactivate when other?.Kind == ScriptObjectKind.RandomEncounter =>
            "Act_DeactTrig schaltet nur Trigger ab; einen Zufallskampf ([MsnRandEnc]) schaltet Act_DisableEnc ab.",
          TriggerReferenceKind.Deactivate when meantKeepsRunning =>
            "Act_DeactTrig auf einen Namen ohne [Trigger] tut nichts – der vermutlich gemeinte Trigger läuft weiter, mit allen Spawns bzw. Wiederholungen.",
          TriggerReferenceKind.Deactivate => "Act_DeactTrig auf einen Namen ohne [Trigger] ist harmlos, aber meist ein Tippfehler oder ein Überbleibsel eines gelöschten Triggers.",
          TriggerReferenceKind.Watch => "Cnd_WatchTrigger wartet dann für immer; der Trigger löst nie aus.",
          _ => "Beim Laden dieses Spielstands wird kein Trigger aktiviert – die Mission steht still.",
        };
        hint += " Trigger-Namen vergleicht die Engine ohne Groß-/Kleinschreibung. Abhilfe: Namen korrigieren oder den fehlenden [Trigger] anlegen"
            + (reference.Kind == TriggerReferenceKind.Deactivate ? " bzw. die Zeile entfernen." : ".");
        yield return new Finding (severity, Id, reference.Line.Number, trigger.Nickname, message, hint);
      }
    }
  }
}
