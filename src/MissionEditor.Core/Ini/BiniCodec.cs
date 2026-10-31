using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionEditor.Core.Ini;

/// <summary>One packed BINI parameter, retaining its original type and payload.</summary>
internal sealed record BiniParameter(byte Type, uint Raw, string? StringValue = null)
{
    public string Value => Type switch
    {
        0 => ((byte)Raw).ToString(CultureInfo.InvariantCulture),
        1 => unchecked((int)Raw).ToString(CultureInfo.InvariantCulture),
        2 => BitConverter.UInt32BitsToSingle(Raw).ToString("R", CultureInfo.InvariantCulture),
        _ => StringValue!,
    };

    internal bool BooleanValue
    {
        get
        {
            if (Type == 0) return (byte)Raw != 0;
            if (Type == 1) return Raw != 0;
            if (Type == 2) return BitConverter.UInt32BitsToSingle(Raw) != 0;
            string value = StringValue!.TrimStart();
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            int end = value.Length > 0 && value[0] is '+' or '-' ? 1 : 0;
            while (end < value.Length && char.IsAsciiDigit(value[end])) end++;
            return int.TryParse(value[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number != 0;
        }
    }

    public string Display => Type == 3 ? JsonSerializer.Serialize(StringValue) : Value;
}

/// <summary>Freelancer BINI v1: little-endian records followed by zero-terminated ANSI strings.</summary>
internal static class BiniCodec
{
    // Every value may point at the same long string, so a small file can decode to a huge text.
    // Real BINI files stay far below this ratio; the floor keeps small files unaffected.
    private const int MaxExpansionFactor = 16;
    private const long MinimumDecodedBudget = 1 << 20;

    internal static IniDocument Read(byte[] data)
    {
        long decodedBudget = Math.Max(MinimumDecodedBudget, (long)data.Length * MaxExpansionFactor);
        long decodedLength = 0;
        if (data.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != 1)
            throw Invalid("Header oder Version ungültig (unterstützt: BINI 1).");
        uint tableValue = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        if (tableValue < 12 || tableValue > data.Length)
            throw Invalid("Stringtabelle liegt außerhalb der Datei.");
        int table = (int)tableValue;
        int position = 12;
        void Require(int count)
        {
            if (count > table - position)
                throw Invalid("Abgeschnittener Sektions-, Eintrags- oder Parameterdatensatz.");
        }
        ushort U16()
        {
            Require(2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));
            position += 2;
            return value;
        }
        string StringAt(uint offset)
        {
            if (offset >= data.Length - table)
                throw Invalid("Ungültiger Stringverweis.");
            int start = table + (int)offset;
            int end = Array.IndexOf(data, (byte)0, start);
            if (end < 0)
                throw Invalid("String ohne Nullterminierung.");
            decodedLength += end - start;
            if (decodedLength > decodedBudget)
                throw Invalid($"Entpackter Inhalt ist mehr als {MaxExpansionFactor}-mal so groß wie die Datei (beschädigte oder manipulierte Datei).");
            byte[] bytes = data[start..end];
            string text = IniDocument.Windows1252.GetString(bytes);
            return IniDocument.Windows1252.GetBytes(text).AsSpan().SequenceEqual(bytes)
                ? text : Encoding.Latin1.GetString(bytes);
        }
        var lines = new List<IniLine>();
        while (position < table)
        {
            Require(4);
            string section = StringAt(U16());
            int count = U16();
            lines.Add(IniLine.BinaryHeader(lines.Count + 1, section));
            for (int entry = 0; entry < count; entry++)
            {
                Require(3);
                string key = StringAt(U16());
                int parameters = data[position++];
                Require(parameters * 5);
                var values = new List<BiniParameter>(parameters);
                for (int parameter = 0; parameter < parameters; parameter++)
                {
                    byte type = data[position++];
                    uint raw = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
                    position += 4;
                    if (type > 3)
                        throw Invalid($"Unbekannter Parametertyp {type}.");
                    values.Add(new BiniParameter(type, raw, type == 3 ? StringAt(raw) : null));
                }
                lines.Add(IniLine.BinaryEntry(lines.Count + 1, key, values));
            }
        }
        return IniDocument.FromBinary(lines, data);
    }

    internal static byte[] Write(IniDocument document)
    {
        if (document.Lines.Any(line => line.Kind == IniLineKind.Comment || line.Comment is not null))
            throw Invalid("BINI kann keine Kommentare speichern. Kommentare vor dem Speichern entfernen.");
        if (document.LeadingLines.Any(line => line.Kind == IniLineKind.Entry))
            throw Invalid("Einträge ohne Sektion können nicht als BINI gespeichert werden.");
        using var strings = new MemoryStream();
        using var records = new MemoryStream();
        using var writer = new BinaryWriter(records);
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        int Add(string value)
        {
            if (value.Contains('\0'))
                throw Invalid("Ein String enthält ein Nullzeichen.");
            if (offsets.TryGetValue(value, out int found))
                return found;
            int offset = checked((int)strings.Length);
            Encoding strict = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            byte[] bytes;
            try { bytes = strict.GetBytes(value); }
            catch (EncoderFallbackException)
            {
                if (value.Any(ch => ch > 255))
                    throw Invalid("Ein Zeichen lässt sich nicht in der BINI-Zeichenkodierung Windows-1252 speichern.");
                bytes = Encoding.Latin1.GetBytes(value);
            }
            strings.Write(bytes);
            strings.WriteByte(0);
            offsets.Add(value, offset);
            return offset;
        }
        // Names have 16-bit offsets; place all names before potentially large string values.
        foreach (IniSection section in document.Sections)
        {
            Add(section.Name);
            foreach (IniLine entry in section.Entries)
                Add(entry.Key!);
        }
        ushort Name(string value)
        {
            int offset = Add(value);
            if (offset > ushort.MaxValue)
                throw Invalid("Sektions- und Schlüsselnamen überschreiten den BINI-Adressbereich (65535).");
            return (ushort)offset;
        }
        foreach (IniSection section in document.Sections)
        {
            if (section.Entries.Count > ushort.MaxValue)
                throw Invalid("Mehr als 65535 Einträge in einer BINI-Sektion.");
            writer.Write(Name(section.Name));
            writer.Write((ushort)section.Entries.Count);
            foreach (IniLine entry in section.Entries)
            {
                IReadOnlyList<BiniParameter> parameters = entry.BinaryParameters ?? ParseEntry(entry.Number, entry.Text).BinaryParameters!;
                if (parameters.Count > byte.MaxValue)
                    throw Invalid("Mehr als 255 Parameter in einem BINI-Eintrag.");
                writer.Write(Name(entry.Key!));
                writer.Write((byte)parameters.Count);
                foreach (BiniParameter parameter in parameters)
                {
                    writer.Write(parameter.Type);
                    writer.Write(parameter.Type == 3 ? (uint)Add(parameter.StringValue!) : parameter.Raw);
                }
            }
        }
        if (strings.Length == 0)
            strings.WriteByte(0);
        using var result = new MemoryStream();
        using var output = new BinaryWriter(result);
        output.Write("BINI"u8);
        output.Write(1u);
        output.Write(checked(12 + (int)records.Length));
        output.Write(records.ToArray());
        output.Write(strings.ToArray());
        return result.ToArray();
    }

    /// <summary>Quoted BINI strings use JSON escaping, keeping commas, semicolons and newlines intact.</summary>
    internal static IniLine ParseEntry(int number, string text)
    {
        IniLine ordinary = IniLine.Parse(number, text, "\r\n");
        if (ordinary.Kind != IniLineKind.Entry)
            return ordinary;
        int equals = text.IndexOf('=');
        string key = equals < 0 ? text.Trim() : text[..equals].Trim();
        var parameters = new List<BiniParameter>();
        int cursor = equals < 0 ? text.Length : equals + 1;
        while (cursor < text.Length)
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor >= text.Length) break;
            if (text[cursor] == ';')
                throw Invalid("BINI kann keine Kommentare speichern.");
            if (text[cursor] == '"')
            {
                int start = cursor++;
                bool closed = false;
                while (cursor < text.Length)
                {
                    char ch = text[cursor++];
                    if (ch == '\\') { cursor++; continue; }
                    if (ch == '"') { closed = true; break; }
                }
                if (!closed || cursor > text.Length)
                    throw Invalid("Nicht abgeschlossener BINI-String.");
                try { parameters.Add(new BiniParameter(3, 0, JsonSerializer.Deserialize<string>(text[start..cursor])!)); }
                catch (JsonException) { throw Invalid("Ungültige Escape-Sequenz im BINI-String."); }
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            }
            else
            {
                int start = cursor;
                while (cursor < text.Length && text[cursor] != ',' && text[cursor] != ';') cursor++;
                string token = text[start..cursor].Trim();
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
                    parameters.Add(new BiniParameter(1, unchecked((uint)integer)));
                else if (token.IndexOfAny(['.', 'e', 'E']) >= 0 && float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float real) && float.IsFinite(real))
                    parameters.Add(new BiniParameter(2, BitConverter.SingleToUInt32Bits(real)));
                else
                    parameters.Add(new BiniParameter(3, 0, token));
            }
            if (cursor >= text.Length) break;
            if (text[cursor++] != ',')
                throw Invalid("Nach einem BINI-Wert wird ein Komma erwartet; Kommentare sind nicht darstellbar.");
            if (string.IsNullOrWhiteSpace(text[cursor..]))
                parameters.Add(new BiniParameter(3, 0, string.Empty));
        }
        return IniLine.BinaryEntry(number, key, parameters);
    }

    private static InvalidDataException Invalid(string reason) => new($"Ungültige BINI-Datei: {reason}");
}
