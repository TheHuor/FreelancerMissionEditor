using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;
using Microsoft.Win32;
using MissionEditor.Core.Model;

namespace MissionEditor.App;

/// <summary>A reusable non-modal browser and editor for existing installation resources.</summary>
public partial class ResourceBrowserWindow : Window
{
  private MissionScript? _script;
  private GameData? _gameData;
  private string? _missionPath;
  private IReadOnlyList<IdsReference> _references = [];
  private ILookup<int, IdsReference> _referencesByIds = Array.Empty<IdsReference>().ToLookup(item => item.Ids);
  private List<ResourceEntry> _catalog = [];
  private Dictionary<int, ResourceEntry[]> _catalogByIds = [];
  private List<ResourceRow> _rows = [];
  private readonly Dictionary<ResourceIdentity, ResourceDraft> _drafts = [];
  private readonly HashSet<ResourceIdentity> _exported = [];
  private readonly Dictionary<string, string> _fileHashes = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, string> _backups = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<TextBox> _segments = [];
  private ResourceRow? _selected;
  private ResourceEntry? _entry;
  private InfocardDocument? _infocard;
  private string _baseline = string.Empty;
  private bool _updating;
  private bool _editorDirty;
  private bool _catalogReady;
  private CancellationTokenSource? _indexCancellation;
  private int _generation;
  private int _filterGeneration;
  private enum GuardChoice { Save, Discard, Cancel }

  /// <summary>Raised after a verified write to the original DLL, for reloading editor hints.</summary>
  public event EventHandler? ResourceSaved;
  /// <summary>Requests navigation to an absolute line in the current mission.</summary>
  public event Action<int>? UsageRequested;
  /// <summary>Requests the host's installation picker.</summary>
  public event EventHandler? GameFolderRequested;

  /// <summary>Creates the browser. Call RefreshContext before showing it.</summary>
  public ResourceBrowserWindow()
  {
    InitializeComponent();
    _updating = true;
    CategoryBox.ItemsSource = new[] { "Alle Verwendungen" };
    CategoryBox.SelectedIndex = 0;
    DllBox.ItemsSource = new[] { new DllChoice(null, -1) };
    DllBox.DisplayMemberPath = nameof(DllChoice.Label);
    DllBox.SelectedIndex = 0;
    _updating = false;
    var geometry = WindowGeometry.Restore(this, "resources");
    if (geometry is not null)
    {
      double ratio = Math.Clamp(geometry.LeftRatio, .2, .7);
      ListColumn.Width = new GridLength(ratio, GridUnitType.Star);
      BodyGrid.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
      UsageRow.Height = new GridLength(Math.Clamp(geometry.UsageHeight, 32, 350));
    }
    Closing += OnClosing;
    BodyGrid.SizeChanged += (_, _) => ConstrainDetailLayout();
    DetailHeaderScroll.SizeChanged += (_, _) => ConstrainDetailLayout();
    Closed += (_, _) => { CancelIndex(); _filterGeneration++; };
    PreviewKeyDown += (_, e) =>
    {
      if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { SaveCurrent(false); e.Handled = true; }
      if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
    };
    UpdateEnabled();
  }

  /// <summary>Refreshes usages from the live document; source identity changes are guarded.</summary>
  public bool RefreshContext(MissionScript? script, GameData? gameData, string? missionPath)
  {
    bool identityChange = !string.Equals(_gameData?.Root, gameData?.Root, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(_missionPath, missionPath, StringComparison.OrdinalIgnoreCase);
    if (identityChange && !TryLeave("Mission oder Spielinstallation wechseln")) return false;
    int? selectedId = _entry?.Ids ?? _selected?.Ids;
    _script = script;
    _gameData = gameData;
    _missionPath = missionPath;
    _references = script is null ? [] : MissionResourceIndex.Build(script.Document, missionPath, gameData);
    _referencesByIds = _references.ToLookup(reference => reference.Ids);
    if (identityChange)
    {
      CancelIndex();
      _generation++;
      _catalog.Clear();
      _catalogByIds.Clear();
      _catalogReady = false;
      _selected = null;
      _entry = null;
      ClearSelection();
      _fileHashes.Clear();
      _updating = true;
      ScopeBox.SelectedIndex = 0;
      SearchBox.Clear();
      _updating = false;
    }
    ContextText.Text = $"Installation: {gameData?.Root ?? "keine – Spielressourcen nicht geladen"}  ·  Mission: {missionPath ?? "keine"}";
    ContextText.ToolTip = ContextText.Text;
    BuildRows();
    if (selectedId is { } id && _entry is not null)
    {
      _selected = _rows.FirstOrDefault(row => row.Ids == id);
      UpdateUsages(id);
      if (_selected is null) SetStatus("Die ausgewählte ID wird in der aktuellen Mission nicht mehr verwendet.");
      else if (!_editorDirty) ShowEntry(_gameData?.Texts?.Resolve(id, _entry.Identity?.Language, _entry.Identity?.Kind) ?? ResourceEntry.Missing(id));
    }
    return true;
  }

  /// <summary>Selects the requested ID, making it visible independently of previous filters.</summary>
  public bool NavigateToId(int ids)
  {
    bool retainEditor = _entry?.Ids == ids;
    if (!retainEditor && !GuardCurrent("Andere ID öffnen")) return false;
    _updating = true;
    ScopeBox.SelectedIndex = 0;
    ResetFilterControls();
    _updating = false;
    BuildRows();
    ResourceRow? row = _rows.FirstOrDefault(item => item.Ids == ids);
    if (row is null)
    {
      row = NewRow(ids);
      _rows.Add(row);
      ApplyFilters();
    }
    if (retainEditor)
    {
      _selected = row;
      _updating = true; ResourceList.SelectedItem = row; ResourceList.ScrollIntoView(row); _updating = false;
      UpdateUsages(ids);
    }
    else SelectRow(row);
    if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
    Activate();
    return true;
  }

  private void ClearSelection()
  {
    _updating = true;
    _entry = null; _selected = null; _infocard = null; _baseline = string.Empty; _editorDirty = false;
    _segments.Clear(); SegmentPanel.Children.Clear();
    IdText.Text = "ID auswählen"; MetadataText.Text = string.Empty; MetadataText.ToolTip = null;
    TechnicalText.Text = string.Empty; DraftText.Text = string.Empty;
    ResolutionText.Text = "ID auswählen, um Text und Fundstellen zu lesen";
    StringEditor.Clear(); XmlEditor.Clear(); PreviewText.Clear();
    ResetUndo(StringEditor); ResetUndo(XmlEditor);
    LanguageBox.ItemsSource = null; UsageList.ItemsSource = null;
    UsageExpander.Header = "Wird verwendet von · Aktuelle Mission";
    XmlTab.Visibility = Visibility.Collapsed; ContentTabs.SelectedIndex = 0;
    _updating = false;
    UpdateEnabled();
    SetStatus("ID auswählen, um Text und Fundstellen zu lesen");
  }

  /// <summary>Protects all pending drafts before closing, reloading or changing sources.</summary>
  public bool TryLeave(string reason)
  {
    if (!_editorDirty && _drafts.Count == 0) return true;
    int count = _drafts.Count + (_editorDirty && (_entry?.Identity is not { } selected || !_drafts.ContainsKey(selected)) ? 1 : 0);
    GuardChoice choice = AskGuard(reason, "Alle offenen Ressourcenentwürfe", count);
    if (choice == GuardChoice.Cancel) return false;
    if (choice == GuardChoice.Discard)
    {
      _drafts.Clear(); _exported.Clear(); _fileHashes.Clear(); _editorDirty = false;
      if (_entry is not null) ShowEntry(_entry);
      return true;
    }
    if (!ApplyEditor()) return false;
    foreach (string file in _drafts.Keys.Select(key => key.DllPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
      if (!WriteFile(file, null)) return false;
    return true;
  }

  private ResourceRow NewRow(int ids)
  {
    var usages = _referencesByIds[ids].ToArray();
    ResourceEntry entry = _catalogByIds.TryGetValue(ids, out var variants) ? variants[0] : _gameData?.Texts?.Resolve(ids) ?? ResourceEntry.Missing(ids);
    ResourceEntry[] available = variants ?? (entry.IsResolved ? _gameData?.Texts?.Variants(ids).ToArray() : null) ?? [entry];
    bool hasDraft = _drafts.Keys.Any(key => key.DllPath == entry.Identity?.DllPath && key.LocalId == entry.Identity?.LocalId);
    if (entry.Identity is { } identity && _drafts.TryGetValue(identity, out var draft))
    {
      string preview = draft.EditedText;
      if (identity.Kind == ResourceKind.Infocard)
      {
        try { preview = InfocardDocument.Parse(preview).PlainText; }
        catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException) { preview = entry.Text; }
      }
      // This entry exists only in the browser row. The selected editor still resolves disk bytes.
      entry = entry with { Text = preview };
    }
    int slot = ids >> 16;
    string? dllPath = entry.Identity?.DllPath ?? (_gameData?.Texts?.DllPaths is { } paths && slot >= 0 && slot < paths.Count ? paths[slot] : null);
    return new ResourceRow(ids, entry, usages, hasDraft, dllPath, available);
  }

  private void BuildRows()
  {
    IEnumerable<int> ids = ScopeBox.SelectedIndex == 1
        ? _catalog.Select(entry => entry.Ids).Concat(_references.Select(reference => reference.Ids))
        : _references.Select(reference => reference.Ids);
    _rows = ids.Distinct().Select(NewRow).ToList();
    _selected = _rows.FirstOrDefault(row => row.Ids == _selected?.Ids) ?? _selected;
    string? category = CategoryBox.SelectedItem as string;
    string? dll = (DllBox.SelectedItem as DllChoice)?.Path;
    _updating = true;
    CategoryBox.ItemsSource = new[] { "Alle Verwendungen" }.Concat(_references.Select(item => item.Category).Distinct().Order()).ToArray();
    CategoryBox.SelectedItem = CategoryBox.Items.Cast<string>().Contains(category ?? "") ? category : "Alle Verwendungen";
    DllBox.ItemsSource = new[] { new DllChoice(null, -1) }.Concat((_gameData?.Texts?.DllPaths ?? []).Select((path, slot) => new DllChoice(path, slot))).ToArray();
    DllBox.SelectedItem = DllBox.Items.Cast<DllChoice>().FirstOrDefault(item => string.Equals(item.Path, dll, StringComparison.OrdinalIgnoreCase)) ?? DllBox.Items[0];
    _updating = false;
    ApplyFilters();
  }

  private async void ApplyFilters()
  {
    if (_updating || ResourceList is null) return;
    string query = SearchBox.Text.Trim();
    string category = CategoryBox.SelectedItem as string ?? "Alle Verwendungen";
    string? dll = (DllBox.SelectedItem as DllChoice)?.Path;
    DllBox.ToolTip = dll ?? "Alle tatsächlich registrierten Ressourcen-DLLs";
    int kind = KindBox.SelectedIndex;
    int sort = SortBox.SelectedIndex;
    int generation = ++_filterGeneration;
    ResourceRow[] snapshot = _rows.ToArray();
    ResourceRow[] Filter()
    {
    IEnumerable<ResourceRow> rows = snapshot.Where(row =>
        row.Variants.Any(variant => MatchesVariant(variant, query, kind, row.Ids))
        && (category == "Alle Verwendungen" || row.References.Any(reference => reference.Category == category))
        && (dll is null || string.Equals(row.DllPath, dll, StringComparison.OrdinalIgnoreCase))
        );
    rows = sort switch { 1 => rows.OrderBy(row => row.DllPath).ThenBy(row => row.Ids), 2 => rows.OrderBy(row => row.Categories).ThenBy(row => row.Ids), _ => rows.OrderBy(row => row.Ids) };
    return rows.ToArray();
    }
    var result = snapshot.Length > 300 ? await Task.Run(Filter) : Filter();
    if (generation != _filterGeneration) return;
    _updating = true;
    ResourceList.ItemsSource = result;
    ResourceList.SelectedItem = result.FirstOrDefault(row => row.Ids == _selected?.Ids);
    _updating = false;
    if (ResourceList.SelectedItem is ResourceRow selected && _entry is { } shown
        && !MatchesVariant(shown, query, kind, shown.Ids) && !_editorDirty
        && (shown.Identity is null || !_drafts.ContainsKey(shown.Identity)))
      SelectRow(selected);
    CountText.Text = $"{result.Length} von {snapshot.Length} eindeutigen IDs";
    if (result.Length == 0) SetStatus("Keine Suchtreffer. Suchbegriff und Filter zurücksetzen.");
  }

  private void SelectRow(ResourceRow row)
  {
    row = _rows.FirstOrDefault(candidate => candidate.Ids == row.Ids) ?? row;
    if (_entry?.Ids != row.Ids) ContentTabs.SelectedIndex = 0;
    ResourceIdentity? previousIdentity = _entry?.Ids == row.Ids ? _entry.Identity : null;
    _updating = true;
    ResourceList.SelectedItem = row;
    ResourceList.ScrollIntoView(row);
    _updating = false;
    _selected = row;
    var variants = _catalogByIds.TryGetValue(row.Ids, out var found) ? found.Where(item => item.IsResolved).ToArray()
        : row.Entry.IsResolved ? _gameData?.Texts?.Variants(row.Ids).ToArray() ?? Array.Empty<ResourceEntry>() : Array.Empty<ResourceEntry>();
    _updating = true;
    // Labels explicitly identify the actual language and resource kind, never a guessed default.
    LanguageBox.ItemsSource = (variants.Length == 0 ? new[] { row.Entry } : variants).Select(item => new LanguageChoice(item)).ToArray();
    LanguageBox.DisplayMemberPath = nameof(LanguageChoice.Label);
    var matching = LanguageBox.Items.Cast<LanguageChoice>().Where(item => MatchesVariant(item.Entry, SearchBox.Text.Trim(), KindBox.SelectedIndex, row.Ids)).ToArray();
    var preferred = matching.FirstOrDefault(item => item.Entry.Identity == previousIdentity)
        ?? matching.FirstOrDefault(item => item.Entry.Identity == row.Entry.Identity)
        ?? matching.FirstOrDefault()
        ?? LanguageBox.Items.Cast<LanguageChoice>().First();
    LanguageBox.SelectedItem = preferred;
    _updating = false;
    ShowEntry(preferred.Entry);
    UpdateUsages(row.Ids);
  }

  private void ShowEntry(ResourceEntry entry)
  {
    _entry = entry;
    _updating = true;
    _editorDirty = false;
    IdText.Text = $"IDS {entry.Ids}";
    MetadataText.Text = entry.Identity is { } key ? $"{key.Kind} · {Path.GetFileName(key.DllPath)} · {LanguageName(key.Language)}" : "Keine aufgelöste Ressource";
    MetadataText.ToolTip = entry.Identity?.DllPath;
    TechnicalText.Text = entry.Identity is { } identity ? $"DLL: {identity.DllPath}\nSlot: {identity.Slot} · lokale ID: {identity.LocalId} · Sprache: {identity.Language} (0x{identity.Language:X4})" : entry.StatusText;
    if (entry.Identity is { } saved && _backups.TryGetValue(saved.DllPath, out string? backup)) TechnicalText.Text += "\nLetzte Sicherung: " + backup;
    ResolutionText.Text = entry.StatusText + (entry.Identity?.Kind == ResourceKind.Infocard && !entry.StatusText.Contains("Klartext aus Infocard") ? " · Klartext aus Infocard" : "");
    string content = entry.Identity is { } id && _drafts.TryGetValue(id, out var draft) ? draft.EditedText : entry.RawText ?? entry.Text;
    _baseline = content;
    _infocard = null;
    _segments.Clear();
    SegmentPanel.Children.Clear();
    StringEditor.Visibility = entry.Identity?.Kind == ResourceKind.Infocard ? Visibility.Collapsed : Visibility.Visible;
    SegmentScroll.Visibility = entry.Identity?.Kind == ResourceKind.Infocard ? Visibility.Visible : Visibility.Collapsed;
    XmlTab.Visibility = entry.Identity?.Kind == ResourceKind.Infocard ? Visibility.Visible : Visibility.Collapsed;
    if (!entry.IsResolved || (ContentTabs.SelectedItem == XmlTab && XmlTab.Visibility == Visibility.Collapsed)) ContentTabs.SelectedIndex = 0;
    StringEditor.Text = content;
    XmlEditor.Text = content;
    ResetUndo(StringEditor);
    ResetUndo(XmlEditor);
    if (entry.Identity?.Kind == ResourceKind.Infocard)
    {
      try
      {
        _infocard = InfocardDocument.Parse(content);
        if (_infocard.CanEditSegments)
        {
          int index = 0;
          int[] paragraphs = SegmentParagraphs(content);
          foreach (string segment in _infocard.TextSegments)
          {
            int paragraph = index < paragraphs.Length ? paragraphs[index] : 1;
            SegmentPanel.Children.Add(new TextBlock { Text = $"TEXT {++index} · Absatz {paragraph} · ursprüngliche Reihenfolge", Margin = new Thickness(3, 8, 3, 4) });
            var editor = new TextBox { Text = segment, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(3) };
            editor.TextChanged += OnSegmentChanged;
            _segments.Add(editor);
            SegmentPanel.Children.Add(editor);
          }
        }
        else SegmentPanel.Children.Add(new TextBlock { Text = "Diese RDL-Struktur kann nur im Reiter RDL/XML verlustfrei bearbeitet werden.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) });
        PreviewText.Text = _infocard.PlainText;
      }
      catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or ArgumentException)
      {
        PreviewText.Text = entry.Text;
        SegmentPanel.Children.Add(new TextBlock { Text = "RDL konnte nicht gelesen werden: " + e.Message, TextWrapping = TextWrapping.Wrap });
      }
    }
    else PreviewText.Text = content;
    if (!entry.IsResolved) PreviewText.Text = entry.StatusText;
    _updating = false;
    UpdateDraftStatus();
    UpdateEnabled();
    SetStatus(entry.StatusText);
  }

  private void UpdateUsages(int ids)
  {
    var usages = _referencesByIds[ids].Select(reference => new UsageRowItem(reference)).ToArray();
    UsageList.ItemsSource = usages;
    UsageExpander.Header = $"Wird verwendet von · {usages.Length} Fundstellen · Aktuelle Mission";
    if (usages.Length == 0) SetStatus("Keine Verwendung in aktueller Mission gefunden.");
  }

  private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (_updating || ResourceList.SelectedItem is not ResourceRow next || next.Ids == _selected?.Ids) return;
    if (!GuardCurrent("Andere ID öffnen"))
    {
      _updating = true; ResourceList.SelectedItem = _selected; _updating = false; return;
    }
    SelectRow(next);
  }

  private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
  {
    if (_updating || LanguageBox.SelectedItem is not LanguageChoice choice || choice.Entry.Identity == _entry?.Identity) return;
    if (!GuardCurrent("Andere Sprachvariante öffnen"))
    {
      _updating = true;
      LanguageBox.SelectedItem = LanguageBox.Items.Cast<LanguageChoice>().FirstOrDefault(item => item.Entry.Identity == _entry?.Identity);
      _updating = false;
      return;
    }
    ShowEntry(choice.Entry);
  }

  private void OnEditorChanged(object sender, TextChangedEventArgs e)
  {
    if (_updating || _entry?.Identity?.Kind != ResourceKind.String) return;
    MarkEditorDirty(StringEditor.Text != _baseline);
    PreviewText.Text = StringEditor.Text;
  }

  private void OnSegmentChanged(object sender, TextChangedEventArgs e)
  {
    if (_updating || _infocard is null || !_infocard.CanEditSegments) return;
    try
    {
      string xml = _infocard.WithTextSegments(_segments.Select(editor => editor.Text).ToArray());
      _updating = true; XmlEditor.Text = xml; _updating = false;
      PreviewText.Text = InfocardDocument.Parse(xml).PlainText;
      MarkEditorDirty(xml != _baseline);
    }
    catch (Exception error) when (error is InvalidDataException or ArgumentException) { SetStatus(error.Message); }
  }

  private void OnXmlChanged(object sender, TextChangedEventArgs e)
  {
    if (_updating || _entry?.Identity?.Kind != ResourceKind.Infocard) return;
    MarkEditorDirty(XmlEditor.Text != _baseline);
    try { PreviewText.Text = InfocardDocument.Parse(XmlEditor.Text).PlainText; }
    catch (Exception error) when (error is InvalidDataException or System.Xml.XmlException or ArgumentException) { SetStatus("RDL/XML noch ungültig: " + error.Message); }
    // Segment editing must reflect the XML source; rebuilding on tab change avoids replacing focused controls.
    _infocard = null;
    SegmentPanel.Children.Clear();
    SegmentPanel.Children.Add(new TextBlock { Text = "RDL/XML wurde bearbeitet. Änderungen übernehmen, um die TEXT-Segmente neu aufzubauen.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) });
  }

  private void MarkEditorDirty(bool dirty)
  {
    if (dirty && _entry?.Identity is { } identity && !_fileHashes.ContainsKey(identity.DllPath))
    {
      try { _fileHashes[identity.DllPath] = ResourceDraft.Create(_entry, _baseline).OriginalFileHash; }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
      {
        // A conflict sentinel can never authorize an original write. It retains the stale
        // selected draft for an explicit full-copy export, which the writer validates separately.
        _fileHashes[identity.DllPath] = "CONFLICT:" + Guid.NewGuid().ToString("N");
        SetStatus("Originalzustand konnte nicht erfasst werden. Original wird nicht überschrieben; Speichern unter… bleibt möglich. " + e.Message);
      }
    }
    _editorDirty = dirty;
    if (!dirty && _entry?.Identity is { } key && !_drafts.Keys.Any(item => string.Equals(item.DllPath, key.DllPath, StringComparison.OrdinalIgnoreCase))) _fileHashes.Remove(key.DllPath);
    UpdateDraftStatus();
  }

  private bool ApplyEditor()
  {
    if (!_editorDirty) return true;
    if (_entry?.Identity is not { } identity || !_entry.IsResolved) return false;
    string text = identity.Kind == ResourceKind.Infocard ? XmlEditor.Text : StringEditor.Text;
    try
    {
      ResourceDraft.Validate(_entry, text);
      ResourceDraft draft = _fileHashes.TryGetValue(identity.DllPath, out string? hash)
          ? new ResourceDraft(_entry, text, hash) : ResourceDraft.Create(_entry, text);
      _drafts[identity] = draft;
      _exported.Remove(identity);
      _editorDirty = false;
      _baseline = text;
      ShowEntry(_entry);
      BuildRows();
      UpdateDraftStatus();
      SetStatus("Entwurf – nicht gespeichert. Ändert alle Verwendungen dieser Ressource.");
      return true;
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException or ArgumentException)
    {
      SetStatus("Entwurf kann nicht übernommen werden: " + e.Message);
      return false;
    }
  }

  private void UpdateDraftStatus()
  {
    string? file = _entry?.Identity?.DllPath;
    int count = _drafts.Keys.Count(key => string.Equals(key.DllPath, file, StringComparison.OrdinalIgnoreCase));
    bool exported = _entry?.Identity is { } id && _exported.Contains(id);
    DraftText.Text = (_editorDirty ? "Editor geändert – nicht gespeichert. " : count > 0 ? "Entwurf – nicht gespeichert. " : "")
        + (exported ? "Als Kopie exportiert; Original unverändert. " : "")
        + (file is null ? "" : $"{Path.GetFileName(file)} · {count} übernommene Änderungen\nÄndert alle Verwendungen dieser Ressource.");
    DraftText.ToolTip = file;
    SaveButton.Content = exported ? "Änderungen ins Original speichern" : "Speichern";
  }

  private bool GuardCurrent(string reason)
  {
    if (!_editorDirty && (_entry?.Identity is not { } key || !_drafts.ContainsKey(key))) return true;
    GuardChoice choice = AskGuard(reason, $"IDS {_entry?.Ids} – aktuelle Sprachvariante", 1);
    if (choice == GuardChoice.Cancel) return false;
    if (choice == GuardChoice.Save) return SaveCurrent(false);
    if (_entry?.Identity is { } identity) { _drafts.Remove(identity); _exported.Remove(identity); ReleaseUnusedHash(identity.DllPath); }
    _editorDirty = false;
    if (_entry is not null) ShowEntry(_entry);
    BuildRows();
    return true;
  }

  private GuardChoice AskGuard(string reason, string scope, int count)
  {
    var dialog = new Window { Owner = this, Title = "Ungespeicherte Ressourcenänderungen", Width = 530, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background, Foreground = Foreground };
    dialog.SetResourceReference(StyleProperty, typeof(Window));
    GuardChoice answer = GuardChoice.Cancel;
    var panel = new StackPanel { Margin = new Thickness(20) };
    panel.Children.Add(new TextBlock { Text = $"{reason}?\n\n{scope}: {count} offene Änderungen.\nAls Kopie exportierte Entwürfe sind weiterhin nicht ins Original gespeichert.\nSpeichern schreibt alle Entwürfe der jeweiligen DLL ins Original; Verwerfen betrifft den genannten Umfang.", TextWrapping = TextWrapping.Wrap });
    var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
    foreach (var item in new[] { ("Speichern", GuardChoice.Save), ("Verwerfen", GuardChoice.Discard), ("Abbrechen", GuardChoice.Cancel) })
    {
      var button = new Button { Content = item.Item1, MinWidth = 95, Margin = new Thickness(6, 0, 0, 0), IsCancel = item.Item2 == GuardChoice.Cancel };
      button.Click += (_, _) => { answer = item.Item2; dialog.DialogResult = true; };
      buttons.Children.Add(button);
    }
    panel.Children.Add(buttons); dialog.Content = panel; dialog.ShowDialog(); return answer;
  }

  private bool SaveCurrent(bool saveAs)
  {
    if (_entry?.Identity is not { } identity || !ApplyEditor()) return false;
    string? destination = null;
    if (saveAs)
    {
      var picker = new SaveFileDialog { Title = "Vollständige geänderte Ressourcen-DLL kopieren", Filter = "Ressourcen-DLL (*.dll)|*.dll", FileName = Path.GetFileNameWithoutExtension(identity.DllPath) + "-edited.dll", OverwritePrompt = true };
      if (picker.ShowDialog(this) != true) return false;
      destination = picker.FileName;
      if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(identity.DllPath), StringComparison.OrdinalIgnoreCase))
      { SetStatus("Speichern unter benötigt einen anderen Dateinamen. Für das Original „Speichern“ verwenden."); return false; }
    }
    return WriteFile(identity.DllPath, destination);
  }

  private bool WriteFile(string file, string? destination)
  {
    var drafts = _drafts.Where(item => string.Equals(item.Key.DllPath, file, StringComparison.OrdinalIgnoreCase)).Select(item => item.Value).ToArray();
    if (drafts.Length == 0) { SetStatus("Keine übernommenen Änderungen für diese DLL."); return true; }
    CancelIndex();
    try
    {
      var result = ResourceWriter.Save(drafts, destination);
      if (result.IsCopy)
      {
        foreach (var draft in drafts) if (draft.Original.Identity is { } id) _exported.Add(id);
        SetStatus($"Vollständige DLL-Kopie: {result.Path}. Original unverändert; Entwürfe bleiben offen. Keine Registrierung geändert. Die Kopie muss im ursprünglichen Ressourcen-Slot eingesetzt werden.");
      }
      else
      {
        if (result.BackupPath is { } backup) _backups[file] = backup;
        foreach (var draft in drafts) if (draft.Original.Identity is { } id) { _drafts.Remove(id); _exported.Remove(id); }
        _fileHashes.Remove(file);
        _gameData?.Texts?.Reload();
        _catalog.Clear(); _catalogByIds.Clear(); _catalogReady = false;
        BuildRows();
        if (_entry is { } old) ShowEntry(_gameData?.Texts?.Resolve(old.Ids, old.Identity?.Language, old.Identity?.Kind) ?? ResourceEntry.Missing(old.Ids));
        ResourceSaved?.Invoke(this, EventArgs.Empty);
        SetStatus($"Gespeichert: {result.Path} · Sicherung: {result.BackupPath}");
        if (ScopeBox.SelectedIndex == 1) _ = IndexInstallation();
      }
      UpdateDraftStatus();
      return true;
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.ComponentModel.Win32Exception)
    {
      SetStatus("Speichern fehlgeschlagen. Auswahl und Entwürfe bleiben erhalten. Speichern unter… ist möglich. " + e.Message);
      return false;
    }
  }

  private async void OnScopeChanged(object sender, SelectionChangedEventArgs e)
  {
    if (_updating || ResourceList is null) return;
    BuildRows();
    if (ScopeBox.SelectedIndex == 1 && !_catalogReady) await IndexInstallation();
  }

  private async Task IndexInstallation()
  {
    if (_gameData?.Texts is not { } texts) { SetStatus("Spielressourcen nicht geladen. Spielordner wählen…"); return; }
    CancelIndex();
    var cancellation = _indexCancellation = new CancellationTokenSource();
    int generation = ++_generation;
    CancelIndexButton.Visibility = Visibility.Visible;
    SetStatus("Ressourcen werden im Hintergrund indexiert…");
    var progress = new Progress<string>(message => { if (generation == _generation) SetStatus(message); });
    try
    {
      var catalog = await Task.Run(() => texts.Enumerate(cancellation.Token, progress), cancellation.Token);
      if (generation != _generation || cancellation.IsCancellationRequested) return;
      _catalog = catalog.ToList(); _catalogByIds = _catalog.Where(item => item.IsResolved).GroupBy(item => item.Ids).ToDictionary(group => group.Key, group => group.ToArray()); _catalogReady = true;
      BuildRows();
      if (_selected is { } row && !_editorDirty) SelectRow(row);
      SetStatus($"Installationskatalog: {_catalog.Select(entry => entry.Ids).Distinct().Count()} IDs. Verwendungen beziehen sich ausschließlich auf die aktuelle Mission.");
    }
    catch (OperationCanceledException) { if (generation == _generation) SetStatus("Indexierung abgebrochen. Erneut „Gesamte Installation“ wählen oder Ressourcen neu laden."); }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception) { if (generation == _generation) SetStatus("Indexierung fehlgeschlagen: " + e.Message); }
    finally { if (generation == _generation) CancelIndexButton.Visibility = Visibility.Collapsed; cancellation.Dispose(); if (ReferenceEquals(_indexCancellation, cancellation)) _indexCancellation = null; }
  }

  private void CancelIndex() { _indexCancellation?.Cancel(); _generation++; if (CancelIndexButton is not null) CancelIndexButton.Visibility = Visibility.Collapsed; }
  private void OnCancelIndex(object sender, RoutedEventArgs e) { CancelIndex(); SetStatus("Indexierung abgebrochen."); }
  private void OnReload(object sender, RoutedEventArgs e)
  {
    if (!TryLeave("Ressourcen neu laden")) return;
    bool installationScope = ScopeBox.SelectedIndex == 1;
    CancelIndex(); _gameData?.Texts?.Reload(); _catalog.Clear(); _catalogByIds.Clear(); _catalogReady = false; _fileHashes.Clear();
    int? id = _entry?.Ids; BuildRows();
    if (id is { } value) SelectRow(_rows.FirstOrDefault(row => row.Ids == value) ?? NewRow(value));
    GameData? source = _gameData;
    string? mission = _missionPath;
    ResourceSaved?.Invoke(this, EventArgs.Empty);
    // Refresh the selection before starting background work. Only IndexInstallation owns
    // the completion path, where its generation and dirty-editor guards are enforced.
    if (installationScope && ScopeBox.SelectedIndex == 1 && ReferenceEquals(source, _gameData) && mission == _missionPath) _ = IndexInstallation();
  }
  private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilters();
  private void ResetFilterControls() { SearchBox.Clear(); CategoryBox.SelectedIndex = 0; DllBox.SelectedIndex = 0; KindBox.SelectedIndex = 0; }
  private void OnResetFilters(object sender, RoutedEventArgs e) { _updating = true; ResetFilterControls(); _updating = false; ApplyFilters(); }
  private void OnApply(object sender, RoutedEventArgs e) => ApplyEditor();
  private void OnDiscard(object sender, RoutedEventArgs e)
  {
    if (_entry?.Identity is { } identity) { _drafts.Remove(identity); _exported.Remove(identity); ReleaseUnusedHash(identity.DllPath); }
    if (_entry is not null) ShowEntry(_entry);
    BuildRows();
  }
  private void OnSave(object sender, RoutedEventArgs e) => SaveCurrent(false);
  private void OnSaveAs(object sender, RoutedEventArgs e) => SaveCurrent(true);
  private void OnCopyId(object sender, RoutedEventArgs e) { if (_entry is not null) Clipboard.SetText(_entry.Ids.ToString()); }
  private void OnCopyText(object sender, RoutedEventArgs e) { if (PreviewText.Text.Length > 0) Clipboard.SetText(PreviewText.Text); }
  private void OnUsage(object sender, RoutedEventArgs e) { if (UsageList.SelectedItem is UsageRowItem usage) UsageRequested?.Invoke(usage.Reference.LineNumber); }
  private void OnGameFolder(object sender, RoutedEventArgs e) => GameFolderRequested?.Invoke(this, EventArgs.Empty);
  private void OnClosing(object? sender, CancelEventArgs e)
  {
    if (!TryLeave("Ressourcenfenster schließen")) { e.Cancel = true; return; }
    WindowGeometry.Save(this, "resources", ListColumn.ActualWidth / Math.Max(1, BodyGrid.ActualWidth - 6), UsageRow.ActualHeight);
  }
  private void UpdateEnabled()
  {
    bool enabled = _entry?.IsResolved == true;
    EditTab.IsEnabled = enabled; XmlTab.IsEnabled = enabled;
    ApplyButton.IsEnabled = enabled; DiscardButton.IsEnabled = enabled; SaveButton.IsEnabled = enabled; SaveAsButton.IsEnabled = enabled;
  }
  private void ConstrainDetailLayout()
  {
    if (BodyGrid.ActualHeight <= 0) return;
    // Keep the actual text usable at minimum height. Metadata stays reachable by
    // scrolling, and the usage splitter cannot consume the entire text area.
    DetailHeaderScroll.MaxHeight = Math.Max(68, Math.Min(220, BodyGrid.ActualHeight - 235));
    UsageRow.MaxHeight = Math.Max(40, BodyGrid.ActualHeight - Math.Min(DetailHeaderScroll.ActualHeight, DetailHeaderScroll.MaxHeight) - 196);
    if (UsageRow.MaxHeight < 85) UsageExpander.IsExpanded = false;
  }
  private void SetStatus(string text) { if (StatusText is not null) StatusText.Text = text; }
  private static void ResetUndo(TextBox editor) { editor.IsUndoEnabled = false; editor.IsUndoEnabled = true; }
  private void ReleaseUnusedHash(string file) { if (!_drafts.Keys.Any(key => string.Equals(key.DllPath, file, StringComparison.OrdinalIgnoreCase))) _fileHashes.Remove(file); }
  private static bool MatchesVariant(ResourceEntry entry, string query, int kind, int ids) =>
      (kind == 0 || entry.Identity?.Kind == (kind == 1 ? ResourceKind.String : ResourceKind.Infocard))
      && (query.Length == 0 || ids.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Text.Contains(query, StringComparison.OrdinalIgnoreCase));
  private static string LanguageName(ushort language) { try { return $"{CultureInfo.GetCultureInfo(language).DisplayName} ({language})"; } catch (CultureNotFoundException) { return $"Sprache {language}"; } }
  private static int[] SegmentParagraphs(string xml)
  {
    var paragraphs = new List<int>();
    int paragraph = 1;
    using var input = new StringReader(xml);
    using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
    while (reader.Read())
    {
      if (reader.NodeType != XmlNodeType.Element) continue;
      if (reader.LocalName.Equals("PARA", StringComparison.OrdinalIgnoreCase)) paragraph++;
      if (reader.LocalName.Equals("TEXT", StringComparison.OrdinalIgnoreCase)) paragraphs.Add(paragraph);
    }
    return paragraphs.ToArray();
  }
  private sealed record LanguageChoice(ResourceEntry Entry)
  {
    public string Label => Entry.Identity is { } id ? $"{LanguageName(id.Language)} · {id.Kind}" : "Keine Sprache";
    public override string ToString() => Label;
  }
  private sealed record DllChoice(string? Path, int Slot)
  {
    public string Label => Path is null ? "Alle DLLs" : $"{Slot} · {System.IO.Path.GetFileName(Path)}";
    public override string ToString() => Label;
  }
  private sealed record UsageRowItem(IdsReference Reference)
  {
    public string Location => $"{Path.GetFileName(Reference.FilePath)} · [{Reference.SectionName}] {Reference.SectionNickname} · Zeile {Reference.LineNumber} · {Reference.Role}";
    public string Description => Reference.SourceText + (Reference.Provenance is { Length: > 0 } path ? "\nBezug: " + path : "");
  }
  private sealed record ResourceRow(int Ids, ResourceEntry Entry, IReadOnlyList<IdsReference> References, bool HasDraft, string? DllPath, IReadOnlyList<ResourceEntry> Variants)
  {
    public string IdLabel => Ids + (HasDraft ? " *" : "");
    public string Preview => Entry.IsResolved ? Entry.Text.Replace('\r', ' ').Replace('\n', ' ') : Entry.StatusText;
    public string Categories => string.Join(", ", References.Select(item => item.Category).Distinct());
    public string Detail => $"{Path.GetFileName(DllPath) ?? "DLL nicht aufgelöst"} · {(References.Count == 0 ? "Keine Verwendung in aktueller Mission" : Categories)}";
  }
}
