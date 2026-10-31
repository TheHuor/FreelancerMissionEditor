using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Actions that name an object the script does not define: Spawn* (exact name, strcmp), object lists,
/// dialogs and objectives (CreateID, any case). The engine skips such an action without a message.
/// </summary>
public sealed class UnknownObjectCheck : IMissionCheck
{
  /// <inheritdoc/>
  public string Id => "unknown-object";

  /// <inheritdoc/>
  public string Title => "Unbekanntes Objekt";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    MissionFlow flow = MissionFlow.Of (script);
    foreach (ObjectReference reference in flow.References)
    {
      ScriptObjectKind[] kinds = KindsOf (reference.Use);
      ScriptObject[] candidates = script.Objects.Where (item => kinds.Contains (item.Kind)).ToArray ();
      bool exact = reference.Use is ObjectUse.SpawnShip or ObjectUse.SpawnSolar or ObjectUse.SpawnLoot;
      if (candidates.Any (item => exact ? item.Nickname == reference.Name : item.Nickname.Equals (reference.Name, StringComparison.OrdinalIgnoreCase)))
        continue;

      string sections = string.Join (" oder ", kinds.Select (kind => $"[{ScriptObject.SectionNameOf (kind)}]"));
      string where = reference.Source is null ? $"{reference.Line.Key}" : $"{reference.Line.Key} in '{reference.Source.Nickname}'";
      ScriptObject? otherCase = exact ? candidates.FirstOrDefault (item => item.Nickname.Equals (reference.Name, StringComparison.OrdinalIgnoreCase)) : null;
      string message;
      string hint;
      if (otherCase is not null)
      {
        message = $"{where}: '{reference.Name}' gibt es nur als '{otherCase.Nickname}' (Zeile {otherCase.LineNumber}) – die Schreibweise muss exakt stimmen.";
        hint = $"{reference.Line.Key} sucht den Namen mit strcmp, also mit Groß-/Kleinschreibung; ohne exakten Treffer passiert nichts. "
            + $"Abhilfe: '{otherCase.Nickname}' schreiben.";
      }
      else
      {
        IReadOnlyList<string> suggestions = NameSimilarity.FindClosest (reference.Name, candidates.Select (item => item.Nickname).Distinct ());
        message = $"{where}: kein {sections} mit dem Namen '{reference.Name}' – {EffectOf (reference.Use)}."
            + (suggestions.Count > 0 ? $" Gemeint vielleicht {string.Join (" oder ", suggestions.Select (name => $"'{name}'"))}?" : string.Empty);
        hint = $"Die Engine sucht {sections} nur in dieser Missionsdatei und übergeht die Aktion still, wenn es keinen Treffer gibt. "
            + "Abhilfe: Namen korrigieren oder die fehlende Sektion anlegen.";
      }
      // In a trigger that never becomes active the line does nothing anyway. A spawned ship without its
      // object list still appears; everything else does nothing at all.
      bool dead = reference.Source is { } source && !flow.Reachable.Contains (source);
      if (dead)
        message += $" (Der Trigger '{reference.Source!.Nickname}' wird ohnehin nie aktiviert.)";
      FindingSeverity severity = dead ? FindingSeverity.Info
          : reference.Use == ObjectUse.ObjList && !reference.Line.IsKey ("Act_GiveObjList") ? FindingSeverity.Warning
          : FindingSeverity.Error;
      yield return new Finding (severity, Id, reference.Line.Number, reference.Source?.Nickname, message, hint, otherCase?.LineNumber);
    }
  }

  private static ScriptObjectKind[] KindsOf (ObjectUse use) => use switch
  {
    ObjectUse.SpawnShip => [ScriptObjectKind.Ship, ScriptObjectKind.Formation],
    ObjectUse.SpawnSolar => [ScriptObjectKind.Solar],
    ObjectUse.SpawnLoot => [ScriptObjectKind.Loot],
    ObjectUse.ObjList => [ScriptObjectKind.ObjList],
    ObjectUse.Dialog => [ScriptObjectKind.Dialog],
    _ => [ScriptObjectKind.Objective],
  };

  private static string EffectOf (ObjectUse use) => use switch
  {
    ObjectUse.SpawnShip or ObjectUse.SpawnSolar or ObjectUse.SpawnLoot => "es wird nichts gespawnt",
    ObjectUse.ObjList => "das Schiff bekommt diese Befehle nie",
    ObjectUse.Dialog => "der Dialog wird nie abgespielt",
    _ => "das Ziel bzw. der Wegpunkt erscheint nie",
  };
}
