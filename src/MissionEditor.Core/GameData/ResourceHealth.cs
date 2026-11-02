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
  /// <summary>The text is RDL/XML, but no supported RDL structure can be read from it.</summary>
  InvalidRdl,
  /// <summary>A verified literal display in the current mission receives RDL/XML.</summary>
  RenderingConflict,
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
    foreach (ResourceEntry entry in existing)
    {
      string variant = severalVariants ? $" ({entry.Identity!.Kind}, Sprache {entry.Identity.Language})" : "";
      if (entry.Status == ResourceStatus.DecodeError)
      {
        // The status text already names the concrete cause, e.g. an incomplete byte at the end.
        problems.Add (new (ResourceProblemKind.DecodeError, (severalVariants ? variant.Trim () + ": " : "") + entry.StatusText));
        continue;
      }
      if (RdlError (entry) is { } error)
        problems.Add (new (ResourceProblemKind.InvalidRdl, "RDL/XML nicht lesbar" + variant + ": " + error));
    }
    var uses = references.ToArray ();
    if (uses.Length > 0)
    {
      // Only the variant the game actually loads per language can conflict with a literal display.
      foreach (ResourceEntry entry in DisplayedVariants (existing))
      {
        string content = entry.RawText ?? entry.Text;
        foreach (string warning in uses.Select (reference => MissionTextUsage.Warning (reference, content)).OfType<string> ().Distinct ())
          problems.Add (new (ResourceProblemKind.RenderingConflict, warning + (severalVariants ? $" Sprache {entry.Identity!.Language}." : "")));
      }
    }
    return problems;
  }

  /// <summary>Per language the variant FmtStr loads: a string takes precedence over an infocard with the same ID.</summary>
  public static IEnumerable<ResourceEntry> DisplayedVariants (IEnumerable<ResourceEntry> variants) => variants.Where (entry => entry.HasRawResource)
      .GroupBy (entry => entry.Identity!.Language).Select (language => language.OrderBy (entry => entry.Identity!.Kind).First ());

  /// <summary>The parse error of RDL content, or null for plain text and readable RDL.</summary>
  public static string? RdlError (ResourceEntry entry)
  {
    string content = entry.RawText ?? entry.Text;
    if (entry.Identity?.Kind != ResourceKind.Infocard && !MissionTextUsage.ContainsRdl (content)) return null;
    try
    {
      InfocardDocument.Validate (content);
      return null;
    }
    catch (Exception error) when (error is InvalidDataException or XmlException or ArgumentException)
    {
      return error.Message;
    }
  }
}
