using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Triggers that never become active: not InitState = ACTIVE and not reached by Act_ActTrig or Act_Save
/// from a trigger that becomes active itself. Their conditions are never tested and their actions never run.
/// </summary>
public sealed class UnreachableTriggerCheck : IMissionCheck
{
  /// <inheritdoc/>
  public string Id => "unreachable-trigger";

  /// <inheritdoc/>
  public string Title => "Nie aktivierter Trigger";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    MissionFlow flow = MissionFlow.Of (script);
    Trigger[] effective = script.EffectiveTriggers.ToArray ();
    if (effective.Length == 0)
      yield break;
    if (flow.Roots.Count == 0)
    {
      Trigger first = effective[0];
      yield return new Finding (FindingSeverity.Warning, Id, first.NicknameLine.Number, null,
          "Kein Trigger hat InitState = ACTIVE – beim Start der Mission läuft nichts an.",
          "Die Engine prüft nur aktive Trigger; zu Beginn sind das die mit InitState = ACTIVE. Ohne sie startet die Mission nur über "
          + "einen Spielstand mit Act_Save-Trigger. Abhilfe: beim Starttrigger 'InitState = ACTIVE' direkt unter nickname eintragen.");
      yield break;
    }

    foreach (Trigger trigger in effective)
    {
      // InitState = COMPLETE marks a trigger as done on purpose.
      if (flow.Reachable.Contains (trigger) || trigger.InitialState == TriggerState.Complete)
        continue;
      string[] activators = effective
          .Where (source => source.References.Any (reference => reference.Target == trigger
              && reference.Kind is TriggerReferenceKind.Activate or TriggerReferenceKind.Save))
          .Select (source => source.Nickname).Distinct ().ToArray ();
      string message = activators.Length == 0
          ? $"Trigger '{trigger.Nickname}' wird nie aktiviert – kein Act_ActTrig oder Act_Save zeigt auf ihn, und er ist nicht InitState = ACTIVE."
          : $"Trigger '{trigger.Nickname}' wird nur von Triggern aktiviert, die selbst nie aktiv werden ({string.Join (", ", activators.Take (5).Select (name => $"'{name}'"))}{(activators.Length > 5 ? ", …" : string.Empty)}).";

      // A dead trigger on its own is a leftover. It breaks the flow when a running trigger waits for it:
      // Cnd_WatchTrigger for ACTIVE or COMPLETE is then never met.
      TriggerReference? waiting = script.EffectiveTriggers
          .Where (flow.Reachable.Contains)
          .SelectMany (watcher => watcher.References)
          .FirstOrDefault (reference => reference.Kind == TriggerReferenceKind.Watch && reference.IsEffective && reference.Target == trigger
              && !reference.Line.GetValue (1).Equals ("INACTIVE", StringComparison.OrdinalIgnoreCase));
      if (waiting is not null)
        message += $" '{waiting.Source.Nickname}' wartet per Cnd_WatchTrigger (Zeile {waiting.Line.Number}) darauf und löst deshalb nie aus.";

      // A dead trigger that puts the player somewhere (Act_MovePlayer) is mostly a developer jump point into the middle of
      // the mission, like goto_hole_key in the original m13: it sets the scene for a test start.
      IniLine? jump = activators.Length == 0 ? trigger.Actions.FirstOrDefault (action => action.IsKey ("Act_MovePlayer")) : null;
      if (jump is not null)
        message += $" Vermutlich ein Entwickler-Sprungpunkt: Er versetzt den Spieler (Act_MovePlayer, Zeile {jump.Number}) und baut die Szene für einen Einstieg mitten in der Mission auf.";
      string hint = "Die Engine prüft die Bedingung eines Triggers erst, wenn er aktiv ist; aktiv wird er nur durch InitState = ACTIVE, "
          + "durch Act_ActTrig eines laufenden Triggers oder als Act_Save-Ziel beim Laden. Cnd_WatchTrigger beobachtet nur. "
          + "Folge: Bedingung und Aktionen dieses Triggers wirken nie – ";
      hint += jump is not null
          ? "im Spiel hat er keine Wirkung. Zum Testen in einer Kopie der Mission: beim Sprungpunkt 'InitState = ACTIVE' direkt unter nickname "
            + "eintragen und es beim regulären Starttrigger entfernen (sonst läuft der Missionsanfang parallel und spawnt doppelt). "
            + "Abhilfe, wenn er nicht gebraucht wird: entfernen oder stehen lassen – beides ändert am Spiel nichts."
          : "oft ein Überbleibsel, manchmal ein vergessener oder falsch geschriebener Act_ActTrig (siehe 'unknown-trigger'). "
            + "Abhilfe: den Act_ActTrig ergänzen oder den toten Trigger entfernen.";
      yield return new Finding (waiting is null ? FindingSeverity.Info : FindingSeverity.Warning, Id, trigger.NicknameLine.Number, trigger.Nickname, message,
          hint, waiting?.Line.Number);
    }
  }
}
