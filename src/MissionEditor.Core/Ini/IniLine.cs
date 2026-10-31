namespace MissionEditor.Core.Ini;

/// <summary>What a single physical line of an INI file contains.</summary>
public enum IniLineKind
{
  /// <summary>Empty or whitespace only.</summary>
  Blank,

  /// <summary>A line whose first non-blank character is ';'.</summary>
  Comment,

  /// <summary>A section header such as <c>[Trigger]</c>.</summary>
  Header,

  /// <summary>A <c>key = value, value</c> line (the '=' and the values are optional).</summary>
  Entry,
}

/// <summary>
/// One physical line of an INI file. The original text and line ending are kept unchanged, so a
/// document writes back byte for byte; the parsed parts follow the rules of Freelancer's
/// INI_Reader (common.dll): ';' ends the line, values are split at ',' and trimmed.
/// </summary>
public sealed class IniLine
{
  private static readonly string[] NoValues = [];

  private IniLine (int number, string text, string lineEnding)
  {
    Number = number;
    Text = text;
    LineEnding = lineEnding;
    Values = NoValues;
  }

  /// <summary>1-based line number in the file.</summary>
  public int Number
  {
    get;
  }

  /// <summary>The line without its line ending, exactly as read.</summary>
  public string Text
  {
    get;
  }

  /// <summary>"\r\n", "\n", "\r" or "" for a last line without one.</summary>
  public string LineEnding
  {
    get;
  }

  /// <summary>What the line contains.</summary>
  public IniLineKind Kind
  {
    get; private set;
  }

  /// <summary>For a header: the section name between the brackets, trimmed.</summary>
  public string? SectionName
  {
    get; private set;
  }

  /// <summary>For an entry: the key left of '=', trimmed.</summary>
  public string? Key
  {
    get; private set;
  }

  /// <summary>For an entry: the comma separated values, trimmed (empty without '=').</summary>
  public IReadOnlyList<string> Values
  {
    get; private set;
  }

  /// <summary>The comment part starting with ';', if any.</summary>
  public string? Comment
  {
    get; private set;
  }

  /// <summary>The value part right of '=' without the comment, trimmed.</summary>
  public string RawValue { get; private set; } = string.Empty;

  /// <summary>Returns the value at <paramref name="index"/> or an empty string.</summary>
  public string GetValue (int index) => index < Values.Count ? Values[index] : string.Empty;

  /// <summary>Reads a boolean while honoring BINI's typed numeric parameters.</summary>
  public bool GetBoolean (int index)
  {
    if (BinaryParameters is { } parameters)
      return index >= 0 && index < parameters.Count && parameters[index].BooleanValue;
    string value = GetValue (index);
    return value.Equals ("true", StringComparison.OrdinalIgnoreCase) || (int.TryParse (value, out int number) && number != 0);
  }

  /// <summary>True when this is an entry with the given key (case-insensitive, like is_value).</summary>
  public bool IsKey (string key) => Kind == IniLineKind.Entry && string.Equals (Key, key, StringComparison.OrdinalIgnoreCase);

  /// <summary>Parses one line.</summary>
  /// <param name="number">1-based line number.</param>
  /// <param name="text">The line without its line ending.</param>
  /// <param name="lineEnding">The line ending that followed the text.</param>
  public static IniLine Parse (int number, string text, string lineEnding)
  {
    var line = new IniLine (number, text, lineEnding);
    string trimmed = text.Trim ();
    int commentStart = text.IndexOf (';');
    string content = commentStart >= 0 ? text[..commentStart] : text;
    if (commentStart >= 0)
      line.Comment = text[commentStart..];

    if (trimmed.Length == 0)
    {
      line.Kind = IniLineKind.Blank;
      return line;
    }
    if (trimmed[0] == ';')
    {
      line.Kind = IniLineKind.Comment;
      return line;
    }
    if (trimmed[0] == '[')
    {
      // INI_Reader reads the name up to ']' including any ';'; "[;Name]" is a commented-out
      // section. The comment split above does not apply to headers.
      line.Comment = null;
      int nameEnd = trimmed.IndexOf (']');
      string name = nameEnd > 0 ? trimmed[1..nameEnd] : trimmed[1..];
      line.Kind = IniLineKind.Header;
      line.SectionName = name.Trim ();
      return line;
    }

    line.Kind = IniLineKind.Entry;
    int equalsIndex = content.IndexOf ('=');
    if (equalsIndex < 0)
    {
      line.Key = content.Trim ();
      return line;
    }
    line.Key = content[..equalsIndex].Trim ();
    line.RawValue = content[(equalsIndex + 1)..].Trim ();
    line.Values = line.RawValue.Length == 0
        ? NoValues
        : line.RawValue.Split (',').Select (value => value.Trim ()).ToArray ();
    return line;
  }

  internal IReadOnlyList<BiniParameter>? BinaryParameters
  {
    get; private set;
  }

  internal static IniLine BinaryHeader (int number, string name) => new (number, $"[{name}]", "\r\n")
  {
    Kind = IniLineKind.Header,
    SectionName = name,
  };

  internal static IniLine BinaryEntry (int number, string key, IReadOnlyList<BiniParameter> parameters)
  {
    string display = string.Join (", ", parameters.Select (value => value.Display));
    return new IniLine (number, parameters.Count == 0 ? key : $"{key} = {display}", "\r\n")
    {
      Kind = IniLineKind.Entry,
      Key = key,
      Values = parameters.Select (value => value.Value).ToArray (),
      RawValue = string.Join (", ", parameters.Select (value => value.Value)),
      BinaryParameters = parameters,
    };
  }

  internal IniLine AtLine (int number) => new (number, Text, LineEnding)
  {
    Kind = Kind,
    SectionName = SectionName,
    Key = Key,
    Values = Values,
    Comment = Comment,
    RawValue = RawValue,
    BinaryParameters = BinaryParameters,
  };

  /// <inheritdoc/>
  public override string ToString () => $"{Number}: {Text}";
}
