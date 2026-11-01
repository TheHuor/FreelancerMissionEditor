using System.Windows;
using MissionEditor.App.Controls;
using MissionEditor.App.ViewModels;

namespace MissionEditor.App;

public partial class MainWindow
{
  private ResourceBrowserWindow? _resourceWindow;
  private SourceWindow? _sourceWindow;

  private void InitializeResourceWindows ()
  {
    AddHandler (SourceLineView.ResourceRequestedEvent, new RoutedEventHandler (OnSourceResourceRequested));
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
      source.AddHandler (SourceLineView.DefinitionRequestedEvent, new RoutedEventHandler (OnDefinitionRequested));
      source.Closed += (_, _) => _sourceWindow = null;
      _sourceWindow = source;
      source.Show ();
    }
    ActivateWindow (_sourceWindow);
    return _sourceWindow;
  }

  private void ShowResourceUsage (int lineNumber)
  {
    if (ViewModel.ShowSourceLine (lineNumber) is not { } line)
      return;
    SectionPreview.SelectedItem = line;
    SectionPreview.ScrollIntoView (line);
    OpenSource ().SelectLine (lineNumber);
  }

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
      var document = string.Equals (path, ViewModel.FilePath, StringComparison.OrdinalIgnoreCase) && ViewModel.Script is { } script
          ? script.Document : Core.Ini.IniDocument.Load (path);
      var source = new System.Windows.Controls.TextBox
      {
        Text = string.Join (Environment.NewLine, document.Lines.Select (line => line.Text)),
        IsReadOnly = true,
        AcceptsReturn = true,
        FontFamily = new System.Windows.Media.FontFamily ("Consolas"),
        HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
      };
      var viewer = new Window
      {
        Owner = this,
        Title = "Herkunfts-INI · " + path,
        Width = 1000,
        Height = 700,
        Content = source
      };
      viewer.Show ();
      int lineIndex = Math.Clamp ((link.Reference.DefinitionLineNumber ?? 1) - 1, 0, Math.Max (0, source.LineCount - 1));
      int character = source.GetCharacterIndexFromLineIndex (lineIndex);
      source.Select (character, source.GetLineLength (lineIndex));
      source.ScrollToLine (lineIndex);
    }
    catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
    {
      MessageBox.Show (this, exception.Message, "Herkunfts-INI kann nicht gelesen werden", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }
}
