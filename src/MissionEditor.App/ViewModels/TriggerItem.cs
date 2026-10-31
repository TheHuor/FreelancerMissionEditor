using MissionEditor.Core.Checks;
using MissionEditor.Core.Graph;
using MissionEditor.Core.Model;

namespace MissionEditor.App.ViewModels;

/// <summary>A row of the trigger list.</summary>
public sealed class TriggerItem (TriggerNode node, IReadOnlyList<FindingItem> findings)
{
  /// <summary>The findings that belong to this trigger.</summary>
  public IReadOnlyList<FindingItem> Findings { get; } = findings;

  /// <summary>The most serious finding's severity, or null without findings.</summary>
  public FindingSeverity? MaxSeverity => Findings.Count > 0 ? Findings.Max (finding => finding.Severity) : null;

  /// <summary>"Error", "Warning", "Info" or "" – selects the badge and the row colour.</summary>
  public string BadgeKind => MaxSeverity?.ToString () ?? string.Empty;

  /// <summary>True when the most serious finding is an error.</summary>
  public bool HasError => MaxSeverity == FindingSeverity.Error;

  /// <summary>Tooltip of the warning sign: the messages.</summary>
  public string FindingsToolTip => string.Join ("\n", Findings.Select (finding => $"{finding.SeverityText}, Zeile {finding.LineNumber}: {finding.Message}"))
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
