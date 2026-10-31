using MissionEditor.Core.Model;

namespace MissionEditor.Core.Checks;

/// <summary>
/// References to trigger nicknames that no [Trigger] defines. The engine ignores such an
/// Act_ActTrig/Act_DeactTrig silently; a Cnd_WatchTrigger never fires and an Act_Save resumes
/// into nothing. A similar existing nickname is suggested.
/// </summary>
public sealed class UnknownTriggerTargetCheck : IMissionCheck
{
    /// <inheritdoc/>
    public string Id => "unknown-trigger";

    /// <inheritdoc/>
    public string Title => "Unbekanntes Trigger-Ziel";

    /// <inheritdoc/>
    public IEnumerable<Finding> Run(MissionScript script)
    {
        string[] names = script.EffectiveTriggers.Select(trigger => trigger.Nickname).ToArray();
        foreach (Trigger trigger in script.Triggers)
        {
            foreach (TriggerReference reference in trigger.References)
            {
                if (reference.Target is not null)
                    continue;
                FindingSeverity severity = trigger.IsShadowed ? FindingSeverity.Info
                    : reference.Kind == TriggerReferenceKind.Deactivate ? FindingSeverity.Warning
                    : FindingSeverity.Error;
                string effect = reference.Kind switch
                {
                    TriggerReferenceKind.Activate => "die Aktivierung passiert nie",
                    TriggerReferenceKind.Deactivate => "die Deaktivierung hat keine Wirkung",
                    TriggerReferenceKind.Watch => "die Bedingung wird nie erfüllt",
                    _ => "der Spielstand setzt an keinem Trigger fort",
                };
                string message = $"{TriggerReference.KeyOf(reference.Kind)} auf unbekannten Trigger '{reference.TargetName}' – {effect}.";
                IReadOnlyList<string> suggestions = FindClosest(reference.TargetName, names);
                if (suggestions.Count > 0)
                    message += $" Gemeint vielleicht {string.Join(" oder ", suggestions.Select(name => $"'{name}'"))}?";
                if (trigger.IsShadowed)
                    message += " (Der Trigger ist doppelt definiert und wird ohnehin ignoriert.)";
                yield return new Finding(severity, Id, reference.Line.Number, trigger.Nickname, message);
            }
        }
    }

    // The nicknames with the smallest edit distance (at most three), or none if all are too far off.
    private static IReadOnlyList<string> FindClosest(string name, string[] candidates)
    {
        const int MaxSuggestions = 3;
        var best = new List<string>();
        int bestDistance = Math.Min(3, Math.Max(2, name.Length / 5));
        string lowerName = name.ToLowerInvariant();
        foreach (string candidate in candidates)
        {
            if (Math.Abs(candidate.Length - name.Length) > bestDistance)
                continue;
            int distance = Levenshtein(lowerName, candidate.ToLowerInvariant(), bestDistance + 1);
            if (distance > bestDistance)
                continue;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best.Clear();
            }
            if (best.Count < MaxSuggestions)
                best.Add(candidate);
        }
        return best;
    }

    // Edit distance, cut short once every value of a row reaches the limit.
    private static int Levenshtein(string first, string second, int limit)
    {
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];
        for (int j = 0; j <= second.Length; j++)
            previous[j] = j;
        for (int i = 1; i <= first.Length; i++)
        {
            current[0] = i;
            int rowMinimum = i;
            for (int j = 1; j <= second.Length; j++)
            {
                int cost = first[i - 1] == second[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMinimum = Math.Min(rowMinimum, current[j]);
            }
            if (rowMinimum >= limit)
                return limit;
            (previous, current) = (current, previous);
        }
        return previous[second.Length];
    }
}
