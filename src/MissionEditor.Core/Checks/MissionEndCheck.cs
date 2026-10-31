using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Whether the mission can succeed: an Act_ChangeState = SUCCEED in a trigger that can become active.
/// Scripts without any SUCCEED (e.g. parts that end through another mechanism) only get a hint.
/// </summary>
public sealed class MissionEndCheck : IMissionCheck
{
  /// <inheritdoc/>
  public string Id => "mission-end";

  /// <inheritdoc/>
  public string Title => "Erfolgsende";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    MissionFlow flow = MissionFlow.Of (script);
    if (flow.Roots.Count == 0)
      yield break;
    var successes = script.EffectiveTriggers
        .SelectMany (trigger => trigger.Actions
            .Where (action => action.IsKey ("Act_ChangeState") && action.GetValue (0).Equals ("SUCCEED", StringComparison.OrdinalIgnoreCase))
            .Select (action => (Trigger: trigger, Line: action)))
        .ToArray ();
    if (successes.Any (success => flow.Reachable.Contains (success.Trigger)))
      yield break;

    Trigger start = flow.Roots[0];
    if (successes.Length == 0)
    {
      yield return new Finding (FindingSeverity.Info, Id, start.NicknameLine.Number, null,
          "Die Mission hat kein Act_ChangeState = SUCCEED – sie kann über das Skript nicht als gelungen enden.",
          "Eine Story- oder Auftragsmission endet erfolgreich mit 'Act_ChangeState = SUCCEED'. Fehlt die Zeile, läuft die Mission weiter, "
          + "bis sie scheitert oder abgelöst wird. Für Skriptteile, die anders enden, ist das in Ordnung.");
      yield break;
    }
    (Trigger trigger, var line) = successes[0];
    yield return new Finding (FindingSeverity.Warning, Id, line.Number, trigger.Nickname,
        $"Act_ChangeState = SUCCEED steht nur in Triggern, die nie aktiv werden (z. B. '{trigger.Nickname}') – die Mission kann nicht gelingen.",
        "Vom Start (InitState = ACTIVE) führt keine Kette aus Act_ActTrig zu einem Trigger mit SUCCEED. Abhilfe: den unterbrochenen "
        + "Act_ActTrig finden – die Funde 'unreachable-trigger' und 'unknown-trigger' zeigen meist die Stelle.");
  }
}
