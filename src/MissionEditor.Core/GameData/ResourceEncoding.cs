using System.Buffers.Binary;
using System.Text;

namespace MissionEditor.Core.Model;

internal static class ResourceEncoding
{
  internal static string[] ReadStringBlock (byte[] bytes)
  {
    var strings = new string[16];
    int offset = 0;
    for (int index = 0; index < 16; index++)
    {
      if (offset + 2 > bytes.Length) throw new InvalidDataException ("Unvollständiger Stringblock.");
      int count = BinaryPrimitives.ReadUInt16LittleEndian (bytes.AsSpan (offset, 2));
      offset += 2;
      if (offset + count * 2 > bytes.Length) throw new InvalidDataException ("Ungültige Stringlänge im Ressourcenblock.");
      strings[index] = new UnicodeEncoding (false, false, true).GetString (bytes, offset, count * 2);
      offset += count * 2;
    }
    return strings;
  }

  internal static byte[] StringBlock (string[] values)
  {
    using var stream = new MemoryStream ();
    using var writer = new BinaryWriter (stream, Encoding.Unicode);
    foreach (string value in values)
    {
      ValidateString (value);
      writer.Write ((ushort)value.Length);
      writer.Write (new UnicodeEncoding (false, false, true).GetBytes (value));
    }
    return stream.ToArray ();
  }

  internal static void ValidateString (string text)
  {
    if (text.Contains ('\0') || text.Length > ushort.MaxValue)
      throw new InvalidDataException ("Strings dürfen keine Nullzeichen und höchstens 65535 UTF-16-Zeichen enthalten.");
    try { _ = new UnicodeEncoding (false, false, true).GetBytes (text); }
    catch (EncoderFallbackException exception) { throw new InvalidDataException ("Text enthält ungültiges Unicode.", exception); }
  }

  internal static (Encoding Encoding, int BomLength, int TerminatorLength) InfocardEncoding (byte[] bytes)
  {
    Encoding encoding;
    int bom;
    if (bytes.AsSpan ().StartsWith (new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding (false, false, true); bom = 2; }
    else if (bytes.AsSpan ().StartsWith (new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding (true, false, true); bom = 2; }
    else if (bytes.AsSpan ().StartsWith (new byte[] { 0xEF, 0xBB, 0xBF })) { encoding = new UTF8Encoding (false, true); bom = 3; }
    else if (bytes.Length >= 2 && bytes[1] == 0) { encoding = new UnicodeEncoding (false, false, true); bom = 0; }
    else if (bytes.Length >= 2 && bytes[0] == 0) { encoding = new UnicodeEncoding (true, false, true); bom = 0; }
    else { encoding = new UTF8Encoding (false, true); bom = 0; }
    int unit = encoding is UnicodeEncoding ? 2 : 1;
    int terminal = 0;
    while (bytes.Length - bom - terminal >= unit && bytes.AsSpan (bytes.Length - terminal - unit, unit).IndexOfAnyExcept ((byte)0) < 0)
      terminal += unit;
    return (encoding, bom, terminal);
  }

  internal static string ReadInfocard (byte[] bytes)
  {
    var encoding = InfocardEncoding (bytes);
    return encoding.Encoding.GetString (bytes, encoding.BomLength, bytes.Length - encoding.BomLength - encoding.TerminatorLength);
  }

  internal static byte[] InfocardBytes (byte[] original, string raw)
  {
    InfocardDocument.Validate (raw);
    var encoding = InfocardEncoding (original);
    byte[] content = encoding.Encoding.GetBytes (raw);
    byte[] result = new byte[encoding.BomLength + content.Length + encoding.TerminatorLength];
    original.AsSpan (0, encoding.BomLength).CopyTo (result);
    content.CopyTo (result, encoding.BomLength);
    return result;
  }
}
