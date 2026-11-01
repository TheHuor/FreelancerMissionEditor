using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Progress that waits for a mission ship (Cnd_DistShip, Cnd_DistVec, Cnd_TLEntered/Exited, Cnd_LaunchComplete on
/// an [MsnShip]) while nothing covers its death: no Act_Invulnerable = ship, true, and no Cnd_Destroyed trigger on it
/// that the section around the waiting trigger activates. If the ship is shot down the mission hangs. Death triggers
/// switched on elsewhere (e.g. general ones that a later Act_DeactTrig turns off) do not count.
/// </summary>
public sealed class VulnerableEscortCheck : IMissionCheck
{
  /// <inheritdoc/>
  public string Id => "vulnerable-escort";

  /// <inheritdoc/>
  public string Title => "Ungeschütztes Begleitschiff";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    MissionFlow flow = MissionFlow.Of (script);
    Trigger[] reachable = script.EffectiveTriggers.Where (flow.Reachable.Contains).ToArray ();
    foreach (Trigger trigger in reachable)
    {
      // Only progress counts: the waiting trigger must be the one way on – it saves, ends the mission or is the only
      // activator of a further trigger. A trigger that just gives the ship new orders blocks nothing.
      if (trigger.Condition is not { } condition || !CarriesProgress (flow, trigger))
        continue;
      foreach (string shipName in ShipArguments (condition).Where (name => name.Length > 0 && !name.Equals ("Player", StringComparison.OrdinalIgnoreCase)).Distinct (StringComparer.OrdinalIgnoreCase))
      {
        var ships = flow.ObjectsCalled (shipName).Where (item => item.Kind == ScriptObjectKind.Ship).ToList ();
        if (ships.Count == 0)
          continue;
        HashSet<string> names = Names (shipName, ships);
        HashSet<Trigger> section = Section (flow, trigger, ships);
        // Act_Invulnerable only reaches ships that exist when it runs: it has to come with the spawn or after it.
        bool invulnerable = section.Any (step => step.Actions.Any (action => action.IsKey ("Act_Invulnerable")
            && names.Contains (action.GetValue (0)) && action.GetBoolean (1)));
        if (invulnerable || HasDeathTrigger (flow, section, ships))
          continue;
        yield return new Finding (FindingSeverity.Warning, Id, condition.Number, trigger.Nickname,
            $"'{trigger.Nickname}' wartet auf '{shipName}' ({condition.Key}); wird das Schiff zerstört, geht es hier nicht weiter – "
            + "weder Act_Invulnerable noch ein Cnd_Destroyed-Trigger in diesem Abschnitt fängt das ab.",
            "Die Bedingung tritt nur ein, solange das Schiff existiert. Abhilfe: das Schiff mit 'Act_Invulnerable = <schiff>, true' schützen "
            + "oder im selben Abschnitt (beim Spawnen oder beim Aktivieren des wartenden Triggers) einen Trigger mit 'Cnd_Destroyed = <schiff>' "
            + "aktivieren, der die Mission scheitern lässt oder weiterführt. Allgemeine Todes-Trigger, die anderswo eingeschaltet und später "
            + "abgeschaltet werden, zählen hier nicht.");
      }
    }
  }

  // The ships the conditions wait for. "Player approaches X" (Cnd_DistShip with the player on one side) mostly means
  // a target or meeting point, not a ship the mission depends on, and is left out.
  private static IEnumerable<string> ShipArguments (IniLine condition) => condition.Key!.ToLowerInvariant () switch
  {
    "cnd_distship" when IsPlayer (condition.GetValue (1)) || IsPlayer (condition.GetValue (2)) => [],
    "cnd_distship" => [condition.GetValue (1), condition.GetValue (2)],
    "cnd_distvec" => [condition.GetValue (1)],
    "cnd_tlentered" or "cnd_tlexited" or "cnd_launchcomplete" => [condition.GetValue (0)],
    _ => [],
  };

  private static bool IsPlayer (string name) => name.Equals ("Player", StringComparison.OrdinalIgnoreCase);

  private static bool CarriesProgress (MissionFlow flow, Trigger trigger) =>
      trigger.Actions.Any (action => action.IsKey ("Act_Save") || action.IsKey ("Act_ChangeState"))
      || trigger.References.Any (reference => reference.Kind == TriggerReferenceKind.Activate && reference.Target is { } next
          && flow.ActivatorsOf (next).Where (flow.Reachable.Contains).All (activator => activator == trigger));

  // The section around the waiting trigger, while the ship exists: the triggers that spawn it and those they
  // activate within a few steps, plus the waiting trigger's activators.
  private static HashSet<Trigger> Section (MissionFlow flow, Trigger waiting, IReadOnlyCollection<ScriptObject> ships)
  {
    var section = new HashSet<Trigger> (flow.ActivatorsOf (waiting));
    var frontier = flow.SpawnsOf (ships).Select (spawn => spawn.Spawn.Source).OfType<Trigger> ().ToList ();
    for (int step = 0; step <= 3 && frontier.Count > 0; step++)
    {
      frontier = frontier.Where (section.Add).ToList ();
      frontier = frontier.SelectMany (item => item.References)
          .Where (reference => reference.Kind == TriggerReferenceKind.Activate && reference.Target is not null)
          .Select (reference => reference.Target!)
          .ToList ();
    }
    return section;
  }

  // A Cnd_Destroyed trigger on the ship in the section or switched on by it.
  private static bool HasDeathTrigger (MissionFlow flow, HashSet<Trigger> section, IReadOnlyCollection<ScriptObject> ships)
  {
    IEnumerable<Trigger> activated = section.SelectMany (step => step.References)
        .Where (reference => reference.Kind == TriggerReferenceKind.Activate && reference.Target is not null)
        .Select (reference => reference.Target!);
    return section.Concat (activated).Any (death => death.Condition is { } condition && condition.IsKey ("Cnd_Destroyed")
        && flow.ObjectsCalled (condition.GetValue (0)).Any (ships.Contains));
  }

  private static HashSet<string> Names (string shipName, IReadOnlyList<ScriptObject> ships)
  {
    var names = new HashSet<string> (StringComparer.OrdinalIgnoreCase) { shipName };
    foreach (ScriptObject ship in ships)
    {
      names.Add (ship.Nickname);
      foreach (IniLine label in ship.Section.Entries.Where (entry => entry.IsKey ("label")))
        names.Add (label.GetValue (0));
    }
    return names;
  }
}
