using MissionEditor.Core.Model;

namespace MissionEditor.App.ViewModels;

/// <summary>An individually navigable reference; display text is never parsed back into an ID.</summary>
public sealed record ResourceLinkItem (IdsReference Reference, ResourceEntry? Resource)
{
  /// <summary>Global resource ID.</summary>
  public int Ids => Reference.Ids;
  /// <summary>Actual lookup outcome, also available on missing links.</summary>
  public bool IsResolved => Resource?.IsResolved == true;
  /// <summary>Short bounded-width hint below the source line.</summary>
  public string Label => $"{Reference.Role} · IDS {Ids} · {(IsResolved ? Resource!.Text.Replace ("\r", "").Replace ("\n", " ¶ ") : "nicht gefunden")}";
  /// <summary>Resolution and provenance explanation.</summary>
  public string ToolTip => $"{Reference.Role}: {Resource?.StatusText ?? "Spielressourcen nicht geladen"}\n{Resource?.Identity?.DllPath}\n{Reference.Provenance}".Trim ();
}
