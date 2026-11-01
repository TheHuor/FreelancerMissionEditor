using MissionEditor.Core.Model;

namespace MissionEditor.App.ViewModels;

/// <summary>An individually navigable reference; display text is never parsed back into an ID.</summary>
public sealed record ResourceLinkItem (IdsReference Reference, ResourceEntry? Resource)
{
  /// <summary>Global resource ID.</summary>
  public int Ids => Reference.Ids;
  /// <summary>Compact role choice in the inspector.</summary>
  public string ContextLabel => $"{Reference.Role} · {Ids} · {MissionTextUsage.Label(Reference.Rendering)}";
  /// <summary>Single origin-definition editing action.</summary>
  public string DefinitionLabel => "Definition bearbeiten · " + System.IO.Path.GetFileName (Reference.DefinitionPath);
  /// <summary>Actual lookup outcome, also available on missing links.</summary>
  public bool IsResolved => Resource?.IsResolved == true;
  /// <summary>Short bounded-width hint below the source line.</summary>
  public string Label => $"{Reference.Role} · IDS {Ids} · {(IsResolved ? ResourcePresentation.Preview(ResourcePresentation.Source(Resource!)).Replace ("\r", "").Replace ("\n", " ¶ ")
      : Resource?.Status == ResourceStatus.DecodeError ? "Textkodierung fehlerhaft · RDL/XML korrigieren" : "nicht gefunden")}";
  /// <summary>Resolution and provenance explanation.</summary>
  public string ToolTip => $"{Reference.Role}: {Resource?.StatusText ?? "Spielressourcen nicht geladen"}\n{Resource?.Identity?.DllPath}\n{Reference.Provenance}".Trim ();
}
