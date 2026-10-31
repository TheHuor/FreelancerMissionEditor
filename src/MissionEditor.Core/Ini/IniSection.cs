namespace MissionEditor.Core.Ini;

/// <summary>A section: its header line and every line up to the next header.</summary>
public sealed class IniSection
{
  internal IniSection (int index, IniLine header, IReadOnlyList<IniLine> lines)
  {
    Index = index;
    Header = header;
    Lines = lines;
    Entries = lines.Where (line => line.Kind == IniLineKind.Entry).ToArray ();
  }

  /// <summary>Position of the section in the document (0-based).</summary>
  public int Index
  {
    get;
  }

  /// <summary>The header line.</summary>
  public IniLine Header
  {
    get;
  }

  /// <summary>The section name, e.g. "Trigger".</summary>
  public string Name => Header.SectionName ?? string.Empty;

  /// <summary>True for "[;Name]": INI_Reader skips such a section and its values.</summary>
  public bool IsCommentedOut => Name.StartsWith (';');

  /// <summary>All lines after the header up to the next header, including blanks and comments.</summary>
  public IReadOnlyList<IniLine> Lines
  {
    get;
  }

  /// <summary>The entry lines of <see cref="Lines"/>.</summary>
  public IReadOnlyList<IniLine> Entries
  {
    get;
  }

  /// <summary>Line number of the header.</summary>
  public int FirstLineNumber => Header.Number;

  /// <summary>Line number of the last line that belongs to the section.</summary>
  public int LastLineNumber => Lines.Count > 0 ? Lines[^1].Number : Header.Number;

  /// <summary>True when the section has the given name (case-insensitive, like is_header).</summary>
  public bool Is (string name) => string.Equals (Name, name, StringComparison.OrdinalIgnoreCase);

  /// <summary>The first entry with the given key, or null.</summary>
  public IniLine? Find (string key) => Entries.FirstOrDefault (entry => entry.IsKey (key));

  /// <summary>All entries with the given key.</summary>
  public IEnumerable<IniLine> FindAll (string key) => Entries.Where (entry => entry.IsKey (key));

  /// <summary>
  /// True when both sections have the same entries in the same order (keys case-insensitive,
  /// values as written, spacing and comments ignored) – i.e. they mean the same to the engine.
  /// </summary>
  public bool HasSameEntries (IniSection other) =>
      Is (other.Name)
      && Entries.Count == other.Entries.Count
      && Entries.Zip (other.Entries).All (pair =>
          string.Equals (pair.First.Key, pair.Second.Key, StringComparison.OrdinalIgnoreCase)
          && pair.First.Values.SequenceEqual (pair.Second.Values));

  /// <summary>The text of the section as it is in the file (header, entries, comments).</summary>
  public string GetText ()
  {
    var builder = new System.Text.StringBuilder ();
    builder.Append (Header.Text).Append ('\n');
    foreach (IniLine line in Lines)
      builder.Append (line.Text).Append ('\n');
    return builder.ToString ().TrimEnd ('\n', ' ', '\t') + "\n";
  }

  /// <inheritdoc/>
  public override string ToString () => $"[{Name}] @{FirstLineNumber}";
}
