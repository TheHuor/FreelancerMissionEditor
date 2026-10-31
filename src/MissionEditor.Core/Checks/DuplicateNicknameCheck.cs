using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Nicknames defined more than once, with what content.dll does with them (fl_decomp
/// MissionScript.cpp / MissionObject.cpp / MissionAct.cpp):
/// <list type="bullet">
/// <item>[Trigger], [NPC], [ObjList], [Dialog]: stored in a map under CreateID (case-insensitive),
/// unique insert – the first definition counts, later ones are dropped while loading.</item>
/// <item>[MsnShip]/[MsnFormation] (one shared list), [MsnSolar], [MsnLoot]: lists in file order;
/// Act_SpawnShip/SpawnFormation/SpawnSolar/SpawnLoot and formation "ship =" search with strcmp
/// and take the first match. Speaker/NPC lookups (FindMissionShip) use stricmp.</item>
/// <item>[MsnRandEnc]: Act_EnableEnc/DisableEnc switch every entry with that exact name.</item>
/// <item>[NNObjective]: id = number or CreateID; Act_SetNNObj sets the state of all matches but
/// makes only the first one visible.</item>
/// </list>
/// </summary>
public sealed class DuplicateNicknameCheck : IMissionCheck
{
    /// <inheritdoc/>
    public string Id => "duplicate-nickname";

    /// <inheritdoc/>
    public string Title => "Doppelter Nickname";

    /// <inheritdoc/>
    public IEnumerable<Finding> Run(MissionScript script)
    {
        foreach (Finding finding in CheckTriggers(script))
            yield return finding;

        // Case-insensitive maps (and objective ids): the first definition wins.
        foreach (ScriptObjectKind kind in new[] { ScriptObjectKind.Npc, ScriptObjectKind.ObjList, ScriptObjectKind.Dialog, ScriptObjectKind.Objective })
        {
            foreach (Finding finding in FindDuplicates(script.Objects.Where(item => item.Kind == kind), StringComparer.OrdinalIgnoreCase))
                yield return finding;
        }

        // Exact-name lists: Spawn* takes the first match; MsnShip and MsnFormation share one list.
        var exactLists = new[]
        {
            script.Objects.Where(item => item.Kind is ScriptObjectKind.Ship or ScriptObjectKind.Formation),
            script.Objects.Where(item => item.Kind == ScriptObjectKind.Solar),
            script.Objects.Where(item => item.Kind == ScriptObjectKind.Loot),
            script.Objects.Where(item => item.Kind == ScriptObjectKind.RandomEncounter),
        };
        foreach (IEnumerable<ScriptObject> list in exactLists)
        {
            foreach (Finding finding in FindDuplicates(list, StringComparer.Ordinal))
                yield return finding;
            foreach (Finding finding in FindCaseVariants(list))
                yield return finding;
        }
    }

    private IEnumerable<Finding> CheckTriggers(MissionScript script)
    {
        var firstById = new Dictionary<uint, Trigger>();
        foreach (Trigger trigger in script.Triggers)
        {
            if (!firstById.TryGetValue(trigger.Id, out Trigger? first))
            {
                firstById[trigger.Id] = trigger;
                continue;
            }
            int firstLine = first.NicknameLine.Number;
            if (!string.Equals(first.Nickname, trigger.Nickname, StringComparison.OrdinalIgnoreCase))
            {
                yield return new Finding(FindingSeverity.Error, Id, trigger.NicknameLine.Number, trigger.Nickname,
                    $"Hash-Kollision: '{trigger.Nickname}' hat dieselbe ID ({trigger.Id}) wie '{first.Nickname}' (Zeile {firstLine}); die Engine ignoriert diesen Trigger.",
                    "CreateID bildet beide Namen auf dieselbe Zahl ab; die Engine legt nur den ersten Trigger an. Abhilfe: einen der beiden Trigger umbenennen und seine Verweise anpassen.",
                    firstLine);
                continue;
            }
            yield return new Finding(FindingSeverity.Warning, Id, trigger.NicknameLine.Number, trigger.Nickname,
                $"Trigger '{trigger.Nickname}' ist doppelt (erste Definition Zeile {firstLine}); die Engine ignoriert diese zweite Definition.",
                "Die Engine legt Trigger unter ihrer ID ab (Groß-/Kleinschreibung egal) und überspringt jeden weiteren [Trigger] mit derselben ID beim Laden. "
                + "Act_ActTrig, Act_DeactTrig, Cnd_WatchTrigger und Act_Save treffen immer die erste Definition. "
                + Remedy(trigger.Section.HasSameEntries(first.Section)),
                firstLine);
        }
    }

    private IEnumerable<Finding> FindDuplicates(IEnumerable<ScriptObject> objects, StringComparer comparer)
    {
        foreach (IGrouping<string, ScriptObject> group in objects.Where(item => item.Nickname.Length > 0)
            .GroupBy(item => item.Nickname, comparer).Where(group => group.Count() > 1))
        {
            ScriptObject first = group.First();
            foreach (ScriptObject other in group.Skip(1))
            {
                string section = ScriptObject.SectionNameOf(other.Kind);
                string firstSection = ScriptObject.SectionNameOf(first.Kind);
                string where = section == firstSection ? $"Zeile {first.LineNumber}" : $"Zeile {first.LineNumber}, [{firstSection}]";
                yield return new Finding(FindingSeverity.Warning, Id, other.LineNumber, other.Nickname,
                    $"[{section}] '{other.Nickname}' ist doppelt (erste Definition {where}).",
                    $"{EngineEffect(other, first)} {Remedy(other.Section.HasSameEntries(first.Section))}",
                    first.LineNumber);
            }
        }
    }

    // Names that differ only in case: different entries for Spawn*, but easy to mix up.
    private IEnumerable<Finding> FindCaseVariants(IEnumerable<ScriptObject> objects)
    {
        foreach (IGrouping<string, ScriptObject> variants in objects.Where(item => item.Nickname.Length > 0)
            .GroupBy(item => item.Nickname, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Nickname).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            ScriptObject first = variants.First();
            foreach (ScriptObject other in variants.Where(item => item.Nickname != first.Nickname).DistinctBy(item => item.Nickname))
            {
                string effect = other.Kind is ScriptObjectKind.Ship or ScriptObjectKind.Formation
                    ? $"Act_SpawnShip/Act_SpawnFormation unterscheiden die Schreibweise (strcmp) und spawnen den richtigen Eintrag. Die Zuordnung von Sprechern und NPCs zu Schiffen (Funksprüche, Kostüme) vergleicht aber ohne Groß-/Kleinschreibung und findet immer zuerst '{first.Nickname}' (Zeile {first.LineNumber})."
                    : "Die Spawn- bzw. Schaltaktionen unterscheiden die Schreibweise (strcmp); ein Verweis mit falscher Groß-/Kleinschreibung trifft den anderen Eintrag oder keinen.";
                yield return new Finding(FindingSeverity.Info, Id, other.LineNumber, other.Nickname,
                    $"'{other.Nickname}' unterscheidet sich von '{first.Nickname}' (Zeile {first.LineNumber}) nur in Groß-/Kleinschreibung.",
                    $"{effect} Abhilfe: einen der beiden Namen deutlich anders benennen und die Verweise anpassen.",
                    first.LineNumber);
            }
        }
    }

    private static string EngineEffect(ScriptObject duplicate, ScriptObject first)
    {
        string line = $"Zeile {first.LineNumber}";
        return duplicate.Kind switch
        {
            ScriptObjectKind.Npc => $"Die Engine legt [NPC] unter CreateID(nickname) ab und verwirft beim Laden jede weitere Definition mit demselben Namen. Schiffe mit 'NPC = {duplicate.Nickname}' bekommen Schiff, Aussehen, Stimme und Zugehörigkeit aus {line}; diese Sektion wirkt nie.",
            ScriptObjectKind.ObjList => $"Die Engine legt [ObjList] unter CreateID(nickname) ab und verwirft beim Laden jede weitere Liste mit demselben Namen. Act_GiveObjList und 'objlist =' in [MsnShip] verwenden immer die Liste aus {line}; die Befehle dieser Liste werden nie ausgeführt.",
            ScriptObjectKind.Dialog => $"Die Engine legt [Dialog] unter CreateID(nickname) ab und verwirft beim Laden jeden weiteren Dialog mit demselben Namen. Act_StartDialog spielt immer den Dialog aus {line}.",
            ScriptObjectKind.Objective => $"Act_SetNNObj setzt den Zustand aller Ziele mit dieser ID, macht aber nur das erste ({line}) sichtbar; dieses Ziel hier bleibt in der Zielliste versteckt.",
            ScriptObjectKind.RandomEncounter => $"Act_EnableEnc und Act_DisableEnc schalten alle [MsnRandEnc] mit diesem Namen gleichzeitig – diese Begegnung und die aus {line} sind immer gemeinsam an oder aus.",
            ScriptObjectKind.Solar => $"Act_SpawnSolar sucht den Namen exakt und nimmt den ersten Treffer in Dateireihenfolge ({line}); dieser [MsnSolar] wird nie gespawnt.",
            ScriptObjectKind.Loot => $"Act_SpawnLoot sucht den Namen exakt und nimmt den ersten Treffer in Dateireihenfolge ({line}); dieser [MsnLoot] wird nie gespawnt.",
            _ => $"[MsnShip] und [MsnFormation] stehen in einer gemeinsamen Liste. Act_SpawnShip, Act_SpawnFormation und 'ship =' in Formationen suchen den Namen exakt und nehmen den ersten Treffer in Dateireihenfolge ({line}); dieser Eintrag wird über seinen Namen nie gespawnt.",
        };
    }

    private static string Remedy(bool sameEntries) => sameEntries
        ? "Beide Definitionen sind inhaltlich gleich – diese zweite kann ohne Folgen gelöscht werden."
        : "Die Definitionen unterscheiden sich. Abhilfe: Ist die zweite gemeint, die erste löschen oder die zweite umbenennen und die Verweise darauf anpassen; sonst die zweite löschen.";
}
