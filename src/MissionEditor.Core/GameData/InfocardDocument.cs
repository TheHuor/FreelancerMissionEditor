using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MissionEditor.Core.Model;

/// <summary>Secure RDL validation and narrowly scoped text-segment editing without reserializing formatting.</summary>
public sealed partial class InfocardDocument
{
  private readonly string _raw;
  private readonly Match[] _segments;
  private static readonly HashSet<string> Supported = new (StringComparer.OrdinalIgnoreCase)
      { "RDL", "TEXT", "PARA", "PUSH", "POP", "TRA", "JUST", "STYLE" };

  private InfocardDocument (string raw, XDocument document)
  {
    _raw = raw;
    XElement[] texts = document.Descendants ().Where (element => element.Name.LocalName.Equals ("TEXT", StringComparison.OrdinalIgnoreCase)).ToArray ();
    _segments = TextElement ().Matches (raw).Cast<Match> ().ToArray ();
    // Regex replacement is safe only for plain paired TEXT elements with an unambiguous source span.
    // Complex/commented/CDATA input remains available in the validated full RDL/XML editor.
    CanEditSegments = !document.DescendantNodes ().Any (node => node is XComment or XCData or XProcessingInstruction)
        && texts.All (text => !text.HasElements) && texts.Length == _segments.Length
        && _segments.All (match => !match.Groups["value"].Value.Contains ("<!", StringComparison.Ordinal))
        && _segments.Select ((match, index) => DecodeSegment (match.Groups["value"].Value) == texts[index].Value).All (matches => matches);
    TextSegments = texts.Select (text => text.Value).ToArray ();
    var plain = new StringBuilder ();
    foreach (XElement element in document.Descendants ())
    {
      if (element.Name.LocalName.Equals ("TEXT", StringComparison.OrdinalIgnoreCase))
        plain.Append (element.Value);
      else if (element.Name.LocalName.Equals ("PARA", StringComparison.OrdinalIgnoreCase))
        plain.Append ('\n');
    }
    PlainText = plain.ToString ();
  }

  /// <summary>Whether every TEXT content can be replaced without touching any other source bytes.</summary>
  public bool CanEditSegments { get; }
  /// <summary>Decoded TEXT contents in document order.</summary>
  public IReadOnlyList<string> TextSegments { get; }
  /// <summary>Readable text with paragraph boundaries.</summary>
  public string PlainText { get; }

  /// <summary>Parse RDL, prohibiting DTDs, external entities and unsupported elements.</summary>
  public static InfocardDocument Parse (string raw)
  {
    if (raw.Contains ('\0'))
      throw new InvalidDataException ("Infocard enthält Nullzeichen.");
    using var input = new StringReader (raw);
    using XmlReader reader = XmlReader.Create (input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
    XDocument document = XDocument.Load (reader, LoadOptions.PreserveWhitespace);
    if (document.Root is not { } root || !root.Name.LocalName.Equals ("RDL", StringComparison.OrdinalIgnoreCase)
        || document.Descendants ().Any (element => element.Name.NamespaceName.Length > 0 || !Supported.Contains (element.Name.LocalName)))
      throw new InvalidDataException ("Keine unterstützte RDL-Struktur (RDL, TEXT, PARA, PUSH, POP, TRA, JUST, STYLE).");
    return new InfocardDocument (raw, document);
  }

  /// <summary>Throw if the source cannot safely be stored as Freelancer RDL.</summary>
  public static void Validate (string raw) => _ = Parse (raw);

  /// <summary>Change only TEXT contents; preserve all original tags, attributes and whitespace.</summary>
  public string WithTextSegments (IReadOnlyList<string> values)
  {
    if (!CanEditSegments || values.Count != _segments.Length)
      throw new InvalidOperationException ("Diese RDL-Struktur kann nur im RDL/XML-Modus bearbeitet werden.");
    var result = new StringBuilder (_raw);
    for (int index = _segments.Length - 1; index >= 0; index--)
    {
      if (values[index].Contains ('\0'))
        throw new InvalidDataException ("Text enthält Nullzeichen.");
      Group content = _segments[index].Groups["value"];
      result.Remove (content.Index, content.Length).Insert (content.Index, SecurityElement.Escape (values[index]));
    }
    string edited = result.ToString ();
    Validate (edited);
    return edited;
  }

  private static string? DecodeSegment (string source)
  {
    try { return XElement.Parse ("<TEXT>" + source + "</TEXT>", LoadOptions.PreserveWhitespace).Value; }
    catch (XmlException) { return null; }
  }

  [GeneratedRegex (@"<TEXT\b[^>]*>(?<value>.*?)</TEXT\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
  private static partial Regex TextElement ();
}
