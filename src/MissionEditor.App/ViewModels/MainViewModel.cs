using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using MissionEditor.Core.Checks;
using MissionEditor.Core.Graph;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

namespace MissionEditor.App.ViewModels;

/// <summary>State of the main window: the loaded script, the trigger list, the selection and the graph.</summary>
public sealed class MainViewModel : ObservableObject
{
  private readonly Stack<TriggerNode> _history = new ();
  private MissionScript? _script;
  private TriggerGraph? _graph;
  private TriggerItem? _selectedTrigger;
  private GraphLayout? _layout;
  private string _filterText = string.Empty;
  private string _statusText = "Keine Datei geladen.";
  private int _depthBackward = 2;
  private int _depthForward = 2;
  private bool _showActivate = true;
  private bool _showDeactivate;
  private bool _showWatch = true;
  private bool _showSave = true;
  private FindingSeverity _minimumSeverity = FindingSeverity.Warning;
  private bool _navigating;
  private bool _onlyWithFindings;
  private IReadOnlyList<SectionLineItem> _sectionLines = [];
  private readonly Stack<IniDocument> _undo = new ();
  private IniDocument? _savedDocument;
  private string? _filePath;
  private bool _backupWritten;
  private FindingItem? _currentFinding;
  private bool _findingsExpanded = true;
  private IniSection? _focusSection;
  private IReadOnlyList<FindingItem> _focusFindings = [];
  private ILookup<int, FindingItem> _findingsByLine = Array.Empty<FindingItem> ().ToLookup (finding => finding.LineNumber);
  private GameFolderSettings _gameFolderSettings = GameFolderSettings.Load ();

  /// <summary>Creates the view model.</summary>
  public MainViewModel ()
  {
    TriggersView = CollectionViewSource.GetDefaultView (Triggers);
    TriggersView.Filter = item => FilterTrigger ((TriggerItem)item);
    FindingsView = CollectionViewSource.GetDefaultView (Findings);
    FindingsView.Filter = item => ((FindingItem)item).Severity >= MinimumSeverity;
    BackCommand = new RelayCommand (GoBack, () => _history.Count > 0);
    UndoCommand = new RelayCommand (Undo, () => _undo.Count > 0);
  }

  /// <summary>The loaded script, or null.</summary>
  public MissionScript? Script
  {
    get => _script;
    private set
    {
      if (SetProperty (ref _script, value))
        OnPropertyChanged (nameof (WindowTitle));
    }
  }

  /// <summary>Title bar text.</summary>
  public string WindowTitle => FilePath is { } path
      ? $"{Path.GetFileName (path)}{(IsModified ? " *" : string.Empty)} – Mission Editor {AboutWindow.DisplayVersion}"
      : $"Mission Editor {AboutWindow.DisplayVersion}";

  /// <summary>All trigger rows.</summary>
  public ObservableCollection<TriggerItem> Triggers { get; } = [];

  /// <summary>The filtered trigger list.</summary>
  public ICollectionView TriggersView
  {
    get;
  }

  /// <summary>All findings.</summary>
  public ObservableCollection<FindingItem> Findings { get; } = [];

  /// <summary>The findings at or above <see cref="MinimumSeverity"/>.</summary>
  public ICollectionView FindingsView
  {
    get;
  }

  /// <summary>The references of the selected trigger.</summary>
  public ObservableCollection<ReferenceItem> References { get; } = [];

  /// <summary>Goes back to the previously selected trigger.</summary>
  public RelayCommand BackCommand
  {
    get;
  }

  /// <summary>Text filter for the trigger list (part of the nickname).</summary>
  public string FilterText
  {
    get => _filterText;
    set
    {
      if (SetProperty (ref _filterText, value))
        TriggersView.Refresh ();
    }
  }

  /// <summary>Text for the status bar.</summary>
  public string StatusText
  {
    get => _statusText;
    private set => SetProperty (ref _statusText, value);
  }

  /// <summary>Shows a message from the window (e.g. "copied") in the status bar.</summary>
  public void ReportStatus (string message) => StatusText = message;

  /// <summary>Which installation provides the game data (automatic or a chosen folder).</summary>
  public GameFolderSettings GameFolderSettings => _gameFolderSettings;

  /// <summary>The game data used for the open mission (nickname checks, texts behind IDS numbers), or null.</summary>
  public GameData? GameData
  {
    get; private set;
  }

  /// <summary>The game data in use, for the status bar.</summary>
  public string GameDataText =>
    GameData is { } gameData
      ? $"Spieldaten: {gameData.Summary}{(_gameFolderSettings.GameFolder.Length > 0 ? " · fest eingestellt" : " · automatisch")}"
      : _gameFolderSettings.GameFolder.Length > 0
        ? $"Spieldaten: {_gameFolderSettings.GameFolder} ist keine Freelancer-Installation"
        : Script is null ? "Spieldaten: automatisch" : "Spieldaten: keine Installation gefunden";

  /// <summary>Uses another game folder setting and checks the open mission again.</summary>
  public void ApplyGameFolder (GameFolderSettings settings)
  {
    _gameFolderSettings = settings;
    if (Script is not null)
      Analyze (Script.Document, keepHistory: true);
    else
      OnPropertyChanged (nameof (GameDataText));
  }

  /// <summary>The selected trigger row.</summary>
  public TriggerItem? SelectedTrigger
  {
    get => _selectedTrigger;
    set
    {
      TriggerItem? previous = _selectedTrigger;
      if (!SetProperty (ref _selectedTrigger, value))
        return;
      if (!_navigating && previous is not null)
        _history.Push (previous.Node);
      OnSelectionChanged ();
    }
  }

  /// <summary>
  /// The source preview with findings marked: the selected trigger's section plus any duplicate
  /// definitions, or the sections of a finding that has no trigger (see <see cref="ShowFinding"/>).
  /// </summary>
  public IReadOnlyList<SectionLineItem> SelectedSectionLines
  {
    get => _sectionLines;
    private set => SetProperty (ref _sectionLines, value);
  }

  /// <summary>
  /// Shows a finding: selects its trigger, or puts its section (and the related one, e.g. the first
  /// definition of a duplicate) into the preview. Returns the preview line to scroll to.
  /// </summary>
  public SectionLineItem? ShowFinding (FindingItem finding)
  {
    if (finding.Node is not null)
    {
      if (finding.Node != SelectedTrigger?.Node)
        SelectNode (finding.Node);
      else if (_focusSection is not null)
        OnSelectionChanged ();
      FindingsExpanded = true;
      CurrentFinding = finding;
    }
    else if (Script is not null && Script.Document.FindSectionAt (finding.LineNumber) is { } section)
    {
      // Not a trigger: the details show the section itself; the trigger selection goes away.
      SelectNode (null);
      var parts = new List<(string? Caption, IniSection Section)> { ($"Fundstelle [{section.Name}], Zeile {finding.LineNumber}", section) };
      if (finding.Finding.RelatedLineNumber is int related && Script.Document.FindSectionAt (related) is { } relatedSection && relatedSection != section)
        parts.Add (($"Bezug: Zeile {related}", relatedSection));
      _focusSection = section;
      _focusFindings = parts.SelectMany (part => new[] { part.Section.Header }.Concat (part.Section.Lines))
          .SelectMany (line => _findingsByLine[line.Number]).Distinct ().ToList ();
      SelectedSectionLines = BuildPreview (parts);
      FindingsExpanded = true;
      _currentFinding = finding;
      NotifyDetailsChanged ();
    }
    return SelectedSectionLines.FirstOrDefault (line => line.Number == finding.LineNumber);
  }

  /// <summary>Title of the details pane: the trigger name or the shown section.</summary>
  public string DetailsTitle => SelectedTrigger?.Name
      ?? (_focusSection is { } section ? $"[{section.Name}] {section.Find ("nickname")?.GetValue (0)}".TrimEnd () : string.Empty);

  /// <summary>Header text of the details pane.</summary>
  public string SelectedSummary
  {
    get
    {
      if (SelectedTrigger is null && _focusSection is { } section)
        return $"Zeile {section.FirstLineNumber}  ·  keine Trigger-Sektion – Graph und Verweise gibt es nur für Trigger";
      Trigger? trigger = SelectedTrigger?.Node.Trigger;
      if (trigger is null)
        return SelectedTrigger is null ? string.Empty : "nicht definiert";
      var parts = new List<string> { $"Zeile {trigger.LineNumber}", $"ID {trigger.Id}" };
      if (trigger.InitialState != TriggerState.Inactive)
        parts.Add ($"InitState = {trigger.InitialState.ToString ().ToUpperInvariant ()}");
      if (trigger.Repeatable)
        parts.Add ("repeatable");
      if (trigger.System is not null)
        parts.Add ($"System {trigger.System}");
      return string.Join ("  ·  ", parts);
    }
  }

  /// <summary>The layout of the neighbourhood of the selected trigger.</summary>
  public GraphLayout? Layout
  {
    get => _layout;
    private set
    {
      if (SetProperty (ref _layout, value))
        OnPropertyChanged (nameof (HiddenNodeText));
    }
  }

  /// <summary>Note about nodes left out of the graph, empty if none.</summary>
  public string HiddenNodeText => Layout is { HiddenNodeCount: > 0 } layout
      ? $"{layout.HiddenNodeCount} Knoten ausgelassen (Ebene voll)"
      : string.Empty;

  /// <summary>Steps to follow backwards in the graph.</summary>
  public int DepthBackward
  {
    get => _depthBackward;
    set
    {
      if (SetProperty (ref _depthBackward, Math.Clamp (value, 0, 10)))
        UpdateLayout ();
    }
  }

  /// <summary>Steps to follow forwards in the graph.</summary>
  public int DepthForward
  {
    get => _depthForward;
    set
    {
      if (SetProperty (ref _depthForward, Math.Clamp (value, 0, 10)))
        UpdateLayout ();
    }
  }

  /// <summary>Show Act_ActTrig edges.</summary>
  public bool ShowActivate
  {
    get => _showActivate;
    set
    {
      if (SetProperty (ref _showActivate, value))
        UpdateLayout ();
    }
  }

  /// <summary>Show Act_DeactTrig edges.</summary>
  public bool ShowDeactivate
  {
    get => _showDeactivate;
    set
    {
      if (SetProperty (ref _showDeactivate, value))
        UpdateLayout ();
    }
  }

  /// <summary>Show Cnd_WatchTrigger edges.</summary>
  public bool ShowWatch
  {
    get => _showWatch;
    set
    {
      if (SetProperty (ref _showWatch, value))
        UpdateLayout ();
    }
  }

  /// <summary>Show Act_Save edges.</summary>
  public bool ShowSave
  {
    get => _showSave;
    set
    {
      if (SetProperty (ref _showSave, value))
        UpdateLayout ();
    }
  }

  /// <summary>Lowest severity shown in the findings list.</summary>
  public FindingSeverity MinimumSeverity
  {
    get => _minimumSeverity;
    set
    {
      if (SetProperty (ref _minimumSeverity, value))
      {
        FindingsView.Refresh ();
        if (OnlyWithFindings)
          TriggersView.Refresh ();
      }
    }
  }

  /// <summary>Shows only triggers with findings at or above <see cref="MinimumSeverity"/>.</summary>
  public bool OnlyWithFindings
  {
    get => _onlyWithFindings;
    set
    {
      if (SetProperty (ref _onlyWithFindings, value))
        TriggersView.Refresh ();
    }
  }

  /// <summary>The findings of the selected trigger.</summary>
  public IReadOnlyList<FindingRow> SelectedFindings
  {
    get
    {
      var rows = new List<FindingRow> ();
      string? previousHint = null;
      foreach (FindingItem finding in DetailFindingsSource)
      {
        rows.Add (new FindingRow (finding, finding.Hint != previousHint, finding == CurrentFinding));
        previousHint = finding.Hint;
      }
      return rows;
    }
  }

  /// <summary>The finding the user navigated to; highlighted in the details.</summary>
  public FindingItem? CurrentFinding
  {
    get => _currentFinding;
    set
    {
      if (SetProperty (ref _currentFinding, value))
        OnPropertyChanged (nameof (SelectedFindings));
    }
  }

  /// <summary>Whether the findings area in the details is open.</summary>
  public bool FindingsExpanded
  {
    get => _findingsExpanded;
    set => SetProperty (ref _findingsExpanded, value);
  }

  /// <summary>Header of the findings area, e.g. "Funde (6)".</summary>
  public string SelectedFindingsHeader => $"Funde ({DetailFindingsSource.Count})";

  /// <summary>True when the selected trigger has findings.</summary>
  public bool HasSelectedFindings => DetailFindingsSource.Count > 0;

  // The findings the details pane lists: those of the selected trigger, or of the shown non-trigger section.
  private IReadOnlyList<FindingItem> DetailFindingsSource => SelectedTrigger?.Findings ?? _focusFindings;

  /// <summary>The severities to choose from, with German names.</summary>
  public IReadOnlyList<KeyValuePair<FindingSeverity, string>> Severities
  {
    get;
  } =
  [
      new(FindingSeverity.Info, "Hinweis"),
        new(FindingSeverity.Warning, "Warnung"),
        new(FindingSeverity.Error, "Fehler"),
    ];

  /// <summary>Selects the trigger with the given nickname, if it exists.</summary>
  public void SelectByName (string nickname) =>
      SelectNode (Triggers.FirstOrDefault (item => item.Name.Equals (nickname, StringComparison.OrdinalIgnoreCase))?.Node);

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

  // Builds script, graph and findings for a document and keeps the selection by name.
  private void Analyze (IniDocument document, bool keepHistory)
  {
    string? previousName = SelectedTrigger?.Name;
    MissionScript script = MissionScript.FromDocument (document, FilePath);
    var graph = new TriggerGraph (script);
    GameData = GameData.Locate (FilePath, _gameFolderSettings.GameFolder);
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
  }

  /// <summary>Selects a trigger by node (from the graph, the details or a finding).</summary>
  public void SelectNode (TriggerNode? node, bool remember = true)
  {
    TriggerItem? item = node is null ? null : Triggers.FirstOrDefault (row => row.Node == node);
    if (item is not null && !TriggersView.Filter (item))
    {
      FilterText = string.Empty;
      OnlyWithFindings = false;
    }
    _navigating = !remember;
    try
    {
      SelectedTrigger = item;
    }
    finally
    {
      _navigating = false;
    }
  }

  private void GoBack ()
  {
    if (_history.Count == 0)
      return;
    TriggerNode node = _history.Pop ();
    SelectNode (node, remember: false);
  }

  private bool FilterTrigger (TriggerItem item) =>
      (FilterText.Length == 0 || item.Name.Contains (FilterText, StringComparison.OrdinalIgnoreCase))
      && (!OnlyWithFindings || item.Findings.Any (finding => finding.Severity >= MinimumSeverity));

  private TriggerNode? FindNodeForFinding (Finding finding)
  {
    if (_graph is null || Script is null)
      return null;
    IniSection? section = Script.Document.FindSectionAt (finding.LineNumber);
    Trigger? trigger = Script.Triggers.FirstOrDefault (candidate => candidate.Section == section);
    return trigger is null ? null : _graph.FindNode (trigger.Id);
  }

  private void OnSelectionChanged ()
  {
    _currentFinding = null;
    _focusSection = null;
    _focusFindings = [];
    References.Clear ();
    if (SelectedTrigger is not null)
    {
      foreach (ReferenceItem reference in ReferenceItem.ForNode (SelectedTrigger.Node).OrderBy (item => item.LineNumber))
        References.Add (reference);
    }
    SelectedSectionLines = BuildTriggerPreview ();
    NotifyDetailsChanged ();
    UpdateLayout ();
  }

  private void NotifyDetailsChanged ()
  {
    OnPropertyChanged (nameof (DetailsTitle));
    OnPropertyChanged (nameof (SelectedSummary));
    OnPropertyChanged (nameof (SelectedFindings));
    OnPropertyChanged (nameof (SelectedFindingsHeader));
    OnPropertyChanged (nameof (HasSelectedFindings));
  }

  private IReadOnlyList<SectionLineItem> BuildTriggerPreview ()
  {
    if (SelectedTrigger is null || Script is null)
      return [];
    if (SelectedTrigger.Node.Trigger is not { } trigger)
      return [new SectionLineItem (0, $"; '{SelectedTrigger.Name}' wird referenziert, aber kein [Trigger] definiert ihn.", [])];
    var parts = new List<(string? Caption, IniSection Section)> { ($"[Trigger] ab Zeile {trigger.LineNumber}", trigger.Section) };
    foreach (Trigger duplicate in Script.Triggers.Where (other => other.Id == trigger.Id && other != trigger))
      parts.Add (($"Zweite Definition, Zeile {duplicate.LineNumber} – von der Engine ignoriert", duplicate.Section));
    if (parts.Count > 1)
      parts[0] = ($"Definition, Zeile {trigger.LineNumber} – gilt", trigger.Section);
    return BuildPreview (parts);
  }

  private List<SectionLineItem> BuildPreview (List<(string? Caption, IniSection Section)> parts)
  {
    var items = new List<SectionLineItem> ();
    // IDS numbers get their text when game data is known (configured folder or the installation of the mission).
    GameData? gameData = GameData;
    foreach ((string? caption, IniSection section) in parts)
    {
      if (caption is not null)
        items.Add (new SectionLineItem (0, caption, [], isCaption: true, section: section));
      List<IniLine> lines = [section.Header, .. section.Lines];
      // Trailing blank lines belong to the section in the file but not in the preview.
      while (lines.Count > 1 && lines[^1].Kind == IniLineKind.Blank)
        lines.RemoveAt (lines.Count - 1);
      items.AddRange (lines.Select (line => new SectionLineItem (line.Number, line.Text, _findingsByLine[line.Number].ToList (),
          note: gameData?.DescribeIds (line, section.Name))));
    }
    return items;
  }

  private void UpdateLayout ()
  {
    if (SelectedTrigger is null)
    {
      Layout = null;
      return;
    }
    var kinds = new HashSet<TriggerReferenceKind> ();
    if (ShowActivate)
      kinds.Add (TriggerReferenceKind.Activate);
    if (ShowDeactivate)
      kinds.Add (TriggerReferenceKind.Deactivate);
    if (ShowWatch)
      kinds.Add (TriggerReferenceKind.Watch);
    if (ShowSave)
      kinds.Add (TriggerReferenceKind.Save);
    Layout = GraphLayout.Build (SelectedTrigger.Node, new GraphLayoutOptions
    {
      NodeHeight = 56,
      NodeWidth = 260,
      DepthBackward = DepthBackward,
      DepthForward = DepthForward,
      Kinds = kinds,
    });
  }
}
