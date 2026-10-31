using MissionEditor.Core.Checks;
using MissionEditor.Core.Graph;

namespace MissionEditor.App.ViewModels;

/// <summary>A row of the findings list.</summary>
public sealed class FindingItem (Finding finding, TriggerNode? node)
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

  /// <summary>"Error", "Warning" or "Info" – selects the badge.</summary>
  public string BadgeKind => Severity.ToString ();

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
