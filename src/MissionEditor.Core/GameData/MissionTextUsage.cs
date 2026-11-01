namespace MissionEditor.Core.Model;

/// <summary>Verified rendering capability of a specific mission use, independent of DLL storage type.</summary>
public enum MissionTextRendering
{
  /// <summary>The end-to-end display path has not been verified.</summary>
  Unknown,
  /// <summary>The verified display expects literal text.</summary>
  PlainText,
  /// <summary>The verified display accepts RDL/XML and plain text.</summary>
  Rdl,
  /// <summary>The text can reach both rich and literal displays.</summary>
  Mixed,
}

/// <summary>Conservative rules traced in fl_decomp; see docs/mission-text-rendering.md.</summary>
public static class MissionTextUsage
{
  /// <summary>Classify a source-positioned use, never an ID number or its current contents.</summary>
  public static MissionTextRendering Classify(IdsReference reference)
  {
    string key = reference.SourceText.Split('=', 2)[0].Trim().ToLowerInvariant();
    return key switch
    {
      "act_popupdialog" when reference.ParameterIndex == 0 => MissionTextRendering.PlainText,
      "act_popupdialog" when reference.ParameterIndex == 1 => MissionTextRendering.Rdl,
      "act_setoffer" => MissionTextRendering.Rdl,
      "type" when reference.SectionName.Equals("NNObjective", StringComparison.OrdinalIgnoreCase) => MissionTextRendering.Mixed,
      // NNIds message types and indirect object/name uses require more endpoint tracing.
      _ => MissionTextRendering.Unknown,
    };
  }

  /// <summary>Human-readable rendering label.</summary>
  public static string Label(MissionTextRendering rendering) => rendering switch
  {
    MissionTextRendering.PlainText => "Klartext",
    MissionTextRendering.Rdl => "RDL-fähig",
    MissionTextRendering.Mixed => "Gemischt (Klartext/RDL)",
    _ => "Ungeprüft",
  };

  /// <summary>Combine known uses while keeping unknown endpoints visible.</summary>
  public static string Summary(IEnumerable<IdsReference> references)
  {
    var modes = references.Select(Classify).Distinct().ToArray();
    bool plain = modes.Contains(MissionTextRendering.PlainText), rich = modes.Contains(MissionTextRendering.Rdl);
    var combined = modes.Contains(MissionTextRendering.Mixed) || (plain && rich) ? MissionTextRendering.Mixed
        : plain ? MissionTextRendering.PlainText : rich ? MissionTextRendering.Rdl : MissionTextRendering.Unknown;
    return Label(combined) + (combined != MissionTextRendering.Unknown && modes.Contains(MissionTextRendering.Unknown) ? " · weitere Verwendungen ungeprüft" : "");
  }

  /// <summary>Recognize RDL roots, including XML declarations, without assuming storage or display compatibility.</summary>
  public static bool ContainsRdl(string text)
  {
    string start = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
    if (start.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
    {
      int end = start.IndexOf("?>", StringComparison.Ordinal);
      if (end < 0) return false;
      start = start[(end + 2)..].TrimStart();
    }
    return start.StartsWith("<RDL", StringComparison.OrdinalIgnoreCase) && start.Length > 4
        && (char.IsWhiteSpace(start[4]) || start[4] is '>' or '/');
  }

  /// <summary>Return a warning only when a known literal endpoint conflicts with RDL content.</summary>
  public static string? Warning(IdsReference reference, string content)
  {
    if (!ContainsRdl(content)) return null;
    return Classify(reference) switch
    {
      MissionTextRendering.PlainText => $"IDS {reference.Ids}: {reference.Role} wird als Klartext angezeigt, enthält aber RDL/XML. XML-Tags können im Spiel sichtbar werden.",
      MissionTextRendering.Mixed => $"IDS {reference.Ids}: {reference.Role} kann auch als Klartext im HUD erscheinen, enthält aber RDL/XML. Die formatierte Loganzeige allein garantiert keine korrekte HUD-Anzeige.",
      _ => null,
    };
  }
}
