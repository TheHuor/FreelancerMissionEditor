using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.App.ViewModels;

public sealed partial class MainViewModel
{
  /// <summary>Raised after a complete document analysis, including undo and installation changes.</summary>
  public event EventHandler? DocumentContextChanged;
  /// <summary>Current document snapshot; includes unsaved changes and indirect object references.</summary>
  public IReadOnlyList<IdsReference> ResourceReferences { get; private set; } = [];

  /// <summary>Refresh hints from the resource files after a successful resource save.</summary>
  public void RefreshResourceHints ()
  {
    int? focusedSectionLine = _focusSection?.FirstLineNumber;
    GameData?.Texts?.Reload ();
    if (Script is not null)
      Analyze (Script.Document, keepHistory: true);
    if (focusedSectionLine is { } number)
      ShowSourceLine (number);
  }

  /// <summary>Selects the exact section and returns the physical source row for usage navigation.</summary>
  public SectionLineItem? ShowSourceLine (int lineNumber)
  {
    IniSection? section = Script?.Document.FindSectionAt (lineNumber);
    if (section is null)
      return null;
    Trigger? trigger = Script!.Triggers.FirstOrDefault (item => item.Section == section);
    if (trigger is not null)
    {
      SelectByName (trigger.Nickname);
      if (_focusSection is not null)
        OnSelectionChanged ();
    }
    else
    {
      SelectNode (null);
      _focusSection = section;
      _focusFindings = section.Lines.SelectMany (line => _findingsByLine[line.Number]).ToArray ();
      SelectedSectionLines = BuildPreview ([( $"[{section.Name}] ab Zeile {section.FirstLineNumber}", section)]);
      NotifyDetailsChanged ();
    }
    return SelectedSectionLines.FirstOrDefault (line => line.Number == lineNumber);
  }
}
