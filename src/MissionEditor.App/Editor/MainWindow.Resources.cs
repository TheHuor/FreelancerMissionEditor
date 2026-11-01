using System.Windows;
using MissionEditor.App.Controls;
using MissionEditor.App.ViewModels;

namespace MissionEditor.App;

public partial class MainWindow
{
  private ResourceBrowserWindow? _resourceWindow;
  private SourceWindow? _sourceWindow;
  private Rect? _pendingGraphCenter;

  private void InitializeResourceWindows ()
  {
    AddHandler (SourceLineView.ResourceRequestedEvent, new RoutedEventHandler (OnSourceResourceRequested));
    AddHandler (SourceLineView.ResourceSelectedEvent, new RoutedEventHandler (OnSourceResourceSelected));
    AddHandler (SourceLineView.DefinitionRequestedEvent, new RoutedEventHandler (OnDefinitionRequested));
    ViewModel.DocumentContextChanged += (_, _) =>
      _resourceWindow?.RefreshContext (ViewModel.Script, ViewModel.GameData, ViewModel.FilePath);
    Closed += (_, _) =>
    {
      _resourceWindow?.Close ();
      _sourceWindow?.Close ();
    };
  }

  private bool CanLeaveResources (string reason) => _resourceWindow?.TryLeave (reason) ?? true;

  private void OnOpenResources (object sender, RoutedEventArgs args) => OpenResources ();

  /// <summary>Opens the single resource browser, optionally selecting a reference even if missing.</summary>
  public void OpenResources (int? ids = null)
  {
    if (_resourceWindow is null)
    {
      var browser = new ResourceBrowserWindow { Owner = this };
      browser.Closed += (_, _) => _resourceWindow = null;
      browser.UsageRequested += ShowResourceUsage;
      browser.ResourceSaved += (_, _) => ViewModel.RefreshResourceHints ();
      browser.GameFolderRequested += (_, _) => OnGameFolder (this, new RoutedEventArgs ());
      _resourceWindow = browser;
      browser.RefreshContext (ViewModel.Script, ViewModel.GameData, ViewModel.FilePath);
      browser.Show ();
    }
    if (ids is { } id)
      _resourceWindow.NavigateToId (id);
    ActivateWindow (_resourceWindow);
  }

  private void OnSourceResourceRequested (object sender, RoutedEventArgs args)
  {
    if (args.OriginalSource is ResourceLinkItem link)
    {
      OpenResources (link.Ids);
      args.Handled = true;
    }
  }

  private void OnOpenSource (object sender, RoutedEventArgs args) => OpenSource ();

  /// <summary>Shows the single large source window alongside the resource browser.</summary>
  public SourceWindow OpenSource ()
  {
    if (_sourceWindow is null)
    {
      var source = new SourceWindow (ViewModel) { Owner = this };
      source.AddHandler (SourceLineView.ResourceRequestedEvent, new RoutedEventHandler (OnSourceResourceRequested));
      source.AddHandler (SourceLineView.ResourceSelectedEvent, new RoutedEventHandler (OnSourceResourceSelected));
      source.AddHandler (SourceLineView.DefinitionRequestedEvent, new RoutedEventHandler (OnDefinitionRequested));
      source.EditSectionRequested += (_, _) => OnEditSelectedSourceSection (this, new RoutedEventArgs ());
      source.Closed += (_, _) => _sourceWindow = null;
      _sourceWindow = source;
      source.Show ();
    }
    ActivateWindow (_sourceWindow);
    return _sourceWindow;
  }

  private void ShowResourceUsage (int lineNumber)
  {
    ViewModel.IsGraphView = false;
    if (ViewModel.ShowSourceLine (lineNumber) is not { } line)
      return;
    SectionPreview.SelectedItem = line;
    SectionPreview.ScrollIntoView (line);
    OpenSource ().SelectLine (lineNumber);
  }

  private void OnSourceResourceSelected (object sender, RoutedEventArgs args)
  {
    if (args.OriginalSource is not ResourceLinkItem link) return;
    ViewModel.InspectResource (link);
    ContextTab.IsSelected = true;
    args.Handled = true;
  }

  private void OnSourceMode (object sender, RoutedEventArgs args) => ViewModel.IsGraphView = false;
  private void OnGraphMode (object sender, RoutedEventArgs args)
  {
    ViewModel.IsGraphView = true;
    if (_pendingGraphCenter is { } center) OnGraphRedrawn (this, center);
  }
  private void OnEditSelectedSourceSection (object sender, RoutedEventArgs args) =>
      OnEditSection (new System.Windows.Controls.Button { Tag = ViewModel.SelectedSourceSection }, args);
  private void OnDeleteSelectedSourceSection (object sender, RoutedEventArgs args) =>
      OnDeleteSection (new System.Windows.Controls.Button { Tag = ViewModel.SelectedSourceSection }, args);
  private void OnSourceLineSelectionChanged (object sender, System.Windows.Controls.SelectionChangedEventArgs args)
  {
    if (FindingsTab is not null) FindingsTab.IsSelected = true;
  }

  private Core.Ini.IniSection? ResolveSourceSection (object sender) => sender is FrameworkElement element
      ? element.Tag as Core.Ini.IniSection ?? (element.DataContext as SectionLineItem)?.Section : null;

  private static void ActivateWindow (Window window)
  {
    if (window.WindowState == WindowState.Minimized)
      window.WindowState = WindowState.Normal;
    window.Activate ();
  }

  private void OnDefinitionRequested (object sender, RoutedEventArgs args)
  {
    if (args.OriginalSource is not ResourceLinkItem { Reference.DefinitionPath: { } path } link)
      return;
    args.Handled = true;
    try
    {
      bool currentMission = string.Equals (path, ViewModel.FilePath, StringComparison.OrdinalIgnoreCase) && ViewModel.Script is not null;
      Core.Ini.IniSectionFileEdit? external = currentMission ? null
          : Core.Ini.IniSectionFileEdit.Open (path, link.Reference.DefinitionLineNumber ?? 1, link.Ids);
      var document = currentMission ? ViewModel.Script!.Document : external!.Document;
      var section = external?.Section ?? document.Sections.FirstOrDefault (item => item.FirstLineNumber == link.Reference.DefinitionLineNumber)
          ?? throw new System.IO.InvalidDataException ("Die Definition wurde verschoben. Bitte erneut öffnen.");
      string? backup = null;
      var editor = new SectionEditorWindow (
          $"Definition bearbeiten · [{section.Name}] {section.Find ("nickname")?.GetValue (0)}",
          Core.Ini.IniDocument.GetEditableText (section),
          $"Datei: {path}\nSektion ab Zeile {section.FirstLineNumber}. " + (currentMission
              ? "Übernehmen ändert die offene Mission. Strg+Z macht die Änderung rückgängig; Strg+S speichert die Mission."
              : "Speichern schreibt nur diese Sektion in die Herkunfts-INI und erstellt eine Sicherung. Andere Sektionen bleiben erhalten."),
          text =>
          {
            try
            {
              if (external is not null) backup = external.Save (text);
              else _ = Core.Ini.IniSectionFileEdit.ApplySection (document, section, text);
              return null;
            }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException or ArgumentException)
            { return error.Message; }
          }, currentMission ? "Übernehmen · Strg+Enter" : "Speichern · Strg+Enter")
      { Owner = this, Width = 1000, Height = 700 };
      if (editor.ShowDialog () != true) return;
      if (currentMission)
        ViewModel.ReplaceSection (section, editor.Text);
      else
      {
        if (ViewModel.GameData is { } game) Core.Model.GameData.Invalidate (game.Root);
        ViewModel.RefreshResourceHints ();
        ViewModel.ReportStatus ($"Herkunfts-INI gespeichert: {path} · Sicherung: {backup}");
      }
    }
    catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
    {
      ThemedMessageBox.Show (this, exception.Message, "Herkunftsdefinition kann nicht bearbeitet werden", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }
}
