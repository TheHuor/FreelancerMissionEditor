using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// Nicknames defined more than once. Triggers are compared by CreateID (case-insensitive) and the
/// engine keeps only the first one. [MsnShip] and [MsnFormation] share one list that Spawn* looks up
/// case-sensitively, so exact duplicates there are a problem and case variants are suspicious.
/// Other sections are compared case-insensitively within their own type.
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
        var firstById = new Dictionary<uint, Trigger>();
        foreach (Trigger trigger in script.Triggers)
        {
            if (!firstById.TryGetValue(trigger.Id, out Trigger? first))
            {
                firstById[trigger.Id] = trigger;
                continue;
            }
            if (!string.Equals(first.Nickname, trigger.Nickname, StringComparison.OrdinalIgnoreCase))
            {
                yield return new Finding(FindingSeverity.Error, Id, trigger.NicknameLine.Number, trigger.Nickname,
                    $"Hash-Kollision: '{trigger.Nickname}' hat dieselbe ID ({trigger.Id}) wie '{first.Nickname}' (Zeile {first.NicknameLine.Number}); die Engine ignoriert diesen Trigger.");
                continue;
            }
            yield return new Finding(FindingSeverity.Warning, Id, trigger.NicknameLine.Number, trigger.Nickname,
                $"Trigger '{trigger.Nickname}' ist doppelt (erste Definition Zeile {first.NicknameLine.Number}); die Engine ignoriert diese zweite Definition.");
        }

        var shipsAndFormations = script.Objects.Where(item => item.Kind is ScriptObjectKind.Ship or ScriptObjectKind.Formation && item.Nickname.Length > 0);
        foreach (Finding finding in FindDuplicates(shipsAndFormations, StringComparer.Ordinal, "MsnShip/MsnFormation"))
            yield return finding;
        foreach (IGrouping<string, ScriptObject> variants in shipsAndFormations
            .GroupBy(item => item.Nickname, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Nickname).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            ScriptObject first = variants.First();
            foreach (ScriptObject other in variants.Skip(1).Where(item => item.Nickname != first.Nickname))
            {
                yield return new Finding(FindingSeverity.Info, Id, other.LineNumber, other.Nickname,
                    $"'{other.Nickname}' unterscheidet sich von '{first.Nickname}' (Zeile {first.LineNumber}) nur in Groß-/Kleinschreibung; Spawn-Aktionen unterscheiden die Schreibweise.");
            }
        }

        foreach (IGrouping<ScriptObjectKind, ScriptObject> kindGroup in script.Objects
            .Where(item => item.Kind is not (ScriptObjectKind.Ship or ScriptObjectKind.Formation) && item.Nickname.Length > 0)
            .GroupBy(item => item.Kind))
        {
            foreach (Finding finding in FindDuplicates(kindGroup, StringComparer.OrdinalIgnoreCase, ScriptObject.SectionNameOf(kindGroup.Key)))
                yield return finding;
        }
    }

    private IEnumerable<Finding> FindDuplicates(IEnumerable<ScriptObject> objects, StringComparer comparer, string listName)
    {
        foreach (IGrouping<string, ScriptObject> group in objects.GroupBy(item => item.Nickname, comparer).Where(group => group.Count() > 1))
        {
            ScriptObject first = group.First();
            foreach (ScriptObject other in group.Skip(1))
            {
                string sectionName = ScriptObject.SectionNameOf(other.Kind);
                yield return new Finding(FindingSeverity.Warning, Id, other.LineNumber, other.Nickname,
                    $"[{sectionName}] '{other.Nickname}' ist in {listName} doppelt (erste Definition Zeile {first.LineNumber}, [{ScriptObject.SectionNameOf(first.Kind)}]).");
            }
        }
    }
}
