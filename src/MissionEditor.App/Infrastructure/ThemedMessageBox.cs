using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MissionEditor.App;

/// <summary>Application messages using the same accessible dark window chrome as the editor.</summary>
public static class ThemedMessageBox
{
  /// <summary>Shows a modal message, preserving the existing button/result contract.</summary>
  public static MessageBoxResult Show (Window owner, string message, string title, MessageBoxButton buttons, MessageBoxImage image)
  {
    var dialog = new Window
    {
      Owner = owner, Title = title, Width = 560, SizeToContent = SizeToContent.Height,
      MaxHeight = SystemParameters.WorkArea.Height * .85, ResizeMode = ResizeMode.NoResize,
      WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
    };
    dialog.SetResourceReference (FrameworkElement.StyleProperty, typeof (Window));
    MessageBoxResult result = buttons switch
    {
      MessageBoxButton.YesNo => MessageBoxResult.No,
      MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
      _ => MessageBoxResult.OK,
    };
    var panel = new DockPanel { Margin = new Thickness (20) };
    var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness (0, 18, 0, 0) };
    DockPanel.SetDock (actions, Dock.Bottom);
    panel.Children.Add (actions);
    string brush = image switch
    {
      MessageBoxImage.Error => "ErrorBrush", MessageBoxImage.Warning => "WarningBrush", _ => "AccentBrush",
    };
    var heading = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold,
      Foreground = (Brush)owner.FindResource (brush), TextWrapping = TextWrapping.Wrap, Margin = new Thickness (0, 0, 0, 10) };
    DockPanel.SetDock (heading, Dock.Top); panel.Children.Add (heading);
    panel.Children.Add (new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
      HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
      Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } });
    (string Label, MessageBoxResult Result)[] choices = buttons switch
    {
      MessageBoxButton.OKCancel => [("OK", MessageBoxResult.OK), ("Abbrechen", MessageBoxResult.Cancel)],
      MessageBoxButton.YesNo => [("Ja", MessageBoxResult.Yes), ("Nein", MessageBoxResult.No)],
      MessageBoxButton.YesNoCancel => [("Ja", MessageBoxResult.Yes), ("Nein", MessageBoxResult.No), ("Abbrechen", MessageBoxResult.Cancel)],
      _ => [("OK", MessageBoxResult.OK)],
    };
    foreach (var choice in choices)
    {
      var button = new Button { Content = choice.Label, Tag = choice.Result, MinWidth = 90,
        Margin = new Thickness (6, 0, 0, 0), IsDefault = choice == choices[0],
        IsCancel = choice.Result == MessageBoxResult.Cancel || buttons == MessageBoxButton.OK };
      button.Click += (_, _) => { result = choice.Result; dialog.DialogResult = true; };
      actions.Children.Add (button);
    }
    dialog.Content = panel;
    dialog.ShowDialog ();
    return result;
  }
}
