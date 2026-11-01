using System.Text;

namespace MissionEditor.Core.Ini;

/// <summary>A bounded section edit against a captured external INI, with conflict detection and backup.</summary>
public sealed class IniSectionFileEdit
{
  private readonly byte[] _original;
  /// <summary>Full target path.</summary>
  public string Path { get; }
  /// <summary>The file snapshot from which the editor was opened.</summary>
  public IniDocument Document { get; }
  /// <summary>The exact defining section.</summary>
  public IniSection Section { get; }

  private IniSectionFileEdit (string path, int line, int? expectedIds)
  {
    Path = System.IO.Path.GetFullPath (path);
    _original = File.ReadAllBytes (Path);
    Document = IniDocument.Parse (_original);
    Section = Document.Sections.FirstOrDefault (section => section.FirstLineNumber == line)
        ?? throw new InvalidDataException ("Die Herkunftsdefinition wurde verschoben. Spieldaten neu laden und erneut öffnen.");
    if (expectedIds is { } ids && !Section.Entries.Any (entry =>
        new[] { "ids_name", "strid_name", "ids_info", "individual_name" }.Any (entry.IsKey)
        && int.TryParse (entry.GetValue (0), out int value) && value == ids))
      throw new InvalidDataException ("Die Herkunftsdefinition hat sich geändert. Spieldaten neu laden und erneut öffnen.");
  }

  /// <summary>Capture a source file and verify that the requested definition still exists at its location.</summary>
  public static IniSectionFileEdit Open (string path, int line, int? expectedIds = null) => new (path, line, expectedIds);

  /// <summary>Replace only this section, preserving encoding/format and unrelated sections. Returns the backup path.</summary>
  public string Save (string text)
  {
    IniDocument changed = ApplySection (Document, Section, text);
    byte[] bytes = changed.ToBytes ();
    _ = IniDocument.Parse (bytes);
    using var original = new FileStream (Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
    using var snapshot = new MemoryStream ();
    original.CopyTo (snapshot);
    if (!_original.AsSpan ().SequenceEqual (snapshot.ToArray ()))
      throw new IOException ("Die INI wurde extern geändert. Nichts gespeichert; der bearbeitete Text bleibt im Dialog erhalten.");
    string working = Path + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
    string backup = Path + "." + DateTime.UtcNow.ToString ("yyyyMMdd-HHmmssfff") + "." + Guid.NewGuid ().ToString ("N")[..8] + ".bak";
    try
    {
      File.WriteAllBytes (working, bytes);
      if (!File.ReadAllBytes (working).AsSpan ().SequenceEqual (bytes)) throw new IOException ("Arbeitskopie konnte nicht verifiziert werden.");
      if (!File.ReadAllBytes (Path).AsSpan ().SequenceEqual (_original)) throw new IOException ("Die INI wurde während des Speicherns geändert.");
      File.Replace (working, Path, backup);
      return backup;
    }
    finally { if (File.Exists (working)) File.Delete (working); }
  }

  /// <summary>Validate a bounded replacement for either a live mission or an external definition.</summary>
  public static IniDocument ApplySection (IniDocument document, IniSection section, string text)
  {
    IniDocument replacement = IniDocument.ParseText (text);
    if (replacement.Sections.Count != 1 || !replacement.Sections[0].Is (section.Name)
        || replacement.LeadingLines.Any (line => line.Kind == IniLineKind.Entry))
      throw new InvalidDataException ($"Nur die bestehende Sektion [{section.Name}] darf bearbeitet werden.");
    IniDocument changed = document.ReplaceSection (section, text);
    if (!changed.IsBini)
    {
      var encoding = (Encoding)changed.Encoding.Clone ();
      encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
      try { _ = encoding.GetBytes (string.Concat (changed.Lines.Select (line => line.Text + line.LineEnding))); }
      catch (EncoderFallbackException error) { throw new InvalidDataException ("Der Text enthält Zeichen außerhalb der ursprünglichen Dateikodierung.", error); }
    }
    _ = changed.ToBytes ();
    return changed;
  }
}
