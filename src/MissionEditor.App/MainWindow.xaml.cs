using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MissionEditor.App.ViewModels;
using MissionEditor.Core.Graph;
using Microsoft.Win32;

namespace MissionEditor.App;

/// <summary>Main window: trigger list, graph, details and findings.</summary>
public partial class MainWindow : Window
{
    private const string FileFilter = "Missions-INI (*.ini)|*.ini|Alle Dateien (*.*)|*.*";

    private Point? _panStart;
    private Point _panStartOffset;

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
        if (!ConfirmDiscard())
            return;
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
        if (ViewModel.FilePath is { } current)
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        if (dialog.ShowDialog(this) == true)
            LoadFile(dialog.FileName);
    }

    private void OnReload(object sender, ExecutedRoutedEventArgs args)
    {
        if (ViewModel.FilePath is { } path)
            LoadFile(path);
    }

    private void OnSaveAs(object sender, ExecutedRoutedEventArgs args)
    {
        var dialog = new SaveFileDialog { Filter = FileFilter, FileName = Path.GetFileName(ViewModel.FilePath ?? "mission.ini") };
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

    private void OnCanSave(object sender, CanExecuteRoutedEventArgs args) => args.CanExecute = ViewModel.IsModified;

    private void OnSave(object sender, ExecutedRoutedEventArgs args) => TrySave();

    private bool TrySave()
    {
        try
        {
            ViewModel.Save();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Speichern fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    // Asks what to do with unsaved changes; false means the caller should stop.
    private bool ConfirmDiscard()
    {
        if (!ViewModel.IsModified)
            return true;
        MessageBoxResult answer = MessageBox.Show(this,
            $"Die Änderungen an {Path.GetFileName(ViewModel.FilePath)} sind noch nicht gespeichert. Jetzt speichern?",
            "Ungespeicherte Änderungen", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => TrySave(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs args)
    {
        if (!ConfirmDiscard())
            args.Cancel = true;
    }

    private void OnEditSection(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: SectionLineItem { Section: { } section } })
            return;
        var editor = new SectionEditorWindow(
            $"[{section.Name}] ab Zeile {section.FirstLineNumber} bearbeiten",
            Core.Ini.IniDocument.GetEditableText(section),
            "Text der Sektion von der Kopfzeile bis zur letzten Angabe. Bei [Trigger] muss 'nickname' direkt nach der Kopfzeile stehen. "
            + "Übernehmen (Strg+Enter) prüft die Datei neu; gespeichert wird erst mit Strg+S, Strg+Z macht die Änderung rückgängig.")
        {
            Owner = this,
        };
        if (editor.ShowDialog() == true && editor.Text != Core.Ini.IniDocument.GetEditableText(section))
            ViewModel.ReplaceSection(section, editor.Text);
    }

    private void OnDeleteSection(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: SectionLineItem { Section: { } section } })
            return;
        (int firstLine, int lastLine) = Core.Ini.IniDocument.GetRemovalRange(section);
        string question = $"[{section.Name}] in den Zeilen {firstLine}–{lastLine} löschen?\n\n{ViewModel.DescribeDeletion(section)}\n\n"
            + "Gespeichert wird erst mit Strg+S; Strg+Z macht das Löschen rückgängig.";
        if (MessageBox.Show(this, question, "Sektion löschen", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            ViewModel.DeleteSection(section);
    }

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

    // The warning sign in the trigger list: select the trigger and show its first finding in the list below.
    private void OnFindingSignClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: TriggerItem item } || item.Findings.Count == 0)
            return;
        ViewModel.SelectNode(item.Node);
        ShowInFindingsGrid(item.Findings.OrderByDescending(finding => finding.Severity).First());
    }

    private void OnDetailFindingClicked(object sender, MouseButtonEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: FindingRow row })
            ShowInFindingsGrid(row.Item);
    }

    private void OnSectionPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control || SectionPreview.SelectedItems.Count == 0)
            return;
        IEnumerable<string> lines = SectionPreview.SelectedItems.Cast<SectionLineItem>().OrderBy(line => line.Number).Select(line => line.Text);
        Clipboard.SetText(string.Join(Environment.NewLine, lines));
        args.Handled = true;
    }

    // Brings the finding's line into the source preview and marks it.
    private void ShowInPreview(FindingItem finding)
    {
        SectionLineItem? line = ViewModel.ShowFinding(finding);
        if (finding.Node is not null)
            Dispatcher.BeginInvoke(() => BringDetailFindingIntoView(finding), System.Windows.Threading.DispatcherPriority.Loaded);
        if (line is null)
            return;
        SectionPreview.SelectedItem = line;
        SectionPreview.ScrollIntoView(line);
    }

    // Scrolls the findings area in the details to the highlighted finding.
    private void BringDetailFindingIntoView(FindingItem finding)
    {
        // SelectedFindings builds new rows on every call, so look the row up among the shown items.
        object? row = DetailFindings.Items.OfType<FindingRow>().FirstOrDefault(candidate => candidate.Item == finding);
        if (row is not null && DetailFindings.ItemContainerGenerator.ContainerFromItem(row) is FrameworkElement container)
            container.BringIntoView();
    }

    private void ShowInFindingsGrid(FindingItem finding)
    {
        ShowInPreview(finding);
        if (finding.Severity < ViewModel.MinimumSeverity)
            ViewModel.MinimumSeverity = finding.Severity;
        FindingsGrid.SelectedItem = finding;
        FindingsGrid.ScrollIntoView(finding);
        FindingsGrid.Focus();
    }

    private void OnFindingDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (sender is DataGrid { SelectedItem: FindingItem finding })
            ShowInPreview(finding);
    }

    // Wheel zooms around the mouse position; Shift+wheel scrolls sideways.
    private void OnGraphMouseWheel(object sender, MouseWheelEventArgs args)
    {
        args.Handled = true;
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            GraphScroller.ScrollToHorizontalOffset(GraphScroller.HorizontalOffset - args.Delta);
            return;
        }
        double factor = args.Delta > 0 ? 1.15 : 1 / 1.15;
        SetZoom(ZoomSlider.Value * factor, args.GetPosition(GraphScroller));
    }

    // Sets the zoom and keeps the graph point under the anchor (viewport coordinates) in place.
    private void SetZoom(double zoom, Point anchor)
    {
        double oldZoom = ZoomSlider.Value;
        double newZoom = Math.Clamp(zoom, ZoomSlider.Minimum, ZoomSlider.Maximum);
        double graphX = (GraphScroller.HorizontalOffset + anchor.X) / oldZoom;
        double graphY = (GraphScroller.VerticalOffset + anchor.Y) / oldZoom;
        ZoomSlider.Value = newZoom;
        GraphScroller.UpdateLayout();
        GraphScroller.ScrollToHorizontalOffset(graphX * newZoom - anchor.X);
        GraphScroller.ScrollToVerticalOffset(graphY * newZoom - anchor.Y);
    }

    private void OnZoomToFit(object sender, RoutedEventArgs args)
    {
        if (GraphCanvas.Width is not > 0 || GraphCanvas.Height is not > 0)
            return;
        double zoom = Math.Min(GraphScroller.ViewportWidth / GraphCanvas.Width, GraphScroller.ViewportHeight / GraphCanvas.Height);
        ZoomSlider.Value = Math.Clamp(zoom, ZoomSlider.Minimum, 1);
        GraphScroller.ScrollToHorizontalOffset(0);
        GraphScroller.ScrollToVerticalOffset(0);
    }

    private void OnZoomReset(object sender, RoutedEventArgs args) =>
        SetZoom(1, new Point(GraphScroller.ViewportWidth / 2, GraphScroller.ViewportHeight / 2));

    // Dragging on empty space (not on a node) moves the view.
    private void OnGraphPanStart(object sender, MouseButtonEventArgs args)
    {
        if (!IsEmptyGraphArea(args.OriginalSource as DependencyObject))
            return;
        _panStart = args.GetPosition(GraphScroller);
        _panStartOffset = new Point(GraphScroller.HorizontalOffset, GraphScroller.VerticalOffset);
        GraphScroller.CaptureMouse();
        GraphScroller.Cursor = Cursors.SizeAll;
        args.Handled = true;
    }

    // True unless the element is (inside) a node, an edge or a scroll bar.
    private bool IsEmptyGraphArea(DependencyObject? element)
    {
        for (DependencyObject? current = element; current is not null && current != GraphScroller; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollBar || current is System.Windows.Shapes.Path)
                return false;
            if (current is FrameworkElement { Parent: Controls.TriggerGraphCanvas })
                return false;
        }
        return true;
    }

    private void OnGraphPanMove(object sender, MouseEventArgs args)
    {
        if (_panStart is not { } start)
            return;
        Vector moved = args.GetPosition(GraphScroller) - start;
        GraphScroller.ScrollToHorizontalOffset(_panStartOffset.X - moved.X);
        GraphScroller.ScrollToVerticalOffset(_panStartOffset.Y - moved.Y);
    }

    private void OnGraphPanEnd(object sender, MouseButtonEventArgs args)
    {
        if (_panStart is null)
            return;
        GraphScroller.ReleaseMouseCapture();
    }

    private void OnGraphPanLost(object sender, MouseEventArgs args)
    {
        _panStart = null;
        GraphScroller.Cursor = null;
    }
}
