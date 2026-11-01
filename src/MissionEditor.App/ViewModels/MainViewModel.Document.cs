using System.IO;
using MissionEditor.Core.Checks;
using MissionEditor.Core.Graph;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.App.ViewModels;

// Document persistence, editing, undo and rebuilding the analyzed mission.
public sealed partial class MainViewModel
{
  /// <summary>The file being edited, or null.</summary>
  public string? FilePath
  {
    get => _filePath;
    private set
    {
      if (SetProperty (ref _filePath, value))
        OnPropertyChanged (nameof (WindowTitle));
    }
  }

  /// <summary>True when the document differs from the file on disk.</summary>
  public bool IsModified => Script is not null && Script.Document != _savedDocument;

  /// <summary>Undoes the last change.</summary>
  public RelayCommand UndoCommand
  {
    get;
  }

  /// <summary>Loads a mission script and runs the checks.</summary>
  /// <exception cref="IOException">The file cannot be read.</exception>
  /// <exception cref="InvalidDataException">The INI or BINI file is malformed or unsupported.</exception>
  public void Load (string path)
  {
    IniDocument document = IniDocument.Load (path);
    bool sameFile = string.Equals (path, FilePath, StringComparison.OrdinalIgnoreCase);
    if (!sameFile)
      _backupWritten = false;
    FilePath = path;
    _savedDocument = document;
    _undo.Clear ();
    Analyze (document, keepHistory: sameFile);
  }

  /// <summary>Writes the document to its file; the first save of a session keeps the original as "*.bak".</summary>
  public void Save ()
  {
    if (Script is null || FilePath is null)
      return;
    string backup = FilePath + ".bak";
    if (!_backupWritten && File.Exists (FilePath) && !File.Exists (backup))
      File.Copy (FilePath, backup);
    _backupWritten = true;
    Script.Document.Save (FilePath);
    MarkSaved ();
  }

  /// <summary>Writes the document to another file and continues editing that one.</summary>
  public void SaveAs (string path)
  {
    if (Script is null)
      return;
    Script.Document.Save (path);
    FilePath = path;
    _backupWritten = true;
    MarkSaved ();
    Analyze (Script.Document, keepHistory: true);
  }

  /// <summary>Deletes a section (with undo).</summary>
  public void DeleteSection (IniSection section)
  {
    if (Script is not null)
      ApplyChange (Script.Document.RemoveSection (section), $"Sektion ab Zeile {section.FirstLineNumber} gelöscht");
  }

  /// <summary>Replaces the text of a section (with undo).</summary>
  public void ReplaceSection (IniSection section, string newText)
  {
    if (Script is not null)
      ApplyChange (Script.Document.ReplaceSection (section, newText), $"Sektion ab Zeile {section.FirstLineNumber} geändert");
  }

  /// <summary>Replaces the whole document, e.g. with sections taken over from another file (with undo).</summary>
  public void ApplyDocument (IniDocument document, string description)
  {
    if (Script is not null)
      ApplyChange (document, description);
  }

  /// <summary>Explains what deleting a section changes in the game, for the confirmation.</summary>
  public string DescribeDeletion (IniSection section)
  {
    if (Script is null)
      return string.Empty;
    Trigger? trigger = Script.Triggers.FirstOrDefault (candidate => candidate.Section == section);
    if (trigger is not null)
    {
      if (trigger.IsShadowed)
        return "Die Engine hat diese Definition ohnehin ignoriert – am Spielverhalten ändert sich nichts.";
      Trigger? duplicate = Script.Triggers.FirstOrDefault (other => other.Id == trigger.Id && other.IsShadowed);
      if (duplicate is not null)
      {
        return section.HasSameEntries (duplicate.Section)
            ? $"Danach gilt die zweite Definition (Zeile {duplicate.LineNumber}). Sie ist inhaltlich gleich – am Spielverhalten ändert sich nichts."
            : $"Danach gilt die zweite Definition (Zeile {duplicate.LineNumber}). Sie unterscheidet sich von dieser – das Spielverhalten ändert sich!";
      }
      int references = _graph?.FindNode (trigger.Id)?.Incoming.Count ?? 0;
      return references > 0
          ? $"Achtung: {references} Verweise (ActTrig/DeactTrig/WatchTrigger/Save) zeigen auf '{trigger.Nickname}' und laufen danach ins Leere."
          : $"Auf '{trigger.Nickname}' verweist kein anderer Trigger.";
    }
    string nickname = section.Find ("nickname")?.GetValue (0) ?? string.Empty;
    IniSection? twin = Script.Document.GetSections (section.Name)
        .FirstOrDefault (other => other != section && string.Equals (other.Find ("nickname")?.GetValue (0), nickname, StringComparison.OrdinalIgnoreCase));
    if (twin is not null)
    {
      return section.HasSameEntries (twin)
          ? $"Eine gleichnamige, inhaltlich gleiche [{section.Name}] bleibt in Zeile {twin.FirstLineNumber} erhalten."
          : $"Eine gleichnamige [{section.Name}] bleibt in Zeile {twin.FirstLineNumber} erhalten; sie unterscheidet sich von dieser.";
    }
    return nickname.Length > 0 ? $"Verweise auf '{nickname}' finden diese Sektion danach nicht mehr." : string.Empty;
  }

  private void ApplyChange (IniDocument document, string description)
  {
    _undo.Push (Script!.Document);
    Analyze (document, keepHistory: true);
    StatusText = $"{description} (Strg+Z macht es rückgängig) · {StatusText}";
  }

  private void Undo ()
  {
    if (_undo.Count == 0)
      return;
    Analyze (_undo.Pop (), keepHistory: true);
    StatusText = $"Rückgängig gemacht · {StatusText}";
  }

  private void MarkSaved ()
  {
    _savedDocument = Script?.Document;
    OnPropertyChanged (nameof (IsModified));
    OnPropertyChanged (nameof (WindowTitle));
    StatusText = $"Gespeichert: {FilePath}";
  }

  /// <summary>Rebuilds the script, graph and findings, then restores the selection by nickname.</summary>
  /// <param name="document">The document to analyze and display.</param>
  /// <param name="keepHistory">Whether navigation history is remapped to nodes in the rebuilt graph.</param>
  /// <remarks>
  /// History stores graph nodes, so old entries must be resolved by ID before selecting a node.
  /// The selected row is cleared before rebuilding the collections to avoid recording a stale node.
  /// </remarks>
  private void Analyze (IniDocument document, bool keepHistory)
  {
    string? previousName = SelectedTrigger?.Name;
    MissionScript script = MissionScript.FromDocument (document, FilePath);
    var graph = new TriggerGraph (script);
    GameData = GameData.Locate (FilePath, _gameFolderSettings.GameFolder);
    ResourceReferences = MissionResourceIndex.Build (document, FilePath, GameData);
    OnPropertyChanged (nameof (ResourceReferences));
    OnPropertyChanged (nameof (GameDataText));
    IReadOnlyList<Finding> findings = MissionChecker.Run (script, GameData);

    List<uint> historyIds = keepHistory ? _history.Reverse ().Select (node => node.Id).ToList () : [];
    _history.Clear ();
    foreach (uint id in historyIds)
    {
      if (graph.FindNode (id) is { } node)
        _history.Push (node);
    }
    _selectedTrigger = null;
    Script = script;
    _graph = graph;
    OnPropertyChanged (nameof (IsModified));
    OnPropertyChanged (nameof (WindowTitle));

    Findings.Clear ();
    foreach (Finding finding in findings)
      Findings.Add (new FindingItem (finding, FindNodeForFinding (finding)));
    _findingsByLine = Findings.ToLookup (finding => finding.LineNumber);
    var findingsPerNode = Findings.Where (item => item.Node is not null).GroupBy (item => item.Node!)
        .ToDictionary (group => group.Key, group => (IReadOnlyList<FindingItem>)group.ToList ());

    Triggers.Clear ();
    foreach (TriggerNode node in graph.Nodes.OrderBy (node => node.Trigger?.LineNumber ?? int.MaxValue).ThenBy (node => node.Name))
      Triggers.Add (new TriggerItem (node, findingsPerNode.GetValueOrDefault (node) ?? []));

    int errors = findings.Count (finding => finding.Severity == FindingSeverity.Error);
    int warnings = findings.Count (finding => finding.Severity == FindingSeverity.Warning);
    StatusText = $"{(script.Document.IsBini ? "BINI · " : string.Empty)}{script.Document.Lines.Count} Zeilen · {script.Triggers.Count} Trigger · {script.Objects.Count} Objekte · {graph.Edges.Count} Verweise · {errors} Fehler, {warnings} Warnungen";

    TriggerItem? initial = (previousName is null ? null : Triggers.FirstOrDefault (item => item.Name.Equals (previousName, StringComparison.OrdinalIgnoreCase)))
        ?? Triggers.FirstOrDefault (item => item.IsInitiallyActive)
        ?? Triggers.FirstOrDefault ();
    SelectNode (initial?.Node, remember: false);
    DocumentContextChanged?.Invoke (this, EventArgs.Empty);
  }
}
