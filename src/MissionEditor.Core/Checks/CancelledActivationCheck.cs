using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Act_ActTrig = X followed by Act_DeactTrig = X in the same trigger: Act_ActTrig only queues the activation, and
/// Act_DeactTrig removes queued activations of X as well (fl_decomp DeactivateMissionTrigger) – X never becomes
/// active. The reverse order (switch off, then on again) restarts X and is not reported.
/// </summary>
public sealed class CancelledActivationCheck : IMissionCheck
{
  /// <inheritdoc/>
  public string Id => "cancelled-activation";

  /// <inheritdoc/>
  public string Title => "Aufgehobene Aktivierung";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    foreach (Trigger trigger in script.EffectiveTriggers)
    {
      var reported = new HashSet<uint> ();
      foreach (TriggerReference activate in trigger.References.Where (reference => reference.Kind == TriggerReferenceKind.Activate))
      {
        TriggerReference? deactivate = trigger.References.FirstOrDefault (reference => reference.Kind == TriggerReferenceKind.Deactivate
            && reference.TargetId == activate.TargetId && reference.Line.Number > activate.Line.Number);
        if (deactivate is null || !reported.Add (activate.TargetId))
          continue;
        yield return new Finding (FindingSeverity.Warning, Id, deactivate.Line.Number, trigger.Nickname,
            $"'{trigger.Nickname}' aktiviert '{activate.TargetName}' (Zeile {activate.Line.Number}) und schaltet ihn danach wieder ab – '{activate.TargetName}' wird nie aktiv.",
            "Act_ActTrig merkt die Aktivierung nur vor; Act_DeactTrig löscht auch vorgemerkte Aktivierungen desselben Triggers. In dieser "
            + "Reihenfolge heben sich die Zeilen auf. Abhilfe: eine der beiden Zeilen entfernen; zum Neustarten eines Triggers erst "
            + "Act_DeactTrig, dann Act_ActTrig schreiben.",
            activate.Line.Number);
      }
    }
  }
}
