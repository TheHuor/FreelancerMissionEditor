using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MissionEditor.App.Controls;
using MissionEditor.App.ViewModels;

namespace MissionEditor.App;

/// <summary>Reusable non-modal section source; resources remain in their own window.</summary>
public sealed class SourceWindow : Window
{
  /// <summary>Requests editing the selected physical source section.</summary>
  public event EventHandler? EditSectionRequested;
  private readonly ListBox _lines = new ()
  {
    SelectionMode = SelectionMode.Extended,
    HorizontalContentAlignment = HorizontalAlignment.Stretch
  };

  /// <summary>Creates the large source view for the same selection as the main editor.</summary>
  public SourceWindow (MainViewModel viewModel)
  {
    SetResourceReference (StyleProperty, typeof (Window));
    // Configure initial window metadata and default/restored geometry.
    Title = "Quelltext";
    Width = 1120;
    Height = 760;
    MinWidth = 840;
    MinHeight = 560;
    DataContext = viewModel;
    WindowGeometry.Restore (this, "source");

    // Root layout container for header, path, and source lines list.
    var panel = new DockPanel { Margin = new Thickness (12) };

    // Section heading bound to the currently selected details title.
    var heading = new TextBlock { FontSize = 18, Margin = new Thickness (0, 0, 0, 8) };
    heading.SetBinding (TextBlock.TextProperty, new Binding (nameof (MainViewModel.DetailsTitle)));
    DockPanel.SetDock (heading, Dock.Top);
    panel.Children.Add (heading);

    // Full source path; trimmed visually, full value available via tooltip.
    var path = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness (0, 0, 0, 8) };
    path.SetBinding (TextBlock.TextProperty, new Binding (nameof (MainViewModel.FilePath)));
    path.SetBinding (ToolTipProperty, new Binding (nameof (MainViewModel.FilePath)));
    DockPanel.SetDock (path, Dock.Top);
    panel.Children.Add (path);

    // Bind line data and render each entry through SourceLineView.
    _lines.SetBinding (ItemsControl.ItemsSourceProperty, new Binding (nameof (MainViewModel.SourceLines)));
    ScrollViewer.SetHorizontalScrollBarVisibility (_lines, ScrollBarVisibility.Auto);
    _lines.SetBinding (ListBox.SelectedItemProperty, new Binding (nameof (MainViewModel.SelectedSourceLine)) { Mode = BindingMode.TwoWay });
    var itemStyle = new Style (typeof (ListBoxItem), (Style)FindResource (typeof (ListBoxItem)));
    itemStyle.Setters.Add (new Setter (Control.PaddingProperty, new Thickness (0)));
    itemStyle.Setters.Add (new Setter (Control.MarginProperty, new Thickness (0)));
    _lines.ItemContainerStyle = itemStyle;
    var row = new FrameworkElementFactory (typeof (SourceLineView));
    row.SetBinding (SourceLineView.LineProperty, new Binding ());
    _lines.ItemTemplate = new DataTemplate (typeof (SectionLineItem)) { VisualTree = row };

    // Support Ctrl+C to copy selected lines in numeric order as plain text.
    _lines.KeyDown += (_, args) =>
    {
      if (args.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control)
        return;

      var text = string.Join (Environment.NewLine, _lines.SelectedItems.Cast<SectionLineItem> ().OrderBy (item => item.Number).Select (item => item.Text));
      if (text.Length > 0)
        Clipboard.SetText (text);

      args.Handled = true;
    };

    var body = new Grid ();
    body.ColumnDefinitions.Add (new ColumnDefinition { Width = new GridLength (1, GridUnitType.Star), MinWidth = 320 });
    body.ColumnDefinitions.Add (new ColumnDefinition { Width = new GridLength (6) });
    body.ColumnDefinitions.Add (new ColumnDefinition { Width = new GridLength (320), MinWidth = 260 });
    body.Children.Add (_lines);
    var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
    Grid.SetColumn (splitter, 1); body.Children.Add (splitter);
    var inspector = new SourceContextView ();
    Grid.SetColumn (inspector, 2); body.Children.Add (inspector);
    panel.Children.Add (body);
    var edit = new Button { Content = "Sektion bearbeiten…", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness (0, 0, 0, 8) };
    edit.Click += (_, _) => EditSectionRequested?.Invoke (this, EventArgs.Empty);
    DockPanel.SetDock (edit, Dock.Top); panel.Children.Insert (0, edit);
    Content = panel;

    // Persist current window geometry when the window is closed.
    Closing += (_, _) => WindowGeometry.Save (this, "source");
  }

  /// <summary>Marks the exact usage line after the section selection has synchronized.</summary>
  public void SelectLine (int lineNumber)
  {
    if (_lines.Items.OfType<SectionLineItem> ().FirstOrDefault (item => item.Number == lineNumber) is { } row)
    {
      _lines.SelectedItem = row;
      _lines.ScrollIntoView (row);
    }
  }
}
