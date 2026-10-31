using MissionEditor.Core.Checks;
using MissionEditor.Core.Graph;
using MissionEditor.Core.Model;

namespace MissionEditor.App.ViewModels;

/// <summary>A row of the trigger list.</summary>
public sealed class TriggerItem(TriggerNode node, int findingCount)
{
    /// <summary>The graph node.</summary>
    public TriggerNode Node { get; } = node;

    /// <summary>The nickname.</summary>
    public string Name => Node.Name;

    /// <summary>Second line in the list: condition or "not defined".</summary>
    public string Detail => Node.Trigger is { } trigger
        ? $"{trigger.ConditionText}   · Z. {trigger.LineNumber}"
        : "nicht definiert";

    /// <summary>Line of the section, or int.MaxValue for undefined triggers (sorted last).</summary>
    public int LineNumber => Node.Trigger?.LineNumber ?? int.MaxValue;

    /// <summary>True when InitState = ACTIVE.</summary>
    public bool IsInitiallyActive => Node.Trigger?.InitialState == TriggerState.Active;

    /// <summary>True for a referenced but undefined nickname.</summary>
    public bool IsMissing => Node.IsMissing;

    /// <summary>Number of findings for this trigger.</summary>
    public int FindingCount { get; } = findingCount;

    /// <summary>True when there are findings.</summary>
    public bool HasFindings => FindingCount > 0;
}

/// <summary>A row of the findings list.</summary>
public sealed class FindingItem(Finding finding, TriggerNode? node)
{
    /// <summary>The finding.</summary>
    public Finding Finding { get; } = finding;

    /// <summary>The trigger the finding belongs to, if any.</summary>
    public TriggerNode? Node { get; } = node;

    /// <summary>Severity in German.</summary>
    public string SeverityText => Finding.Severity switch
    {
        FindingSeverity.Error => "Fehler",
        FindingSeverity.Warning => "Warnung",
        _ => "Hinweis",
    };

    /// <summary>The severity.</summary>
    public FindingSeverity Severity => Finding.Severity;

    /// <summary>The line.</summary>
    public int LineNumber => Finding.LineNumber;

    /// <summary>The check id.</summary>
    public string CheckId => Finding.CheckId;

    /// <summary>The trigger or object nickname.</summary>
    public string Subject => Finding.Subject ?? string.Empty;

    /// <summary>The message.</summary>
    public string Message => Finding.Message;
}

/// <summary>A link from the selected trigger to a neighbour, shown in the details.</summary>
public sealed class ReferenceItem(string relation, TriggerNode other, int lineNumber, string lineText)
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
    public static IEnumerable<ReferenceItem> ForNode(TriggerNode node)
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
            yield return new ReferenceItem(relation, edge.From, edge.Reference.Line.Number, edge.Reference.Line.Text.Trim());
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
            yield return new ReferenceItem(relation, edge.To, edge.Reference.Line.Number, edge.Reference.Line.Text.Trim());
        }
    }
}
