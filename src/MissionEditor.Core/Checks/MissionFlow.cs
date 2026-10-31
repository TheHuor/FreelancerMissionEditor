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
public sealed record ObjectReference(ObjectUse Use, string Name, IniLine Line, Trigger? Source);

/// <summary>
/// The flow of a script as the engine runs it: which triggers can ever become active and which
/// objects the actions spawn or use. Shared by the gap checks.
/// </summary>
public sealed class MissionFlow
{
    private MissionFlow(MissionScript script)
    {
        Script = script;
        Roots = script.EffectiveTriggers.Where(trigger => trigger.InitialState == TriggerState.Active).ToArray();

        // A trigger becomes active through InitState = ACTIVE, an Act_ActTrig of an active trigger, or as
        // the Act_Save target when a story save is loaded. Cnd_WatchTrigger only watches, it activates nothing.
        var reachable = new HashSet<Trigger>(Roots);
        var queue = new Queue<Trigger>(Roots);
        while (queue.TryDequeue(out Trigger? trigger))
        {
            foreach (TriggerReference reference in trigger.References)
            {
                if (reference.Kind is TriggerReferenceKind.Activate or TriggerReferenceKind.Save
                    && reference.Target is { } target && reachable.Add(target))
                    queue.Enqueue(target);
            }
        }
        Reachable = reachable;

        var references = new List<ObjectReference>();
        foreach (Trigger trigger in script.EffectiveTriggers)
        {
            foreach (IniLine action in trigger.Actions)
                AddActionReferences(references, action, trigger);
        }
        foreach (ScriptObject ship in script.Objects.Where(item => item.Kind == ScriptObjectKind.Ship))
        {
            foreach (IniLine line in ship.Section.Entries.Where(entry => entry.IsKey("init_objectives")))
                Add(references, ObjectUse.ObjList, line.GetValue(0), line, null);
        }
        References = references;
    }

    /// <summary>The script.</summary>
    public MissionScript Script { get; }

    /// <summary>The triggers active from the start (InitState = ACTIVE).</summary>
    public IReadOnlyList<Trigger> Roots { get; }

    /// <summary>The triggers that can become active at some point.</summary>
    public IReadOnlySet<Trigger> Reachable { get; }

    /// <summary>References from actions of the effective triggers and from object sections to named objects.</summary>
    public IReadOnlyList<ObjectReference> References { get; }

    /// <summary>Analyses a script.</summary>
    public static MissionFlow Of(MissionScript script) => new(script);

    /// <summary>The [MsnShip]/[MsnFormation] entries a spawn of the name hits: the first exact match (strcmp).</summary>
    public ScriptObject? FindShipGroup(string name) =>
        Script.Objects.FirstOrDefault(item => item.Kind is ScriptObjectKind.Ship or ScriptObjectKind.Formation && item.Nickname == name);

    /// <summary>
    /// The [MsnShip] and [MsnFormation] entries that get spawned: named by a spawn action, or a ship that a
    /// spawned formation takes along ("ship =", exact name).
    /// </summary>
    public IReadOnlySet<ScriptObject> SpawnedShipGroups()
    {
        var spawned = new HashSet<ScriptObject>();
        foreach (ObjectReference reference in References.Where(reference => reference.Use == ObjectUse.SpawnShip))
        {
            if (FindShipGroup(reference.Name) is { } group)
                spawned.Add(group);
        }
        foreach (ScriptObject formation in spawned.Where(item => item.Kind == ScriptObjectKind.Formation).ToArray())
        {
            foreach (IniLine member in formation.Section.Entries.Where(entry => entry.IsKey("ship")))
            {
                if (FindShipGroup(member.GetValue(0)) is { Kind: ScriptObjectKind.Ship } ship)
                    spawned.Add(ship);
            }
        }
        return spawned;
    }

    private static void AddActionReferences(List<ObjectReference> references, IniLine action, Trigger trigger)
    {
        switch (action.Key!.ToLowerInvariant())
        {
            case "act_spawnship":
            case "act_spawnshiprel":
                Add(references, ObjectUse.SpawnShip, action.GetValue(0), action, trigger);
                Add(references, ObjectUse.ObjList, action.GetValue(1), action, trigger);
                break;
            case "act_spawnformation":
                Add(references, ObjectUse.SpawnShip, action.GetValue(0), action, trigger);
                break;
            case "act_spawnsolar":
                Add(references, ObjectUse.SpawnSolar, action.GetValue(0), action, trigger);
                break;
            case "act_spawnloot":
                Add(references, ObjectUse.SpawnLoot, action.GetValue(0), action, trigger);
                break;
            case "act_giveobjlist":
                Add(references, ObjectUse.ObjList, action.GetValue(1), action, trigger);
                break;
            case "act_startdialog":
                Add(references, ObjectUse.Dialog, action.GetValue(0), action, trigger);
                break;
            case "act_setnnobj":
            case "act_setnnstate":
                Add(references, ObjectUse.Objective, action.GetValue(0), action, trigger);
                break;
            case "act_setnnhidden":
                if (!action.GetValue(0).Equals("all", StringComparison.OrdinalIgnoreCase))
                    Add(references, ObjectUse.Objective, action.GetValue(0), action, trigger);
                break;
        }
    }

    private static void Add(List<ObjectReference> references, ObjectUse use, string name, IniLine line, Trigger? source)
    {
        // "no_ol" is the scripts' way of saying "no object list" (Act_SpawnShip checks CreateID("no_ol")).
        if (name.Length > 0 && !(use == ObjectUse.ObjList && name.Equals("no_ol", StringComparison.OrdinalIgnoreCase)))
            references.Add(new ObjectReference(use, name, line, source));
    }
}
