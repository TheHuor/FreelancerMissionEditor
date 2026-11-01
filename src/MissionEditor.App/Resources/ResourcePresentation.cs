using System.IO;
using System.Buffers.Binary;
using System.Xml;
using MissionEditor.Core.Model;

namespace MissionEditor.App;

// Storage identity and content format are independent: a string may contain RDL.
internal static class ResourcePresentation
{
  internal static bool IsRdl(string text) => MissionTextUsage.ContainsRdl(text);
  // ResourceEntry.RawBytes deliberately retains the complete block for safe writes.
  // The browser, however, displays only the selected string's UTF-16 payload.
  internal static byte[] EntryBytes(ResourceEntry entry)
  {
    if (!entry.HasRawResource) return [];
    byte[] bytes = entry.RawBytes!;
    if (entry.Identity?.Kind != ResourceKind.String) return bytes.ToArray();
    int offset = 0;
    for (int index = 0; index <= entry.Identity.LocalId % 16; index++)
    {
      if (offset + 2 > bytes.Length) throw new InvalidDataException("Unvollständiger Stringblock.");
      int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)) * 2;
      offset += 2;
      if (length > bytes.Length - offset) throw new InvalidDataException("Ungültige Stringlänge im Ressourcenblock.");
      if (index == entry.Identity.LocalId % 16) return bytes.AsSpan(offset, length).ToArray();
      offset += length;
    }
    return [];
  }

  internal static string Source(ResourceEntry entry) => entry.RawText ?? entry.Text;
  internal static string Storage(ResourceEntry entry) => !entry.HasRawResource ? "Nicht aufgelöst"
      : entry.Identity?.Kind == ResourceKind.String ? "String (Stringtabelle)" : "Infocard (RT_HTML)";
  internal static string Format(ResourceEntry entry, string? content = null) => entry.Identity?.Kind == ResourceKind.Infocard || IsRdl(content ?? Source(entry))
      ? "RDL/XML" : "Klartext";
  internal static string Preview(string source)
  {
    if (!IsRdl(source)) return source;
    try { return InfocardDocument.Parse(source).PlainText; }
    catch (Exception error) when (error is InvalidDataException or XmlException or ArgumentException)
    { return "RDL/XML nicht lesbar – Quelltext im XML-Modus korrigieren."; }
  }
  internal static string Guidance(ResourceEntry entry, string content)
  {
    if (!entry.HasRawResource) return "Kein Inhalt vorhanden. DLL, ID und Sprachvariante unter Technische Details prüfen.";
    if (entry.Identity?.Kind == ResourceKind.Infocard)
      return "Im Editor Text und Formatierung visuell ändern; mit XML zum Quelltext wechseln. Gespeichert wird weiterhin eine Infocard.";
    if (IsRdl(content))
      return "Dieser String enthält RDL/XML. Im Editor visuell bearbeiten oder mit XML zum Quelltext wechseln; Tags und Formatierung erhalten. Gespeichert wird weiterhin ein String.";
    return "Reinen Text unter Bearbeiten ändern. Gespeichert wird weiterhin ein String ohne RDL-Formatierung.";
  }
}
