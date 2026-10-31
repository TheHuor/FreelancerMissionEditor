namespace MissionEditor.App.ViewModels;

/// <summary>A finding in the details pane; the hint is shown only when it differs from the row above.</summary>
public sealed class FindingRow (FindingItem item, bool showHint, bool isCurrent)
{
  /// <summary>True for the finding the user navigated to (shown with a blue frame).</summary>
  public bool IsCurrent { get; } = isCurrent;

  /// <summary>The finding.</summary>
  public FindingItem Item { get; } = item;

  /// <summary>True when this row shows the hint.</summary>
  public bool ShowHint { get; } = showHint && item.HasHint;

  /// <summary>Severity in German.</summary>
  public string SeverityText => Item.SeverityText;

  /// <summary>Selects the badge.</summary>
  public string BadgeKind => Item.BadgeKind;

  /// <summary>The line.</summary>
  public int LineNumber => Item.LineNumber;

  /// <summary>The message.</summary>
  public string Message => Item.Message;

  /// <summary>Explanation and fix.</summary>
  public string Hint => Item.Hint;

  /// <summary>Tooltip: message and hint (also for rows that do not show the hint).</summary>
  public string FullText => Item.FullText;
}
