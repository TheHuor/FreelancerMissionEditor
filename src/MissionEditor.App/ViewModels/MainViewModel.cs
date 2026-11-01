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
public sealed partial class MainViewModel : ObservableObject
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
          note: gameData?.DescribeIds (line, section.Name))
      {
        ResourceLinks = ResourceReferences.Where (reference => reference.LineNumber == line.Number)
            .Select (reference => new ResourceLinkItem (reference, gameData?.Texts?.Resolve (reference.Ids))).ToArray (),
        ResourceErrors = MissionResourceIndex.Parameters (line, section.Name)
            .Where (parameter => !int.TryParse (line.GetValue (parameter.Index), out _))
            .Select (parameter => $"{parameter.Role}: '{line.GetValue (parameter.Index)}' ist keine gültige numerische IDS.")
            .Concat (MissionResourceIndex.ObjectTargets (line, section)
                .Where (target => gameData?.Definition (target.Kind, line.GetValue (target.Index))?.NameIds is null)
                .Select (target => $"{target.Label} · {line.GetValue (target.Index)} · {(gameData is null ? "Spielressourcen nicht geladen" : "kein Anzeigename zugeordnet")}"))
            .ToArray (),
      }));
    }
    return items;
  }

}
