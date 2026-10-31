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
    private readonly Stack<TriggerNode> _history = new();
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

    /// <summary>Creates the view model.</summary>
    public MainViewModel()
    {
        TriggersView = CollectionViewSource.GetDefaultView(Triggers);
        TriggersView.Filter = item => FilterTrigger((TriggerItem)item);
        FindingsView = CollectionViewSource.GetDefaultView(Findings);
        FindingsView.Filter = item => ((FindingItem)item).Severity >= MinimumSeverity;
        BackCommand = new RelayCommand(GoBack, () => _history.Count > 0);
    }

    /// <summary>The loaded script, or null.</summary>
    public MissionScript? Script
    {
        get => _script;
        private set
        {
            if (SetProperty(ref _script, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    /// <summary>Title bar text.</summary>
    public string WindowTitle => Script?.FilePath is { } path ? $"{Path.GetFileName(path)} – Mission Editor" : "Mission Editor";

    /// <summary>All trigger rows.</summary>
    public ObservableCollection<TriggerItem> Triggers { get; } = [];

    /// <summary>The filtered trigger list.</summary>
    public ICollectionView TriggersView { get; }

    /// <summary>All findings.</summary>
    public ObservableCollection<FindingItem> Findings { get; } = [];

    /// <summary>The findings at or above <see cref="MinimumSeverity"/>.</summary>
    public ICollectionView FindingsView { get; }

    /// <summary>The references of the selected trigger.</summary>
    public ObservableCollection<ReferenceItem> References { get; } = [];

    /// <summary>Goes back to the previously selected trigger.</summary>
    public RelayCommand BackCommand { get; }

    /// <summary>Text filter for the trigger list (part of the nickname).</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
                TriggersView.Refresh();
        }
    }

    /// <summary>Text for the status bar.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>The selected trigger row.</summary>
    public TriggerItem? SelectedTrigger
    {
        get => _selectedTrigger;
        set
        {
            TriggerItem? previous = _selectedTrigger;
            if (!SetProperty(ref _selectedTrigger, value))
                return;
            if (!_navigating && previous is not null)
                _history.Push(previous.Node);
            OnSelectionChanged();
        }
    }

    /// <summary>The section text of the selected trigger.</summary>
    public string SelectedSectionText => SelectedTrigger?.Node.Trigger?.Section.GetText()
        ?? (SelectedTrigger is null ? string.Empty : $"; '{SelectedTrigger.Name}' wird referenziert, aber kein [Trigger] definiert ihn.");

    /// <summary>Header text of the details pane.</summary>
    public string SelectedSummary
    {
        get
        {
            Trigger? trigger = SelectedTrigger?.Node.Trigger;
            if (trigger is null)
                return SelectedTrigger is null ? string.Empty : "nicht definiert";
            var parts = new List<string> { $"Zeile {trigger.LineNumber}", $"ID {trigger.Id}" };
            if (trigger.InitialState != TriggerState.Inactive)
                parts.Add($"InitState = {trigger.InitialState.ToString().ToUpperInvariant()}");
            if (trigger.Repeatable)
                parts.Add("repeatable");
            if (trigger.System is not null)
                parts.Add($"System {trigger.System}");
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>The layout of the neighbourhood of the selected trigger.</summary>
    public GraphLayout? Layout
    {
        get => _layout;
        private set
        {
            if (SetProperty(ref _layout, value))
                OnPropertyChanged(nameof(HiddenNodeText));
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
            if (SetProperty(ref _depthBackward, Math.Clamp(value, 0, 10)))
                UpdateLayout();
        }
    }

    /// <summary>Steps to follow forwards in the graph.</summary>
    public int DepthForward
    {
        get => _depthForward;
        set
        {
            if (SetProperty(ref _depthForward, Math.Clamp(value, 0, 10)))
                UpdateLayout();
        }
    }

    /// <summary>Show Act_ActTrig edges.</summary>
    public bool ShowActivate
    {
        get => _showActivate;
        set
        {
            if (SetProperty(ref _showActivate, value))
                UpdateLayout();
        }
    }

    /// <summary>Show Act_DeactTrig edges.</summary>
    public bool ShowDeactivate
    {
        get => _showDeactivate;
        set
        {
            if (SetProperty(ref _showDeactivate, value))
                UpdateLayout();
        }
    }

    /// <summary>Show Cnd_WatchTrigger edges.</summary>
    public bool ShowWatch
    {
        get => _showWatch;
        set
        {
            if (SetProperty(ref _showWatch, value))
                UpdateLayout();
        }
    }

    /// <summary>Show Act_Save edges.</summary>
    public bool ShowSave
    {
        get => _showSave;
        set
        {
            if (SetProperty(ref _showSave, value))
                UpdateLayout();
        }
    }

    /// <summary>Lowest severity shown in the findings list.</summary>
    public FindingSeverity MinimumSeverity
    {
        get => _minimumSeverity;
        set
        {
            if (SetProperty(ref _minimumSeverity, value))
                FindingsView.Refresh();
        }
    }

    /// <summary>The severities to choose from, with German names.</summary>
    public IReadOnlyList<KeyValuePair<FindingSeverity, string>> Severities { get; } =
    [
        new(FindingSeverity.Info, "Hinweis"),
        new(FindingSeverity.Warning, "Warnung"),
        new(FindingSeverity.Error, "Fehler"),
    ];

    /// <summary>Selects the trigger with the given nickname, if it exists.</summary>
    public void SelectByName(string nickname) =>
        SelectNode(Triggers.FirstOrDefault(item => item.Name.Equals(nickname, StringComparison.OrdinalIgnoreCase))?.Node);

    /// <summary>Loads a mission script and runs the checks.</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="InvalidDataException">The file is not a text INI.</exception>
    public void Load(string path)
    {
        string? previousName = SelectedTrigger?.Name;
        MissionScript script = MissionScript.Load(path);
        var graph = new TriggerGraph(script);
        IReadOnlyList<Finding> findings = MissionChecker.Run(script);

        _history.Clear();
        _selectedTrigger = null;
        Script = script;
        _graph = graph;

        Findings.Clear();
        foreach (Finding finding in findings)
            Findings.Add(new FindingItem(finding, FindNodeForFinding(finding)));
        var findingsPerNode = Findings.Where(item => item.Node is not null).GroupBy(item => item.Node!).ToDictionary(group => group.Key, group => group.Count());

        Triggers.Clear();
        foreach (TriggerNode node in graph.Nodes.OrderBy(node => node.Trigger?.LineNumber ?? int.MaxValue).ThenBy(node => node.Name))
            Triggers.Add(new TriggerItem(node, findingsPerNode.GetValueOrDefault(node)));

        int errors = findings.Count(finding => finding.Severity == FindingSeverity.Error);
        int warnings = findings.Count(finding => finding.Severity == FindingSeverity.Warning);
        StatusText = $"{script.Document.Lines.Count} Zeilen · {script.Triggers.Count} Trigger · {script.Objects.Count} Objekte · {graph.Edges.Count} Verweise · {errors} Fehler, {warnings} Warnungen";

        TriggerItem? initial = (previousName is null ? null : Triggers.FirstOrDefault(item => item.Name.Equals(previousName, StringComparison.OrdinalIgnoreCase)))
            ?? Triggers.FirstOrDefault(item => item.IsInitiallyActive)
            ?? Triggers.FirstOrDefault();
        SelectNode(initial?.Node, remember: false);
    }

    /// <summary>Writes the document unchanged to another file.</summary>
    public void SaveAs(string path) => Script?.Document.Save(path);

    /// <summary>Selects a trigger by node (from the graph, the details or a finding).</summary>
    public void SelectNode(TriggerNode? node, bool remember = true)
    {
        TriggerItem? item = node is null ? null : Triggers.FirstOrDefault(row => row.Node == node);
        if (item is not null && !TriggersView.Filter(item))
            FilterText = string.Empty;
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

    private void GoBack()
    {
        if (_history.Count == 0)
            return;
        TriggerNode node = _history.Pop();
        SelectNode(node, remember: false);
    }

    private bool FilterTrigger(TriggerItem item) =>
        FilterText.Length == 0 || item.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);

    private TriggerNode? FindNodeForFinding(Finding finding)
    {
        if (_graph is null || Script is null)
            return null;
        IniSection? section = Script.Document.FindSectionAt(finding.LineNumber);
        Trigger? trigger = Script.Triggers.FirstOrDefault(candidate => candidate.Section == section);
        return trigger is null ? null : _graph.FindNode(trigger.Id);
    }

    private void OnSelectionChanged()
    {
        References.Clear();
        if (SelectedTrigger is not null)
        {
            foreach (ReferenceItem reference in ReferenceItem.ForNode(SelectedTrigger.Node).OrderBy(item => item.LineNumber))
                References.Add(reference);
        }
        OnPropertyChanged(nameof(SelectedSectionText));
        OnPropertyChanged(nameof(SelectedSummary));
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        if (SelectedTrigger is null)
        {
            Layout = null;
            return;
        }
        var kinds = new HashSet<TriggerReferenceKind>();
        if (ShowActivate)
            kinds.Add(TriggerReferenceKind.Activate);
        if (ShowDeactivate)
            kinds.Add(TriggerReferenceKind.Deactivate);
        if (ShowWatch)
            kinds.Add(TriggerReferenceKind.Watch);
        if (ShowSave)
            kinds.Add(TriggerReferenceKind.Save);
        Layout = GraphLayout.Build(SelectedTrigger.Node, new GraphLayoutOptions
        {
            DepthBackward = DepthBackward,
            DepthForward = DepthForward,
            Kinds = kinds,
        });
    }
}
