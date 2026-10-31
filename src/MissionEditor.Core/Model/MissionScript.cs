using MissionEditor.Core.Ini;

namespace MissionEditor.Core.Model;

/// <summary>
/// A mission script (the INI behind [Mission] / GenericMission): triggers, their references and
/// the named objects, on top of the unchanged <see cref="IniDocument"/>.
/// </summary>
public sealed class MissionScript
{
    private readonly Dictionary<uint, Trigger> _triggersById = [];

    private MissionScript(IniDocument document, string? filePath)
    {
        Document = document;
        FilePath = filePath;

        var triggers = new List<Trigger>();
        var objects = new List<ScriptObject>();
        var sectionsWithoutNickname = new List<IniSection>();
        foreach (IniSection section in document.Sections)
        {
            if (section.IsCommentedOut)
                continue;
            if (section.Is("Trigger"))
            {
                Trigger? trigger = ReadTrigger(section);
                if (trigger is null)
                {
                    sectionsWithoutNickname.Add(section);
                    continue;
                }
                // The engine keeps the first trigger of an id and skips later ones.
                if (!_triggersById.TryAdd(trigger.Id, trigger))
                    trigger.IsShadowed = true;
                triggers.Add(trigger);
                continue;
            }
            ScriptObjectKind? kind = ScriptObject.KindOf(section.Name);
            if (kind is not null)
                objects.Add(new ScriptObject(kind.Value, section, section.Find("nickname")));
        }

        foreach (Trigger trigger in triggers)
        {
            foreach (TriggerReference reference in trigger.References)
                reference.Target = _triggersById.GetValueOrDefault(reference.TargetId);
        }

        Triggers = triggers;
        Objects = objects;
        TriggerSectionsWithoutNickname = sectionsWithoutNickname;
    }

    /// <summary>The underlying file.</summary>
    public IniDocument Document { get; }

    /// <summary>Where the script was loaded from, if from a file.</summary>
    public string? FilePath { get; }

    /// <summary>All [Trigger] sections with a nickname, in file order, including shadowed duplicates.</summary>
    public IReadOnlyList<Trigger> Triggers { get; }

    /// <summary>The triggers the engine uses (first of each id).</summary>
    public IEnumerable<Trigger> EffectiveTriggers => Triggers.Where(trigger => !trigger.IsShadowed);

    /// <summary>All named non-trigger sections in file order.</summary>
    public IReadOnlyList<ScriptObject> Objects { get; }

    /// <summary>[Trigger] sections the engine skips because they have no nickname.</summary>
    public IReadOnlyList<IniSection> TriggerSectionsWithoutNickname { get; }

    /// <summary>Loads a script from a file.</summary>
    public static MissionScript Load(string path) => new(IniDocument.Load(path), path);

    /// <summary>Builds a script from a parsed document.</summary>
    public static MissionScript FromDocument(IniDocument document, string? filePath = null) => new(document, filePath);

    /// <summary>The trigger the engine uses for a nickname (case-insensitive), or null.</summary>
    public Trigger? FindTrigger(string nickname) => FindTrigger(Hashing.FlHash.CreateId(nickname));

    /// <summary>The trigger the engine uses for an id, or null.</summary>
    public Trigger? FindTrigger(uint id) => _triggersById.GetValueOrDefault(id);

    private static Trigger? ReadTrigger(IniSection section)
    {
        int nicknameIndex = -1;
        for (int i = 0; i < section.Entries.Count; i++)
        {
            if (section.Entries[i].IsKey("nickname"))
            {
                nicknameIndex = i;
                break;
            }
        }
        if (nicknameIndex < 0)
            return null;

        var trigger = new Trigger(section, section.Entries[nicknameIndex]);
        var ignored = new List<IgnoredEntry>();
        var conditions = new List<IniLine>();
        var actions = new List<IniLine>();
        var references = new List<TriggerReference>();

        for (int i = 0; i < nicknameIndex; i++)
        {
            // LoadMissionTriggerSection only evaluates "State" before the nickname, and drops it.
            if (!section.Entries[i].IsKey("system"))
                ignored.Add(new IgnoredEntry(section.Entries[i], IgnoreReason.BeforeNickname));
        }

        for (int i = nicknameIndex + 1; i < section.Entries.Count; i++)
        {
            IniLine entry = section.Entries[i];
            string key = entry.Key ?? string.Empty;
            if (EngineVocabulary.Conditions.Contains(key))
                conditions.Add(entry);
            else if (EngineVocabulary.Actions.Contains(key))
                actions.Add(entry);
            else if (entry.IsKey("InitState"))
                trigger.InitialState = ParseState(entry.GetValue(0), trigger.InitialState);
            else if (entry.IsKey("repeatable"))
                trigger.Repeatable |= entry.GetBoolean(0);
            else if (entry.IsKey("system"))
                trigger.System = entry.GetValue(0);
            else if (EngineVocabulary.LooksLikeCondition(key) || EngineVocabulary.LooksLikeAction(key))
                ignored.Add(new IgnoredEntry(entry, IgnoreReason.UnknownKey));
        }

        IniLine? effectiveCondition = conditions.Count > 0 ? conditions[^1] : null;
        foreach (IniLine condition in conditions)
        {
            if (condition.IsKey("Cnd_WatchTrigger") && condition.GetValue(0).Length > 0)
                references.Add(new TriggerReference(trigger, condition, TriggerReferenceKind.Watch, condition.GetValue(0), condition == effectiveCondition));
        }
        foreach (IniLine action in actions)
        {
            TriggerReferenceKind? kind = action.Key!.ToLowerInvariant() switch
            {
                "act_acttrig" => TriggerReferenceKind.Activate,
                "act_deacttrig" => TriggerReferenceKind.Deactivate,
                "act_save" => TriggerReferenceKind.Save,
                _ => null,
            };
            if (kind is not null && action.GetValue(0).Length > 0)
                references.Add(new TriggerReference(trigger, action, kind.Value, action.GetValue(0), true));
        }

        trigger.Conditions = conditions;
        trigger.Actions = actions;
        trigger.IgnoredEntries = ignored;
        trigger.References = references.OrderBy(reference => reference.Line.Number).ToArray();
        return trigger;
    }

    private static TriggerState ParseState(string value, TriggerState current) => value.ToUpperInvariant() switch
    {
        "ACTIVE" => TriggerState.Active,
        "INACTIVE" => TriggerState.Inactive,
        "COMPLETE" => TriggerState.Complete,
        _ => current,
    };

}
