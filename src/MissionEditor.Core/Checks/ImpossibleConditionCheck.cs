using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Cnd_Destroyed on a ship, formation, solar or label of this script that no action ever spawns: the
/// condition can never be met and the trigger never fires. Names the script does not define (objects of
/// the system, labels set elsewhere) are not judged.
/// </summary>
public sealed class ImpossibleConditionCheck : IMissionCheck
{
  /// <inheritdoc/>
  public string Id => "impossible-condition";

  /// <inheritdoc/>
  public string Title => "Bedingung kann nie eintreten";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    MissionFlow flow = MissionFlow.Of (script);
    var spawned = new HashSet<ScriptObject> (flow.SpawnedShipGroups ());
    foreach (ScriptObject solar in script.Objects.Where (item => item.Kind == ScriptObjectKind.Solar))
    {
      if (flow.References.Any (reference => reference.Use == ObjectUse.SpawnSolar && reference.Name == solar.Nickname))
        spawned.Add (solar);
    }
    ScriptObject[] candidates = script.Objects
        .Where (item => item.Kind is ScriptObjectKind.Ship or ScriptObjectKind.Formation or ScriptObjectKind.Solar).ToArray ();

    foreach (Trigger trigger in script.EffectiveTriggers)
    {
      if (trigger.Condition is not { } condition || !condition.IsKey ("Cnd_Destroyed"))
        continue;
      // Cnd_Destroyed compares CreateID values: the name or label matches in any case.
      string target = condition.GetValue (0);
      ScriptObject[] holders = candidates.Where (item => item.Nickname.Equals (target, StringComparison.OrdinalIgnoreCase)
          || item.Section.Entries.Any (entry => entry.IsKey ("label") && entry.GetValue (0).Equals (target, StringComparison.OrdinalIgnoreCase))).ToArray ();
      if (target.Length == 0 || holders.Length == 0 || holders.Any (spawned.Contains))
        continue;
      ScriptObject first = holders[0];
      string what = holders.Length == 1
          ? $"[{ScriptObject.SectionNameOf (first.Kind)}] '{first.Nickname}'"
          : $"keines der {holders.Length} Objekte mit diesem Namen oder Label";
      bool dead = !flow.Reachable.Contains (trigger);
      yield return new Finding (dead ? FindingSeverity.Info : FindingSeverity.Warning, Id, condition.Number, trigger.Nickname,
          $"Cnd_Destroyed = {target} kann nie eintreten – {what} wird nie gespawnt; Trigger '{trigger.Nickname}' löst nie aus."
          + (dead ? " (Der Trigger wird ohnehin nie aktiviert.)" : string.Empty),
          "Zerstört werden kann nur, was im Raum ist; Missionsobjekte kommen erst per Act_SpawnShip, Act_SpawnFormation oder Act_SpawnSolar "
          + "ins Spiel. Abhilfe: den fehlenden Spawn ergänzen, den Namen in der Spawn-Aktion korrigieren (exakte Schreibweise) oder die Bedingung anpassen.",
          first.LineNumber);
    }
  }
}
