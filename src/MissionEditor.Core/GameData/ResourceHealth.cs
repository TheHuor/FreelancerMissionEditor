using System.IO;
using System.Xml;

namespace MissionEditor.Core.Model;

/// <summary>Kinds of resource problems shown in the resource browser, ordered by severity.</summary>
public enum ResourceProblemKind
{
  /// <summary>The registered DLL is missing or cannot be read.</summary>
  DllUnavailable,
  /// <summary>The IDS has no entry in its DLL, or is not a valid IDS.</summary>
  EntryMissing,
  /// <summary>The infocard's encoded text is malformed; the original bytes remain available.</summary>
  DecodeError,
  /// <summary>The text starts as RDL/XML, but no supported RDL structure can be read from it. Plain text is not affected.</summary>
  InvalidRdl,
  /// <summary>A verified literal display in the current mission receives RDL/XML.</summary>
  RenderingConflict,
}

/// <summary>Which resource the game loads first when a string and an infocard share an ID.</summary>
public enum ResourceLookup
{
  /// <summary>
  /// FmtStr and the mission texts built from it: the string table entry, the infocard only without one
  /// (fl_decomp ids_strings.cpp, GetIdsStringWWithRawFallback).
  /// </summary>
  StringFirst,
  /// <summary>
  /// Infocard displays such as ids_info: the RT_HTML infocard, the string table entry only without one
  /// (fl_decomp ids_rdl.cpp, LoadIdsIntoRdl).
  /// </summary>
  InfocardFirst,
}

/// <summary>One reason why a resource looks broken, with the affected language variant when known.</summary>
public sealed record ResourceProblem (ResourceProblemKind Kind, string Reason);

/// <summary>Classify resources as broken or fine, reusing the editor's resolution, RDL and rendering rules.</summary>
public static class ResourceHealth
{
  /// <summary>Short label for filters and markers.</summary>
  public static string Label (ResourceProblemKind kind) => kind switch
  {
    ResourceProblemKind.DllUnavailable => "DLL fehlt oder nicht lesbar",
    ResourceProblemKind.EntryMissing => "Text fehlt",
    ResourceProblemKind.DecodeError => "Kodierung fehlerhaft",
    ResourceProblemKind.InvalidRdl => "RDL/XML nicht lesbar",
    _ => "Unpassend für die Spielanzeige",
  };

  /// <summary>
  /// Problems of one IDS. <paramref name="preferred"/> is the string-first lookup result,
  /// <paramref name="variants"/> all actual language/type variants, <paramref name="references"/> the uses in the current mission.
  /// Without a loaded installation nothing is reported: missing data is then not a defect of the resource.
  /// </summary>
  public static IReadOnlyList<ResourceProblem> Assess (ResourceEntry preferred, IReadOnlyList<ResourceEntry> variants, IEnumerable<IdsReference> references)
  {
    var problems = new List<ResourceProblem> ();
    switch (preferred.Status)
    {
      case ResourceStatus.SlotMissing or ResourceStatus.DllMissing or ResourceStatus.DllUnreadable:
        problems.Add (new (ResourceProblemKind.DllUnavailable, preferred.StatusText));
        return problems;
      case ResourceStatus.EntryMissing or ResourceStatus.InvalidId:
        problems.Add (new (ResourceProblemKind.EntryMissing, preferred.StatusText));
        return problems;
      case ResourceStatus.NoInstallation:
        return problems;
    }
    var existing = variants.Where (entry => entry.HasRawResource).ToArray ();
    if (existing.Length == 0 && preferred.HasRawResource) existing = [preferred];
    bool severalVariants = existing.Length > 1;
    var shownInInfocards = DisplayedVariants (existing, ResourceLookup.InfocardFirst).ToHashSet ();
    foreach (ResourceEntry entry in existing)
    {
      string variant = severalVariants ? $" ({entry.Identity!.Kind}, Sprache {entry.Identity.Language})" : "";
      if (entry.Status == ResourceStatus.DecodeError)
      {
        // The status text already names the concrete cause, e.g. an incomplete byte at the end.
        problems.Add (new (ResourceProblemKind.DecodeError, (severalVariants ? variant.Trim () + ": " : "") + entry.StatusText));
        continue;
      }
      if (RdlError (entry, !shownInInfocards.Contains (entry)) is { } error)
        problems.Add (new (ResourceProblemKind.InvalidRdl, "RDL/XML nicht lesbar" + variant + ": " + error));
    }
    var uses = references.ToArray ();
    if (uses.Length > 0)
    {
      // Only the variant the game actually loads per language can conflict with a literal display.
      foreach (ResourceEntry entry in DisplayedVariants (existing, ResourceLookup.StringFirst))
      {
        string content = entry.RawText ?? entry.Text;
        foreach (string warning in uses.Select (reference => MissionTextUsage.Warning (reference, content)).OfType<string> ().Distinct ())
          problems.Add (new (ResourceProblemKind.RenderingConflict, warning + (severalVariants ? $" Sprache {entry.Identity!.Language}." : "")));
      }
    }
    return problems;
  }

  /// <summary>
  /// Per language the variant a display path loads. Mission texts go through FmtStr, where a string takes precedence
  /// over an infocard with the same ID; infocard displays such as ids_info prefer the infocard.
  /// </summary>
  public static IEnumerable<ResourceEntry> DisplayedVariants (IEnumerable<ResourceEntry> variants, ResourceLookup lookup)
      => variants.Where (entry => entry.HasRawResource).GroupBy (entry => entry.Identity!.Language)
          .Select (language => (lookup == ResourceLookup.StringFirst
              ? language.OrderBy (entry => entry.Identity!.Kind)
              : language.OrderByDescending (entry => entry.Identity!.Kind)).First ());

  /// <summary>
  /// The parse error of RDL content with its effect in the game and a concrete remedy, or null for plain text and readable RDL.
  /// <paramref name="hiddenByInfocard"/> tells that infocard displays load an infocard with the same ID instead of this string.
  /// </summary>
  public static string? RdlError (ResourceEntry entry, bool hiddenByInfocard = false)
  {
    string content = entry.RawText ?? entry.Text;
    // The game tries the XML reader and shows text that is not XML as plain text with line breaks
    // (fl_decomp ids_rdl.cpp, LoadIdsIntoRdl -> AppendTextAsRdl). Such an infocard is valid, it just has no formatting.
    if (!content.TrimStart ('﻿', ' ', '\r', '\n', '\t').StartsWith ('<')) return null;
    if (entry.Identity?.Kind != ResourceKind.Infocard && !MissionTextUsage.ContainsRdl (content)) return null;
    try
    {
      InfocardDocument.Validate (content);
      return null;
    }
    catch (XmlException error)
    {
      // XMLReader::read_buffer succeeds only with S_OK, but its SAX handler has already added the nodes before the
      // error (fl_decomp common_rdlio.cpp). LoadIdsIntoRdl then appends the whole raw text as plain text on top.
      return error.Message + " Im Spiel erscheint der Anfang formatiert, danach folgt der gesamte Text noch einmal als Klartext mit allen Tags. "
          + Remedy (entry, content, hiddenByInfocard);
    }
    catch (Exception error) when (error is InvalidDataException or ArgumentException)
    {
      return error.Message;
    }
  }

  private static string Remedy (ResourceEntry entry, string content, bool hiddenByInfocard)
  {
    if (MissingClosingTags (content) is not { } missing)
      return "Abhilfe: das RDL an der genannten Stelle im RDL/XML-Reiter reparieren.";
    if (entry.Identity?.Kind != ResourceKind.String)
      return $"Der Text ist abgeschnitten. Abhilfe: am Ende {missing} ergänzen.";
    string truncated = $"Der String ({content.Length} Zeichen) endet mitten im Text, es fehlen {missing}; er ist vermutlich an eine Längengrenze gestoßen. ";
    return truncated + (hiddenByInfocard
        ? "Infocard-Anzeigen wie ids_info zeigen bereits die Infocard mit derselben ID; dieser String erscheint nur dort, wo zuerst Strings geladen werden, etwa in Missionstexten. Abhilfe: dort eine eigene IDS mit kurzem Text verwenden oder den String kürzen und schließen."
        : "Abhilfe: eine Infocard mit derselben ID und dem vollständigen RDL anlegen; Infocard-Anzeigen wie ids_info laden sie vor dem String. Missionstexte laden weiterhin zuerst den String.");
  }

  /// <summary>The closing tags that make truncated RDL readable, or null when the error is not a missing end.</summary>
  private static string? MissingClosingTags (string content)
  {
    var open = new Stack<string> ();
    try
    {
      using var input = new StringReader (content);
      using XmlReader reader = XmlReader.Create (input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
      while (reader.Read ())
      {
        if (reader.NodeType == XmlNodeType.Element && !reader.IsEmptyElement) open.Push (reader.Name);
        else if (reader.NodeType == XmlNodeType.EndElement && open.Count > 0) open.Pop ();
      }
    }
    catch (XmlException)
    {
      // Expected: the reader stops at the missing end with the still open elements on the stack.
    }
    if (open.Count == 0) return null;
    string closing = string.Concat (open.Select (name => $"</{name}>"));
    try
    {
      InfocardDocument.Validate (content + closing);
      return closing;
    }
    catch (Exception error) when (error is InvalidDataException or XmlException or ArgumentException)
    {
      return null;
    }
  }
}
