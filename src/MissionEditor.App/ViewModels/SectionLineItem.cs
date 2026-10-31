using MissionEditor.Core.Checks;
using MissionEditor.Core.Ini;

namespace MissionEditor.App.ViewModels;

/// <summary>One line of the section preview, marked when a finding points at it.</summary>
public sealed class SectionLineItem (int number, string text, IReadOnlyList<FindingItem> findings, bool isCaption = false, IniSection? section = null, string? note = null)
{
  /// <summary>The text behind the line's IDS numbers, or null.</summary>
  public string? Note { get; } = note;

  /// <summary>True when there is a note to show.</summary>
  public bool HasNote => Note is not null;

  /// <summary>The note as shown after the line.</summary>
  public string NoteText => Note is null ? string.Empty : "→ " + Note;

  /// <summary>For a caption: the section below it, which the buttons edit or delete.</summary>
  public IniSection? Section { get; } = section;

  /// <summary>True when the caption offers edit and delete.</summary>
  public bool HasActions => Section is not null;

  /// <summary>True for a caption between two sections (not a file line).</summary>
  public bool IsCaption { get; } = isCaption;

  /// <summary>Line number in the file (0 for a synthetic note).</summary>
  public int Number { get; } = number;

  /// <summary>Line number as shown, empty for a note.</summary>
  public string NumberText => Number > 0 ? Number.ToString () : string.Empty;

  /// <summary>The line text.</summary>
  public string Text { get; } = text;

  /// <summary>Findings on this line.</summary>
  public IReadOnlyList<FindingItem> Findings { get; } = findings;

  /// <summary>True when a finding points at this line.</summary>
  public bool HasFindings => Findings.Count > 0;

  /// <summary>"Error", "Warning", "Info" or "" – selects the badge and the row colour.</summary>
  public string BadgeKind => HasFindings ? Findings.Max (finding => finding.Severity).ToString () : string.Empty;

  /// <summary>True when one of the findings is an error.</summary>
  public bool HasError => Findings.Any (finding => finding.Severity == FindingSeverity.Error);

  /// <summary>Tooltip: the findings with their hints, or null.</summary>
  public string? ToolTip => HasFindings
      ? string.Join ("\n\n", Findings.Select (finding => $"{finding.SeverityText}: {finding.FullText}"))
      : null;
}
