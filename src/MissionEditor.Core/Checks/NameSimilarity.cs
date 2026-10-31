namespace MissionEditor.Core.Checks;

/// <summary>Finds similar names (case-insensitive edit distance) for "did you mean" suggestions.</summary>
public static class NameSimilarity
{
  /// <summary>The candidates with the smallest edit distance (at most three), or none if all are too far off.</summary>
  public static IReadOnlyList<string> FindClosest (string name, IEnumerable<string> candidates)
  {
    const int MaxSuggestions = 3;
    var best = new List<string> ();
    int bestDistance = Math.Min (3, Math.Max (2, name.Length / 5));
    string lowerName = name.ToLowerInvariant ();
    foreach (string candidate in candidates)
    {
      if (Math.Abs (candidate.Length - name.Length) > bestDistance)
        continue;
      int distance = Levenshtein (lowerName, candidate.ToLowerInvariant (), bestDistance + 1);
      if (distance > bestDistance)
        continue;
      if (distance < bestDistance)
      {
        bestDistance = distance;
        best.Clear ();
      }
      if (best.Count < MaxSuggestions)
        best.Add (candidate);
    }
    return best;
  }

  // Edit distance capped at the limit. Only cells with |i - j| < limit can stay below it, so just that
  // diagonal band is computed: O(length × limit) instead of O(length²) for long names.
  private static int Levenshtein (string first, string second, int limit)
  {
    if (Math.Abs (first.Length - second.Length) >= limit)
      return limit;
    var previous = new int[second.Length + 1];
    var current = new int[second.Length + 1];
    for (int j = 0; j <= second.Length; j++)
      previous[j] = Math.Min (j, limit);
    for (int i = 1; i <= first.Length; i++)
    {
      int bandStart = Math.Max (1, i - limit);
      int bandEnd = Math.Min (second.Length, i + limit);
      current[0] = Math.Min (i, limit);
      // The cells just outside the band count as "at the limit" for the neighbours inside it.
      if (bandStart > 1)
        current[bandStart - 1] = limit;
      int rowMinimum = bandStart == 1 ? current[0] : limit;
      for (int j = bandStart; j <= bandEnd; j++)
      {
        int cost = first[i - 1] == second[j - 1] ? 0 : 1;
        int distance = Math.Min (Math.Min (current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
        current[j] = Math.Min (distance, limit);
        rowMinimum = Math.Min (rowMinimum, current[j]);
      }
      if (bandEnd < second.Length)
        current[bandEnd + 1] = limit;
      if (rowMinimum >= limit)
        return limit;
      (previous, current) = (current, previous);
    }
    return previous[second.Length];
  }
}
