using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>What an action does with a named object of the script.</summary>
public enum ObjectUse
{
  /// <summary>Act_SpawnShip, Act_SpawnShipRel, Act_SpawnFormation: an [MsnShip] or [MsnFormation], exact name.</summary>
  SpawnShip,

  /// <summary>Act_SpawnSolar: an [MsnSolar], exact name.</summary>
  SpawnSolar,

  /// <summary>Act_SpawnLoot: an [MsnLoot], exact name.</summary>
  SpawnLoot,

  /// <summary>The object list of Act_SpawnShip/SpawnShipRel/GiveObjList or "init_objectives": an [ObjList] by CreateID.</summary>
  ObjList,

  /// <summary>Act_StartDialog: a [Dialog] by CreateID.</summary>
  Dialog,

  /// <summary>Act_SetNNObj, Act_SetNNHidden, Act_SetNNState: an [NNObjective] by CreateID.</summary>
  Objective,
}

/// <summary>A reference from an action (or an object section) to a named object.</summary>
/// <param name="Use">What is done with the object.</param>
/// <param name="Name">The name as written.</param>
/// <param name="Line">The line with the reference.</param>
/// <param name="Source">The trigger that contains the line, or null for a reference from an object section.</param>
public sealed record ObjectReference (ObjectUse Use, string Name, IniLine Line, Trigger? Source);

/// <summary>
/// The flow of a script as the engine runs it: which triggers can ever become active and which
/// objects the actions spawn or use. Shared by the gap checks.
/// </summary>
public sealed class MissionFlow
{
  private MissionFlow (MissionScript script)
  {
    Script = script;
    Roots = script.EffectiveTriggers.Where (trigger => trigger.InitialState == TriggerState.Active).ToArray ();

    // A trigger becomes active through InitState = ACTIVE, an Act_ActTrig of an active trigger, or as
    // the Act_Save target when a story save is loaded. Cnd_WatchTrigger only watches, it activates nothing.
    var reachable = new HashSet<Trigger> (Roots);
    var queue = new Queue<Trigger> (Roots);
    while (queue.TryDequeue (out Trigger? trigger))
    {
      foreach (TriggerReference reference in trigger.References)
      {
        if (reference.Kind is TriggerReferenceKind.Activate or TriggerReferenceKind.Save
            && reference.Target is { } target && reachable.Add (target))
          queue.Enqueue (target);
      }
    }
    Reachable = reachable;

    var references = new List<ObjectReference> ();
    foreach (Trigger trigger in script.EffectiveTriggers)
    {
      foreach (IniLine action in trigger.Actions)
        AddActionReferences (references, action, trigger);
    }
    foreach (ScriptObject ship in script.Objects.Where (item => item.Kind == ScriptObjectKind.Ship))
    {
      foreach (IniLine line in ship.Section.Entries.Where (entry => entry.IsKey ("init_objectives")))
        Add (references, ObjectUse.ObjList, line.GetValue (0), line, null);
    }
    References = references;
  }

  /// <summary>The script.</summary>
  public MissionScript Script
  {
    get;
  }

  /// <summary>The triggers active from the start (InitState = ACTIVE).</summary>
  public IReadOnlyList<Trigger> Roots
  {
    get;
  }

  /// <summary>The triggers that can become active at some point.</summary>
  public IReadOnlySet<Trigger> Reachable
  {
    get;
  }

  /// <summary>References from actions of the effective triggers and from object sections to named objects.</summary>
  public IReadOnlyList<ObjectReference> References
  {
    get;
  }

  /// <summary>Analyses a script.</summary>
  public static MissionFlow Of (MissionScript script) => new (script);

  /// <summary>The [MsnShip]/[MsnFormation] entries a spawn of the name hits: the first exact match (strcmp).</summary>
  public ScriptObject? FindShipGroup (string name) =>
      Script.Objects.FirstOrDefault (item => item.Kind is ScriptObjectKind.Ship or ScriptObjectKind.Formation && item.Nickname == name);

  /// <summary>
  /// The [MsnShip] and [MsnFormation] entries that get spawned: named by a spawn action, or a ship that a
  /// spawned formation takes along ("ship =", exact name).
  /// </summary>
  public IReadOnlySet<ScriptObject> SpawnedShipGroups ()
  {
    var spawned = new HashSet<ScriptObject> ();
    foreach (ObjectReference reference in References.Where (reference => reference.Use == ObjectUse.SpawnShip))
    {
      if (FindShipGroup (reference.Name) is { } group)
        spawned.Add (group);
    }
    foreach (ScriptObject formation in spawned.Where (item => item.Kind == ScriptObjectKind.Formation).ToArray ())
    {
      foreach (IniLine member in formation.Section.Entries.Where (entry => entry.IsKey ("ship")))
      {
        if (FindShipGroup (member.GetValue (0)) is { Kind: ScriptObjectKind.Ship } ship)
          spawned.Add (ship);
      }
    }
    return spawned;
  }

  /// <summary>
  /// The ships and solars a name stands for the way conditions see it (CreateID, any case): an [MsnShip] or
  /// [MsnSolar] with that nickname or label, or the member ships of an [MsnFormation] with that nickname.
  /// </summary>
  public IReadOnlyList<ScriptObject> ObjectsCalled (string nameOrLabel)
  {
    if (nameOrLabel.Length == 0)
      return [];
    var found = new List<ScriptObject> ();
    foreach (ScriptObject item in Script.Objects)
    {
      bool named = item.Nickname.Equals (nameOrLabel, StringComparison.OrdinalIgnoreCase);
      if (item.Kind is ScriptObjectKind.Ship or ScriptObjectKind.Solar
          && (named || item.Section.Entries.Any (entry => entry.IsKey ("label") && entry.GetValue (0).Equals (nameOrLabel, StringComparison.OrdinalIgnoreCase))))
        found.Add (item);
      else if (item.Kind == ScriptObjectKind.Formation && named)
        found.AddRange (FormationMembers (item));
    }
    return found.Distinct ().ToList ();
  }

  /// <summary>The ships a formation takes along ("ship =", exact names of earlier [MsnShip]).</summary>
  public IEnumerable<ScriptObject> FormationMembers (ScriptObject formation) =>
      formation.Section.Entries.Where (entry => entry.IsKey ("ship"))
          .Select (entry => FindShipGroup (entry.GetValue (0)))
          .OfType<ScriptObject> ()
          .Where (item => item.Kind == ScriptObjectKind.Ship);

  /// <summary>
  /// The spawn actions that bring the objects into space, each with how many of <paramref name="wanted"/> it
  /// spawns: Act_SpawnShip/SpawnShipRel/SpawnFormation on a ship or a formation containing it, Act_SpawnSolar on a solar.
  /// </summary>
  public IEnumerable<(ObjectReference Spawn, int Count)> SpawnsOf (IReadOnlyCollection<ScriptObject> wanted)
  {
    foreach (ObjectReference reference in References)
    {
      if (reference.Use == ObjectUse.SpawnSolar)
      {
        int solars = wanted.Count (item => item.Kind == ScriptObjectKind.Solar && item.Nickname == reference.Name);
        if (solars > 0)
          yield return (reference, solars);
        continue;
      }
      if (reference.Use != ObjectUse.SpawnShip || FindShipGroup (reference.Name) is not { } group)
        continue;
      int ships = group.Kind == ScriptObjectKind.Formation ? FormationMembers (group).Count (wanted.Contains) : wanted.Contains (group) ? 1 : 0;
      if (ships > 0)
        yield return (reference, ships);
    }
  }

  /// <summary>The effective triggers whose Act_ActTrig activates the target.</summary>
  public IEnumerable<Trigger> ActivatorsOf (Trigger target) =>
      Script.EffectiveTriggers.Where (trigger => trigger.References.Any (reference => reference.Kind == TriggerReferenceKind.Activate && reference.Target == target));

  /// <summary>
  /// True when the mission's progress depends on the trigger: it saves, changes the mission state, or is the only
  /// reachable activator of a further trigger. A trigger that only sends a comm or gives orders blocks nothing.
  /// </summary>
  public bool CarriesProgress (Trigger trigger) =>
      trigger.Actions.Any (action => action.IsKey ("Act_Save") || action.IsKey ("Act_ChangeState"))
      || trigger.References.Any (reference => reference.Kind == TriggerReferenceKind.Activate && reference.Target is { } next
          && ActivatorsOf (next).Where (Reachable.Contains).All (activator => activator == trigger));

  /// <summary>
  /// The shortest chain of Act_ActTrig from one trigger to another (both included), or null when there is none
  /// within <paramref name="maximumSteps"/> steps.
  /// </summary>
  public IReadOnlyList<Trigger>? ActivationPath (Trigger from, Trigger to, int maximumSteps = 8)
  {
    var previous = new Dictionary<Trigger, Trigger> ();
    var seen = new HashSet<Trigger> { from };
    var queue = new Queue<(Trigger Trigger, int Steps)> ();
    queue.Enqueue ((from, 0));
    while (queue.TryDequeue (out var item))
    {
      if (item.Steps == maximumSteps)
        continue;
      foreach (TriggerReference reference in item.Trigger.References)
      {
        if (reference.Kind != TriggerReferenceKind.Activate || reference.Target is not { } next || !seen.Add (next) && next != to)
          continue;
        previous.TryAdd (next, item.Trigger);
        if (next == to)
        {
          var path = new List<Trigger> { to };
          for (Trigger current = to; current != from; current = previous[current])
            path.Add (previous[current]);
          path.Reverse ();
          return path;
        }
        queue.Enqueue ((next, item.Steps + 1));
      }
    }
    return null;
  }

  private static void AddActionReferences (List<ObjectReference> references, IniLine action, Trigger trigger)
  {
    switch (action.Key!.ToLowerInvariant ())
    {
      case "act_spawnship":
      case "act_spawnshiprel":
        Add (references, ObjectUse.SpawnShip, action.GetValue (0), action, trigger);
        Add (references, ObjectUse.ObjList, action.GetValue (1), action, trigger);
        break;
      case "act_spawnformation":
        Add (references, ObjectUse.SpawnShip, action.GetValue (0), action, trigger);
        break;
      case "act_spawnsolar":
        Add (references, ObjectUse.SpawnSolar, action.GetValue (0), action, trigger);
        break;
      case "act_spawnloot":
        Add (references, ObjectUse.SpawnLoot, action.GetValue (0), action, trigger);
        break;
      case "act_giveobjlist":
        Add (references, ObjectUse.ObjList, action.GetValue (1), action, trigger);
        break;
      case "act_startdialog":
        Add (references, ObjectUse.Dialog, action.GetValue (0), action, trigger);
        break;
      case "act_setnnobj":
      case "act_setnnstate":
        Add (references, ObjectUse.Objective, action.GetValue (0), action, trigger);
        break;
      case "act_setnnhidden":
        if (!action.GetValue (0).Equals ("all", StringComparison.OrdinalIgnoreCase))
          Add (references, ObjectUse.Objective, action.GetValue (0), action, trigger);
        break;
    }
  }

  private static void Add (List<ObjectReference> references, ObjectUse use, string name, IniLine line, Trigger? source)
  {
    // "no_ol" is the scripts' way of saying "no object list" (Act_SpawnShip checks CreateID("no_ol")).
    if (name.Length > 0 && !(use == ObjectUse.ObjList && name.Equals ("no_ol", StringComparison.OrdinalIgnoreCase)))
      references.Add (new ObjectReference (use, name, line, source));
  }
}
