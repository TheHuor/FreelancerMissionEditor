namespace MissionEditor.Core.Checks;

/// <summary>Finds similar names (case-insensitive edit distance) for "did you mean" suggestions.</summary>
internal static class NameSimilarity
{
    /// <summary>The candidates with the smallest edit distance (at most three), or none if all are too far off.</summary>
    public static IReadOnlyList<string> FindClosest(string name, IEnumerable<string> candidates)
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
