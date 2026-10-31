using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MissionEditor.App.ViewModels;
using MissionEditor.Core.Graph;
using Microsoft.Win32;

namespace MissionEditor.App;

/// <summary>Main window: trigger list, graph, details and findings.</summary>
public partial class MainWindow : Window
{
    private const string FileFilter = "Missions-INI (*.ini)|*.ini|Alle Dateien (*.*)|*.*";

    /// <summary>Creates the window; command line: [file] [trigger to select].</summary>
    public MainWindow()
    {
        InitializeComponent();
        string[] arguments = Environment.GetCommandLineArgs();
        if (arguments.Length > 1)
        {
            Loaded += (_, _) =>
            {
                LoadFile(arguments[1]);
                if (arguments.Length > 2)
                    ViewModel.SelectByName(arguments[2]);
            };
        }
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void LoadFile(string path)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            ViewModel.Load(path);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Datei kann nicht geladen werden", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void OnOpen(object sender, ExecutedRoutedEventArgs args)
    {
        var dialog = new OpenFileDialog { Filter = FileFilter };
        if (ViewModel.Script?.FilePath is { } current)
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        if (dialog.ShowDialog(this) == true)
            LoadFile(dialog.FileName);
    }

    private void OnReload(object sender, ExecutedRoutedEventArgs args)
    {
        if (ViewModel.Script?.FilePath is { } path)
            LoadFile(path);
    }

    private void OnSaveAs(object sender, ExecutedRoutedEventArgs args)
    {
        var dialog = new SaveFileDialog { Filter = FileFilter, FileName = Path.GetFileName(ViewModel.Script?.FilePath ?? "mission.ini") };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            ViewModel.SaveAs(dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Speichern fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCanUseScript(object sender, CanExecuteRoutedEventArgs args) => args.CanExecute = ViewModel.Script is not null;

    private void OnFocusFilter(object sender, ExecutedRoutedEventArgs args)
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
    }

    private void OnFileDrop(object sender, DragEventArgs args)
    {
        if (args.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            LoadFile(files[0]);
    }

    private void OnTriggerListSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (TriggerList.SelectedItem is not null)
            TriggerList.ScrollIntoView(TriggerList.SelectedItem);
    }

    // Centres the selected node in the visible part of the graph once the new size is measured.
    private void OnGraphRedrawn(object? sender, Rect centerBounds)
    {
        Dispatcher.BeginInvoke(() =>
        {
            double zoom = ZoomSlider.Value;
            GraphScroller.ScrollToHorizontalOffset(Math.Max(0, (centerBounds.X + centerBounds.Width / 2) * zoom - GraphScroller.ViewportWidth / 2));
            GraphScroller.ScrollToVerticalOffset(Math.Max(0, (centerBounds.Y + centerBounds.Height / 2) * zoom - GraphScroller.ViewportHeight / 2));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnGraphNodeClicked(object? sender, TriggerNode node) => ViewModel.SelectNode(node);

    private void OnReferenceClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ReferenceItem reference })
            ViewModel.SelectNode(reference.Other);
    }

    private void OnFindingDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (sender is DataGrid { SelectedItem: FindingItem { Node: { } node } })
            ViewModel.SelectNode(node);
    }

    private void OnGraphMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value * (args.Delta > 0 ? 1.1 : 1 / 1.1), ZoomSlider.Minimum, ZoomSlider.Maximum);
        args.Handled = true;
    }
}
