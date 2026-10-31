using MissionEditor.Core.Checks;
using MissionEditor.Core.Graph;
using MissionEditor.Core.Model;

namespace MissionEditor.App.ViewModels;

/// <summary>A row of the trigger list.</summary>
public sealed class TriggerItem(TriggerNode node, IReadOnlyList<FindingItem> findings)
{
    /// <summary>The findings that belong to this trigger.</summary>
    public IReadOnlyList<FindingItem> Findings { get; } = findings;

    /// <summary>The most serious finding's severity, or null without findings.</summary>
    public FindingSeverity? MaxSeverity => Findings.Count > 0 ? Findings.Max(finding => finding.Severity) : null;

    /// <summary>True when the most serious finding is an error.</summary>
    public bool HasError => MaxSeverity == FindingSeverity.Error;

    /// <summary>Tooltip of the warning sign: the messages.</summary>
    public string FindingsToolTip => string.Join("\n", Findings.Select(finding => $"{finding.SeverityText}, Zeile {finding.LineNumber}: {finding.Message}"))
        + "\n\nKlicken: zum Fund in der Prüfliste springen";

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
    public int FindingCount => Findings.Count;

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

    /// <summary>Explanation and fix, empty if none.</summary>
    public string Hint => Finding.Hint ?? string.Empty;

    /// <summary>True when there is a hint.</summary>
    public bool HasHint => Finding.Hint is not null;

    /// <summary>Message and hint for tooltips.</summary>
    public string FullText => HasHint ? $"{Message}\n\n{Hint}" : Message;
}

/// <summary>One line of the section preview, marked when a finding points at it.</summary>
public sealed class SectionLineItem(int number, string text, IReadOnlyList<FindingItem> findings)
{
    /// <summary>Line number in the file (0 for a synthetic note).</summary>
    public int Number { get; } = number;

    /// <summary>Line number as shown, empty for a note.</summary>
    public string NumberText => Number > 0 ? Number.ToString() : string.Empty;

    /// <summary>The line text.</summary>
    public string Text { get; } = text;

    /// <summary>Findings on this line.</summary>
    public IReadOnlyList<FindingItem> Findings { get; } = findings;

    /// <summary>True when a finding points at this line.</summary>
    public bool HasFindings => Findings.Count > 0;

    /// <summary>True when one of the findings is an error.</summary>
    public bool HasError => Findings.Any(finding => finding.Severity == FindingSeverity.Error);

    /// <summary>Tooltip: the findings with their hints, or null.</summary>
    public string? ToolTip => HasFindings
        ? string.Join("\n\n", Findings.Select(finding => $"{finding.SeverityText}: {finding.FullText}"))
        : null;
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
