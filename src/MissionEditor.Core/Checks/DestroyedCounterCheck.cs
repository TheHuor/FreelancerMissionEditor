using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Cnd_Destroyed = X, N counts kills only while its trigger is active: activating it resets the count to 0, it
/// fires at count == N (fl_decomp MissionCnd.cpp, CndDestroyed). Reported: targets spawned in a trigger that only
/// later (over further triggers) activates the counting one – early kills never count and the mission can hang –
/// and an N larger than the number of objects ever spawned under that name or label.
/// </summary>
public sealed class DestroyedCounterCheck : IMissionCheck
{
  // Spawner → … → counting trigger: at most three activation steps (four triggers).
  private const int MaximumChainSteps = 3;

  /// <inheritdoc/>
  public string Id => "destroyed-counter";

  /// <inheritdoc/>
  public string Title => "Abschusszähler";

  /// <inheritdoc/>
  public IEnumerable<Finding> Run (MissionScript script)
  {
    MissionFlow flow = MissionFlow.Of (script);
    foreach (Trigger trigger in script.EffectiveTriggers)
    {
      // Without a count (or -1) the engine checks the remaining label members instead of counting.
      if (trigger.Condition is not { } condition || !condition.IsKey ("Cnd_Destroyed")
          || !int.TryParse (condition.GetValue (1), out int needed) || needed <= 0 || !flow.Reachable.Contains (trigger))
        continue;
      string target = condition.GetValue (0);
      IReadOnlyList<ScriptObject> holders = flow.ObjectsCalled (target);
      if (holders.Count == 0)
        continue;
      var spawns = flow.SpawnsOf (holders).Where (spawn => spawn.Spawn.Source is { } source && flow.Reachable.Contains (source)).ToList ();
      if (spawns.Count == 0)
        continue;

      int spawned = spawns.Sum (spawn => spawn.Count);
      if (needed > spawned)
      {
        yield return new Finding (FindingSeverity.Warning, Id, condition.Number, trigger.Nickname,
            $"Cnd_Destroyed = {condition.RawValue}: verlangt {needed} Abschüsse, gespawnt werden unter '{target}' höchstens {spawned} – '{trigger.Nickname}' löst nie aus.",
            "Die Engine zählt jeden Abschuss eines Objekts mit diesem Namen oder Label und löst bei genau N aus. Gezählt wurden alle Spawn-Aktionen "
            + "erreichbarer Trigger (Formationen mit ihren Mitgliedern). Abhilfe: N an die Zahl der Ziele anpassen oder die Anzahl weglassen "
            + "(dann löst der Trigger aus, wenn das letzte Objekt des Labels zerstört ist).");
        continue;
      }

      // The race: a trigger spawns the targets and a trigger shortly after in its chain activates the counting one.
      // Only counts of several kills ("destroy the group") – a single kill is mostly a death reaction – and short
      // chains, where the player plausibly engages before the counting trigger is switched on (the Mission 26 pattern).
      if (needed < 2)
        continue;
      foreach ((ObjectReference spawn, int _) in spawns)
      {
        Trigger spawner = spawn.Source!;
        if (spawner == trigger || flow.ActivatorsOf (trigger).Contains (spawner))
          continue;
        if (flow.ActivationPath (spawner, trigger, MaximumChainSteps) is not { Count: >= 3 } path)
          continue;
        bool cloaked = path.Skip (1).Any (step => step.Actions.Any (action => action.IsKey ("Act_Cloak")
            && !action.GetBoolean (1) && Names (target, holders).Contains (action.GetValue (0))));
        string chain = string.Join (" → ", path.Select (step => step.Nickname));
        yield return new Finding (cloaked ? FindingSeverity.Info : FindingSeverity.Warning, Id, condition.Number, trigger.Nickname,
            $"Cnd_Destroyed = {condition.RawValue} in '{trigger.Nickname}' zählt erst ab der Aktivierung, die Ziele spawnt aber schon '{spawner.Nickname}' "
            + $"(Zeile {spawn.Line.Number}); aktiviert wird erst über {chain}."
            + (cloaked
                ? " Die Ziele sind bis dahin getarnt (Act_Cloak … false erst später) – vermutlich gewollt."
                : " Was vorher abgeschossen wird, zählt nie – der Trigger kann hängen bleiben."),
            "Beim Aktivieren setzt die Engine den Zähler von Cnd_Destroyed auf 0 und zählt nur, solange der Trigger aktiv ist (fl_decomp "
            + "CndDestroyed::OnActivate). Abhilfe: den Zähltrigger schon im spawnenden Trigger per Act_ActTrig aktivieren, die Ziele erst im "
            + "aktivierenden Trigger spawnen oder sie bis dahin tarnen (Act_Cloak = <Ziel>, true / false).",
            spawn.Line.Number);
        break;
      }
    }
  }

  // The names Act_Cloak may use for the targets: the condition's name or label, the objects' nicknames and labels.
  private static HashSet<string> Names (string target, IReadOnlyList<ScriptObject> holders)
  {
    var names = new HashSet<string> (StringComparer.OrdinalIgnoreCase) { target };
    foreach (ScriptObject holder in holders)
    {
      names.Add (holder.Nickname);
      foreach (IniLine label in holder.Section.Entries.Where (entry => entry.IsKey ("label")))
        names.Add (label.GetValue (0));
    }
    return names;
  }
}
