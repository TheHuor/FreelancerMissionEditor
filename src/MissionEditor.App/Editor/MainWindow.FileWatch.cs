using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MissionEditor.App;

// Notices when another program (typically the AI agent) changes the open file and offers to reload it.
public partial class MainWindow
{
  private FileSystemWatcher? _watcher;
  private DateTime? _knownWriteTime;
  private DispatcherTimer? _diskCheckTimer;

  /// <summary>True while the "changed on disk" banner is shown (for tests).</summary>
  public bool IsChangedOnDiskBannerVisible => ChangedOnDiskBanner.Visibility == Visibility.Visible;

  // Starts watching the file that is now open; call after loading or "save as".
  private void WatchFile (string path)
  {
    _watcher?.Dispose ();
    _watcher = null;
    HideDiskChange ();
    RememberDiskState ();
    string? folder = Path.GetDirectoryName (Path.GetFullPath (path));
    if (folder is null || !Directory.Exists (folder))
      return;
    _watcher = new FileSystemWatcher (folder, Path.GetFileName (path))
    {
      NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
    };
    _watcher.Changed += OnFileEvent;
    _watcher.Created += OnFileEvent;
    _watcher.Renamed += OnFileEvent;
    _watcher.EnableRaisingEvents = true;
  }

  // The state on disk that the editor knows about (after loading or saving), so its own saves raise no banner.
  private void RememberDiskState () =>
      _knownWriteTime = ViewModel.FilePath is { } path && File.Exists (path) ? File.GetLastWriteTimeUtc (path) : null;

  // Watcher events come in bursts on a worker thread; check once things have settled.
  private void OnFileEvent (object sender, FileSystemEventArgs args) => Dispatcher.BeginInvoke (() =>
  {
    _diskCheckTimer ??= new DispatcherTimer (TimeSpan.FromMilliseconds (500), DispatcherPriority.Background, (_, _) =>
      {
        _diskCheckTimer!.Stop ();
        CheckDiskState ();
      }, Dispatcher);
    _diskCheckTimer.Stop ();
    _diskCheckTimer.Start ();
  });

  /// <summary>Compares the file on disk with the known state and shows the banner when it changed (also used by tests).</summary>
  public void CheckDiskState ()
  {
    if (ViewModel.FilePath is not { } path || !File.Exists (path))
      return;
    if (File.GetLastWriteTimeUtc (path) == _knownWriteTime)
      return;
    ChangedOnDiskText.Text = ViewModel.IsModified
        ? $"{Path.GetFileName (path)} wurde außerhalb des Editors geändert (z. B. vom KI-Agenten). Neu laden verwirft deine ungespeicherten Änderungen."
        : $"{Path.GetFileName (path)} wurde außerhalb des Editors geändert (z. B. vom KI-Agenten).";
    ChangedOnDiskBanner.Visibility = Visibility.Visible;
  }

  private void HideDiskChange () => ChangedOnDiskBanner.Visibility = Visibility.Collapsed;

  private void OnIgnoreDiskChange (object sender, RoutedEventArgs args)
  {
    // Ignoring means: this state on disk is known now; a later change shows the banner again.
    RememberDiskState ();
    HideDiskChange ();
  }

  private void OnReloadAfterDiskChange (object sender, RoutedEventArgs args)
  {
    if (!CanLeaveResources ("Mission neu laden"))
      return;
    if (ViewModel.FilePath is not { } path)
      return;
    if (ViewModel.IsModified && MessageBox.Show (this, "Deine ungespeicherten Änderungen gehen verloren. Trotzdem neu laden?", "Neu laden",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
      return;
    try
    {
      ViewModel.Load (path);
      WatchFile (path);
      ViewModel.ReportStatus ($"Neu geladen · {ViewModel.StatusText}");
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
    {
      MessageBox.Show (this, exception.Message, "Datei kann nicht geladen werden", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }
}
