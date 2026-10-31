using System.Text;

namespace MissionEditor.Core.Ini;

/// <summary>
/// A text INI file kept line by line, so it can be written back without any change: line endings,
/// comments, blank lines, spacing and the encoding stay as they were. Freelancer's text INIs are
/// ANSI (Windows-1252); a UTF-8 BOM is honoured. BINI v1 documents retain their binary format.
/// </summary>
public sealed class IniDocument
{
    private readonly byte[]? _originalBinary;

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    static IniDocument()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private IniDocument(IReadOnlyList<IniLine> lines, Encoding encoding, bool hasBom, bool isBini = false, byte[]? originalBinary = null)
    {
        Lines = lines;
        Encoding = encoding;
        HasBom = hasBom;
        IsBini = isBini;
        _originalBinary = originalBinary is null ? null : (byte[])originalBinary.Clone();

        var sections = new List<IniSection>();
        int lineIndex = 0;
        while (lineIndex < lines.Count && lines[lineIndex].Kind != IniLineKind.Header)
            lineIndex++;
        LeadingLines = lines.Take(lineIndex).ToArray();
        while (lineIndex < lines.Count)
        {
            IniLine header = lines[lineIndex++];
            int bodyStart = lineIndex;
            while (lineIndex < lines.Count && lines[lineIndex].Kind != IniLineKind.Header)
                lineIndex++;
            sections.Add(new IniSection(sections.Count, header, lines.Skip(bodyStart).Take(lineIndex - bodyStart).ToArray()));
        }
        Sections = sections;
    }

    /// <summary>The ANSI code page of Freelancer's text files.</summary>
    public static Encoding Windows1252 => Encoding.GetEncoding(1252);

    /// <summary>All physical lines in file order.</summary>
    public IReadOnlyList<IniLine> Lines { get; }

    /// <summary>Lines before the first header (comments, blanks, stray values).</summary>
    public IReadOnlyList<IniLine> LeadingLines { get; }

    /// <summary>All sections in file order, including commented-out ones.</summary>
    public IReadOnlyList<IniSection> Sections { get; }

    /// <summary>The encoding used to read and to write the file.</summary>
    public Encoding Encoding { get; }

    /// <summary>True when the file started with a UTF-8 BOM.</summary>
    public bool HasBom { get; }

    /// <summary>True for a binary BINI document; edits and Save As retain this format.</summary>
    public bool IsBini { get; }

    internal static IniDocument FromBinary(IReadOnlyList<IniLine> lines, byte[] original) =>
        new(lines, Windows1252, false, true, original);

    /// <summary>Reads a file.</summary>
    /// <exception cref="InvalidDataException">The BINI data is malformed or unsupported.</exception>
    public static IniDocument Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Parses the bytes of a file.</summary>
    /// <exception cref="InvalidDataException">The BINI data is malformed or unsupported.</exception>
    public static IniDocument Parse(byte[] data)
    {
        if (data.Length >= 4 && data[0] == 'B' && data[1] == 'I' && data[2] == 'N' && data[3] == 'I')
            return BiniCodec.Read(data);

        bool hasBom = data.AsSpan().StartsWith(Utf8Bom);
        Encoding encoding;
        string text;
        if (hasBom)
        {
            encoding = new UTF8Encoding(false);
            text = encoding.GetString(data, Utf8Bom.Length, data.Length - Utf8Bom.Length);
        }
        else
        {
            // Windows-1252 leaves five byte values undefined; if a file uses one of them the
            // round trip would change it, so such files fall back to Latin-1, which maps every byte.
            encoding = Windows1252;
            text = encoding.GetString(data);
            if (!encoding.GetBytes(text).AsSpan().SequenceEqual(data))
            {
                encoding = Encoding.Latin1;
                text = encoding.GetString(data);
            }
        }
        return new IniDocument(SplitLines(text), encoding, hasBom);
    }

    /// <summary>Parses text (for tests and new documents); it is written as Windows-1252.</summary>
    public static IniDocument ParseText(string text) => new(SplitLines(text), Windows1252, false);

    /// <summary>All sections with the given name (case-insensitive).</summary>
    public IEnumerable<IniSection> GetSections(string name) => Sections.Where(section => section.Is(name));

    /// <summary>The line with the given 1-based number, or null.</summary>
    public IniLine? GetLine(int number) => number >= 1 && number <= Lines.Count ? Lines[number - 1] : null;

    /// <summary>The section that contains the given line number, or null.</summary>
    public IniSection? FindSectionAt(int lineNumber)
    {
        foreach (IniSection section in Sections)
        {
            if (lineNumber >= section.FirstLineNumber && lineNumber <= section.LastLineNumber)
                return section;
        }
        return null;
    }

    /// <summary>
    /// The line range <see cref="RemoveSection"/> deletes: the header up to the last entry of the
    /// section, plus the blank lines right after it. Comments after the last entry stay, because they
    /// usually introduce the next section.
    /// </summary>
    public static (int FirstLine, int LastLine) GetRemovalRange(IniSection section)
    {
        int lastLine = section.Entries.Count > 0 ? section.Entries[^1].Number : section.Header.Number;
        foreach (IniLine line in section.Lines.Where(line => line.Number > lastLine))
        {
            if (line.Kind != IniLineKind.Blank)
                break;
            lastLine = line.Number;
        }
        return (section.Header.Number, lastLine);
    }

    /// <summary>
    /// Returns a new document without the given section (see <see cref="GetRemovalRange"/>); every
    /// other line, the encoding and the BOM stay exactly as they were.
    /// </summary>
    public IniDocument RemoveSection(IniSection section)
    {
        if (section.Index >= Sections.Count || Sections[section.Index] != section)
            throw new ArgumentException("Die Sektion gehört nicht zu diesem Dokument.", nameof(section));
        (int firstLine, int lastLine) = GetRemovalRange(section);
        if (IsBini)
            return ReplaceBinaryRange(firstLine, lastLine, []);
        var builder = new StringBuilder();
        foreach (IniLine line in Lines.Where(line => line.Number < firstLine || line.Number > lastLine))
            builder.Append(line.Text).Append(line.LineEnding);
        return new IniDocument(SplitLines(builder.ToString()), Encoding, HasBom);
    }

    /// <summary>
    /// Returns a new document in which the section (header up to its last entry; comments and blank
    /// lines after it stay) is replaced by <paramref name="newText"/>. The new lines get the line
    /// ending the file uses; everything else stays byte for byte.
    /// </summary>
    public IniDocument ReplaceSection(IniSection section, string newText)
    {
        if (section.Index >= Sections.Count || Sections[section.Index] != section)
            throw new ArgumentException("Die Sektion gehört nicht zu diesem Dokument.", nameof(section));
        int firstLine = section.Header.Number;
        int lastLine = section.Entries.Count > 0 ? section.Entries[^1].Number : section.Header.Number;
        string lineEnding = Lines.FirstOrDefault(line => line.LineEnding.Length > 0)?.LineEnding ?? "\r\n";
        string[] newLines = newText.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');

        if (IsBini)
            return ReplaceBinaryRange(firstLine, lastLine, newLines);
        // Characters the file's encoding cannot hold would silently turn into '?' on save.
        try
        {
            Encoding.GetEncoding(Encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(newText);
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidDataException(
                $"Das Zeichen '{exception.CharUnknown}' lässt sich in der Kodierung der Datei ({Encoding.WebName}) nicht speichern.", exception);
        }
        var builder = new StringBuilder();
        foreach (IniLine line in Lines.Where(line => line.Number < firstLine))
            builder.Append(line.Text).Append(line.LineEnding);
        for (int i = 0; i < newLines.Length; i++)
        {
            // The last new line keeps the ending of the last replaced line ("" at the end of the file).
            string ending = i < newLines.Length - 1 ? lineEnding : Lines[lastLine - 1].LineEnding;
            builder.Append(newLines[i]).Append(ending);
        }
        foreach (IniLine line in Lines.Where(line => line.Number > lastLine))
            builder.Append(line.Text).Append(line.LineEnding);
        return new IniDocument(SplitLines(builder.ToString()), Encoding, HasBom);
    }

    /// <summary>The section text from the header up to its last entry, as <see cref="ReplaceSection"/> replaces it.</summary>
    public static string GetEditableText(IniSection section)
    {
        int lastLine = section.Entries.Count > 0 ? section.Entries[^1].Number : section.Header.Number;
        IEnumerable<IniLine> lines = new[] { section.Header }.Concat(section.Lines.Where(line => line.Number <= lastLine));
        return string.Join(Environment.NewLine, lines.Select(line => line.Text));
    }

    private IniDocument ReplaceBinaryRange(int firstLine, int lastLine, IReadOnlyList<string> replacement)
    {
        IniLine[] source = Lines.Where(line => line.Number >= firstLine && line.Number <= lastLine).ToArray();
        var available = new HashSet<IniLine>(source);
        var lines = Lines.Where(line => line.Number < firstLine).ToList();
        foreach (string text in replacement)
        {
            IniLine? exact = source.FirstOrDefault(line => available.Contains(line) && line.Text == text);
            if (exact is not null)
            {
                available.Remove(exact);
                lines.Add(exact);
                continue;
            }
            IniLine parsed = BiniCodec.ParseEntry(lines.Count + 1, text);
            if (parsed.BinaryParameters is { } parameters)
            {
                IniLine? previous = source.FirstOrDefault(line => available.Contains(line) && line.IsKey(parsed.Key!));
                if (previous?.BinaryParameters is { } oldParameters)
                {
                    available.Remove(previous);
                    // Preserve unchanged sibling payloads even when another parameter was edited.
                    BiniParameter[] preserved = parameters.Select((parameter, index) =>
                        index < oldParameters.Count && parameter.Value == oldParameters[index].Value
                            ? oldParameters[index] : parameter).ToArray();
                    parsed = IniLine.BinaryEntry(lines.Count + 1, parsed.Key!, preserved);
                }
            }
            lines.Add(parsed);
        }
        lines.AddRange(Lines.Where(line => line.Number > lastLine));
        return new IniDocument(lines.Select((line, index) => line.AtLine(index + 1)).ToArray(), Encoding, false, true);
    }

    /// <summary>The original format as bytes; unchanged BINI documents retain their exact original bytes.</summary>
    public byte[] ToBytes()
    {
        if (IsBini)
            return _originalBinary is null ? BiniCodec.Write(this) : (byte[])_originalBinary.Clone();
        var builder = new StringBuilder();
        foreach (IniLine line in Lines)
            builder.Append(line.Text).Append(line.LineEnding);
        byte[] body = Encoding.GetBytes(builder.ToString());
        return HasBom ? [.. Utf8Bom, .. body] : body;
    }

    /// <summary>Writes the document to a file.</summary>
    public void Save(string path) => File.WriteAllBytes(path, ToBytes());

    private static List<IniLine> SplitLines(string text)
    {
        var lines = new List<IniLine>();
        int lineStart = 0;
        int position = 0;
        while (position < text.Length)
        {
            char current = text[position];
            if (current != '\r' && current != '\n')
            {
                position++;
                continue;
            }
            int endingLength = current == '\r' && position + 1 < text.Length && text[position + 1] == '\n' ? 2 : 1;
            lines.Add(IniLine.Parse(lines.Count + 1, text[lineStart..position], text.Substring(position, endingLength)));
            position += endingLength;
            lineStart = position;
        }
        if (lineStart < text.Length)
            lines.Add(IniLine.Parse(lines.Count + 1, text[lineStart..], string.Empty));
        return lines;
    }
}
