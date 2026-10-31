namespace MissionEditor.Core.Ini;

/// <summary>How a section differs between the open document and another file.</summary>
public enum SectionChangeKind
{
  /// <summary>Both have the section, with different text.</summary>
  Changed,

  /// <summary>Only the other file has it.</summary>
  Added,

  /// <summary>Only the open document has it.</summary>
  Removed,
}

/// <summary>One differing section.</summary>
/// <param name="Kind">Changed, added or removed.</param>
/// <param name="Key">Identifies the section: name, nickname and occurrence (all lower case).</param>
/// <param name="Title">For the user, e.g. "[Trigger] start".</param>
/// <param name="Before">The text in the open document (empty when added).</param>
/// <param name="After">The text in the other file (empty when removed).</param>
/// <param name="CurrentLine">First line in the open document, if it has the section.</param>
/// <param name="OtherLine">First line in the other file, if it has the section.</param>
public sealed record SectionChange (SectionChangeKind Kind, string Key, string Title, string Before, string After, int? CurrentLine, int? OtherLine);

/// <summary>
/// Compares two mission files section by section (sections are matched by name, nickname and occurrence)
/// and applies chosen differences to the open document, e.g. to take over a draft the AI agent exported.
/// </summary>
public static class SectionDiff
{
  /// <summary>The sections that differ, in the order of the other file (removed ones where they were).</summary>
  public static IReadOnlyList<SectionChange> Compare (IniDocument current, IniDocument other)
  {
    Dictionary<string, IniSection> currentByKey = KeyedSections (current).ToDictionary (item => item.Key, item => item.Section);
    var otherKeys = new HashSet<string> ();
    var changes = new List<SectionChange> ();
    foreach ((string key, IniSection section) in KeyedSections (other))
    {
      otherKeys.Add (key);
      string after = IniDocument.GetEditableText (section);
      if (!currentByKey.TryGetValue (key, out IniSection? mine))
        changes.Add (new SectionChange (SectionChangeKind.Added, key, TitleOf (section), string.Empty, after, null, section.FirstLineNumber));
      else if (IniDocument.GetEditableText (mine) is var before && !SameText (before, after))
        changes.Add (new SectionChange (SectionChangeKind.Changed, key, TitleOf (section), before, after, mine.FirstLineNumber, section.FirstLineNumber));
    }
    foreach ((string key, IniSection section) in KeyedSections (current).Where (item => !otherKeys.Contains (item.Key)))
      changes.Add (new SectionChange (SectionChangeKind.Removed, key, TitleOf (section), IniDocument.GetEditableText (section), string.Empty, section.FirstLineNumber, null));
    return changes;
  }

  /// <summary>
  /// The open document with the chosen changes applied: changed sections replaced, removed ones deleted,
  /// added ones inserted after the section that precedes them in the other file.
  /// </summary>
  /// <exception cref="InvalidDataException">A text cannot be stored in the document's encoding.</exception>
  public static IniDocument Apply (IniDocument current, IniDocument other, IEnumerable<SectionChange> changes)
  {
    List<SectionChange> chosen = changes.ToList ();
    IniDocument document = current;
    foreach (SectionChange change in chosen.Where (change => change.Kind == SectionChangeKind.Changed))
    {
      if (Find (document, change.Key) is { } section)
        document = document.ReplaceSection (section, change.After);
    }
    foreach (SectionChange change in chosen.Where (change => change.Kind == SectionChangeKind.Removed))
    {
      if (Find (document, change.Key) is { } section)
        document = document.RemoveSection (section);
    }
    List<(string Key, IniSection Section)> otherSections = KeyedSections (other).ToList ();
    foreach (SectionChange change in chosen.Where (change => change.Kind == SectionChangeKind.Added))
    {
      int position = otherSections.FindIndex (item => item.Key == change.Key);
      IniSection? anchor = null;
      for (int i = position - 1; i >= 0 && anchor is null; i--)
        anchor = Find (document, otherSections[i].Key);
      if (anchor is not null)
        document = document.ReplaceSection (anchor, IniDocument.GetEditableText (anchor) + "\n\n" + change.After);
      else if (document.Sections.Count > 0)
        document = document.ReplaceSection (document.Sections[0], change.After + "\n\n" + IniDocument.GetEditableText (document.Sections[0]));
    }
    return document;
  }

  private static IniSection? Find (IniDocument document, string key) =>
      KeyedSections (document).FirstOrDefault (item => item.Key == key).Section;

  private static IEnumerable<(string Key, IniSection Section)> KeyedSections (IniDocument document)
  {
    var occurrences = new Dictionary<string, int> ();
    foreach (IniSection section in document.Sections)
    {
      string identity = $"{section.Name.ToLowerInvariant ()}|{section.Find ("nickname")?.GetValue (0).ToLowerInvariant () ?? string.Empty}";
      int occurrence = occurrences[identity] = occurrences.GetValueOrDefault (identity) + 1;
      yield return ($"{identity}|{occurrence}", section);
    }
  }

  private static string TitleOf (IniSection section) =>
      section.Find ("nickname")?.GetValue (0) is { Length: > 0 } nickname ? $"[{section.Name}] {nickname}" : $"[{section.Name}]";

  private static bool SameText (string first, string second) =>
      first.Replace ("\r\n", "\n").TrimEnd () == second.Replace ("\r\n", "\n").TrimEnd ();
}
