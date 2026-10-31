using MissionEditor.Core.Graph;
using MissionEditor.Core.Model;

namespace MissionEditor.App.ViewModels;

/// <summary>A link from the selected trigger to a neighbour, shown in the details.</summary>
public sealed class ReferenceItem (string relation, TriggerNode other, int lineNumber, string lineText)
{
  /// <summary>E.g. "aktiviert von".</summary>
  public string Relation { get; } = relation;

  /// <summary>The neighbour.</summary>
  public TriggerNode Other { get; } = other;

  /// <summary>The neighbour's name, marked if undefined.</summary>
  public string OtherName => Other.IsMissing ? $"? {Other.Name}" : Other.Name;

  /// <summary>Line of the referencing entry.</summary>
  public int LineNumber { get; } = lineNumber;

  /// <summary>Text of the referencing entry.</summary>
  public string LineText { get; } = lineText;

  /// <summary>Creates the rows for all edges of a node.</summary>
  public static IEnumerable<ReferenceItem> ForNode (TriggerNode node)
  {
    foreach (TriggerEdge edge in node.Incoming)
    {
      string relation = edge.Kind switch
      {
        TriggerReferenceKind.Activate => "aktiviert von",
        TriggerReferenceKind.Deactivate => "deaktiviert von",
        TriggerReferenceKind.Save => "Fortsetzung von Save in",
        _ => "beobachtet",
      };
      yield return new ReferenceItem (relation, edge.From, edge.Reference.Line.Number, edge.Reference.Line.Text.Trim ());
    }
    foreach (TriggerEdge edge in node.Outgoing)
    {
      string relation = edge.Kind switch
      {
        TriggerReferenceKind.Activate => "aktiviert",
        TriggerReferenceKind.Deactivate => "deaktiviert",
        TriggerReferenceKind.Save => "speichert, setzt fort bei",
        _ => "wird beobachtet von",
      };
      yield return new ReferenceItem (relation, edge.To, edge.Reference.Line.Number, edge.Reference.Line.Text.Trim ());
    }
  }
}
