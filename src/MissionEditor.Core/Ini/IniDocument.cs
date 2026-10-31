using System.Text;

namespace MissionEditor.Core.Ini;

/// <summary>
/// A text INI file kept line by line, so it can be written back without any change: line endings,
/// comments, blank lines, spacing and the encoding stay as they were. Freelancer's text INIs are
/// ANSI (Windows-1252); a UTF-8 BOM is honoured. Binary BINI files are rejected.
/// </summary>
public sealed class IniDocument
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    static IniDocument()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private IniDocument(IReadOnlyList<IniLine> lines, Encoding encoding, bool hasBom)
    {
        Lines = lines;
        Encoding = encoding;
        HasBom = hasBom;

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

    /// <summary>Reads a file.</summary>
    /// <exception cref="InvalidDataException">The file is a binary BINI file.</exception>
    public static IniDocument Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Parses the bytes of a file.</summary>
    /// <exception cref="InvalidDataException">The data is a binary BINI file.</exception>
    public static IniDocument Parse(byte[] data)
    {
        if (data.Length >= 4 && data[0] == 'B' && data[1] == 'I' && data[2] == 'N' && data[3] == 'I')
            throw new InvalidDataException("Binäre INI-Datei (BINI) – wird noch nicht unterstützt.");

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

    /// <summary>The file content as bytes, including the BOM if the file had one.</summary>
    public byte[] ToBytes()
    {
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
