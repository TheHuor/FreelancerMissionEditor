using System.IO;
using System.Windows;
using System.Windows.Media;
using MissionEditor.Core.Model;
using Microsoft.Win32;

namespace MissionEditor.App;

/// <summary>Chooses which Freelancer installation provides the game data: automatic or a fixed folder.</summary>
public partial class GameFolderWindow : Window
{
  private readonly GameFolderSettings _settings;
  private readonly string? _missionPath;
  private bool _initialized;

  /// <summary>Creates the dialog for the current settings; the open mission shows what "automatic" finds.</summary>
  public GameFolderWindow (GameFolderSettings settings, string? missionPath)
  {
    InitializeComponent ();
    _settings = settings;
    _missionPath = missionPath;
    RecentBox.ItemsSource = settings.RecentFolders;
    RecentPanel.Visibility = settings.RecentFolders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    FolderBox.Text = settings.GameFolder.Length > 0 ? settings.GameFolder : settings.RecentFolders.FirstOrDefault () ?? string.Empty;
    (settings.GameFolder.Length > 0 ? FixedOption : AutomaticOption).IsChecked = true;
    _initialized = true;
    Refresh ();
  }

  /// <summary>The chosen settings after "Übernehmen".</summary>
  public GameFolderSettings Result => _settings;

  /// <summary>Chooses a fixed folder (for tests).</summary>
  public void ChooseFolderForTest (string folder)
  {
    FixedOption.IsChecked = true;
    FolderBox.Text = folder;
    Refresh ();
  }

  /// <summary>The status line under the folder (for tests).</summary>
  public string StatusForTest => FolderStatus.Text;

  private string Folder => FolderBox.Text.Trim ();

  private void Refresh ()
  {
    if (!_initialized)
      return;
    bool isFixed = FixedOption.IsChecked == true;
    FolderBox.IsEnabled = isFixed;
    RecentBox.IsEnabled = isFixed;
    if (!isFixed)
    {
      GameData? found = GameData.Locate (_missionPath, null);
      ShowStatus (true, _missionPath is null
          ? "Wird beim Öffnen einer Mission aus deren Pfad ermittelt."
          : found is null ? "Die geöffnete Mission liegt in keiner Freelancer-Installation – Prüfung und Texte entfallen." : $"Gefunden: {found.Summary}");
      OkButton.IsEnabled = true;
      return;
    }
    if (Folder.Length == 0)
    {
      ShowStatus (false, "Ordner wählen.");
      OkButton.IsEnabled = false;
      return;
    }
    if (!GameData.IsInstallation (Folder))
    {
      ShowStatus (false, "In diesem Ordner gibt es kein EXE\\freelancer.ini – den Hauptordner der Installation wählen (der mit EXE und DATA).");
      OkButton.IsEnabled = false;
      return;
    }
    GameData? data = GameData.ForRoot (Folder);
    ShowStatus (data is not null, data is null ? "freelancer.ini ließ sich nicht lesen." : $"Gelesen: {data.Summary}");
    OkButton.IsEnabled = data is not null;
  }

  private void ShowStatus (bool good, string text)
  {
    FolderStatus.Text = text;
    FolderStatus.Foreground = (Brush)FindResource (good ? "MutedTextBrush" : "WarningBrush");
  }

  private void OnOptionChanged (object sender, RoutedEventArgs args) => Refresh ();

  private void OnRecentChosen (object sender, RoutedEventArgs args)
  {
    if (RecentBox.SelectedItem is string folder)
      FolderBox.Text = folder;
  }

  private void OnFolderTyped (object sender, RoutedEventArgs args) => Refresh ();

  private void OnBrowse (object sender, RoutedEventArgs args)
  {
    var dialog = new OpenFolderDialog { Title = "Freelancer-Installation wählen (der Ordner mit EXE und DATA)" };
    if (Directory.Exists (Folder))
      dialog.InitialDirectory = Folder;
    if (dialog.ShowDialog (this) == true)
    {
      FixedOption.IsChecked = true;
      FolderBox.Text = dialog.FolderName;
      Refresh ();
    }
  }

  private void OnOk (object sender, RoutedEventArgs args)
  {
    if (FixedOption.IsChecked == true)
    {
      _settings.GameFolder = Path.TrimEndingDirectorySeparator (Path.GetFullPath (Folder));
      _settings.Remember (_settings.GameFolder);
    }
    else
      _settings.GameFolder = string.Empty;
    _settings.Save ();
    DialogResult = true;
  }
}
