using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Objects no action uses: ships and formations never spawned (directly or as a member of a spawned
/// formation), solars and loot never spawned, object lists never given, dialogs never started and
/// objectives never set. Usually leftovers; sometimes a missing spawn.
/// </summary>
public sealed class UnusedObjectCheck : IMissionCheck
{
  /// <inheritdoc/>
  public string Id => "unused-object";

  /// <inheritdoc/>
  public string Title => "Nie benutztes Objekt";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    MissionFlow flow = MissionFlow.Of (script);
    IReadOnlySet<ScriptObject> spawnedGroups = flow.SpawnedShipGroups ();
    bool UsedByCreateId (ObjectUse use, ScriptObject item) =>
        flow.References.Any (reference => reference.Use == use && reference.Name.Equals (item.Nickname, StringComparison.OrdinalIgnoreCase));
    bool UsedExactly (ObjectUse use, ScriptObject item) =>
        flow.References.Any (reference => reference.Use == use && reference.Name == item.Nickname);

    // A ship listed in a formation is reported through the formation, not twice.
    var formationMembers = script.Objects.Where (item => item.Kind == ScriptObjectKind.Formation)
        .SelectMany (formation => formation.Section.Entries.Where (entry => entry.IsKey ("ship")).Select (entry => entry.GetValue (0)))
        .ToHashSet (StringComparer.Ordinal);
    // Only the first entry of a name counts; later duplicates are the duplicate check's business.
    var seen = new HashSet<(ScriptObjectKind, string)> ();
    foreach (ScriptObject item in script.Objects)
    {
      if (item.Nickname.Length == 0 || !seen.Add ((Group (item.Kind), item.Kind is ScriptObjectKind.Ship or ScriptObjectKind.Formation or ScriptObjectKind.Solar or ScriptObjectKind.Loot ? item.Nickname : item.Nickname.ToLowerInvariant ())))
        continue;
      (bool used, string effect) = item.Kind switch
      {
        ScriptObjectKind.Ship => (spawnedGroups.Contains (item) || formationMembers.Contains (item.Nickname), "wird nie gespawnt (kein Act_SpawnShip und in keiner Formation)"),
        ScriptObjectKind.Formation => (spawnedGroups.Contains (item), "wird nie gespawnt (kein Act_SpawnFormation/Act_SpawnShip)"),
        ScriptObjectKind.Solar => (UsedExactly (ObjectUse.SpawnSolar, item), "wird nie gespawnt (kein Act_SpawnSolar)"),
        ScriptObjectKind.Loot => (UsedExactly (ObjectUse.SpawnLoot, item), "wird nie gespawnt (kein Act_SpawnLoot)"),
        ScriptObjectKind.ObjList => (UsedByCreateId (ObjectUse.ObjList, item), "wird nie vergeben (kein Act_GiveObjList, Act_SpawnShip oder init_objectives nennt sie)"),
        ScriptObjectKind.Dialog => (UsedByCreateId (ObjectUse.Dialog, item), "wird nie abgespielt (kein Act_StartDialog)"),
        ScriptObjectKind.Objective => (UsedByCreateId (ObjectUse.Objective, item), "wird nie gesetzt (kein Act_SetNNObj, Act_SetNNState oder Act_SetNNHidden)"),
        _ => (true, string.Empty),
      };
      if (used)
        continue;
      yield return new Finding (FindingSeverity.Info, Id, item.LineNumber, item.Nickname,
          $"[{ScriptObject.SectionNameOf (item.Kind)}] '{item.Nickname}' {effect}.",
          "Kein Trigger der Mission benutzt dieses Objekt, es hat im Spiel keine Wirkung. Meist ein Überbleibsel; "
          + "fehlt aber eine Aktion (oder ist ihr Name falsch geschrieben, siehe 'unknown-object'), bleibt ein Teil der Mission aus.");
    }
  }

  // [MsnShip] and [MsnFormation] share one name list.
  private static ScriptObjectKind Group (ScriptObjectKind kind) => kind == ScriptObjectKind.Formation ? ScriptObjectKind.Ship : kind;
}
