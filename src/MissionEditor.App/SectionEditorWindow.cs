using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MissionEditor.App;

/// <summary>A small text editor for one INI section; <see cref="Text"/> holds the result after OK.</summary>
public sealed class SectionEditorWindow : Window
{
    private readonly TextBox _editor;

    /// <summary>Creates the dialog.</summary>
    /// <param name="title">Window title.</param>
    /// <param name="text">The section text to edit.</param>
    /// <param name="note">A line of advice shown above the text.</param>
    public SectionEditorWindow(string title, string text, string note)
    {
        Title = title;
        Width = 760;
        Height = 560;
        MinWidth = 600;
        MinHeight = 400;
        SetResourceReference(StyleProperty, typeof(Window));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _editor = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 6, 0, 8),
        };
        var okButton = new Button { Content = "Übernehmen · Strg+Enter", IsDefault = false, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 8, 0) };
        okButton.Click += (_, _) => Accept();
        var cancelButton = new Button { Content = "Abbrechen", IsCancel = true, Padding = new Thickness(14, 3, 14, 3) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var hint = new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedTextBrush") };
        var layout = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(hint);
        layout.Children.Add(buttons);
        layout.Children.Add(_editor);
        Content = layout;

        // Enter belongs to the text; Ctrl+Enter accepts.
        InputBindings.Add(new KeyBinding(new ActionCommand(Accept), Key.Enter, ModifierKeys.Control));
        Loaded += (_, _) => _editor.Focus();
    }

    /// <summary>The edited text (valid after the dialog returned true).</summary>
    public string Text => _editor.Text;

    private void Accept() => DialogResult = true;

    private sealed class ActionCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
