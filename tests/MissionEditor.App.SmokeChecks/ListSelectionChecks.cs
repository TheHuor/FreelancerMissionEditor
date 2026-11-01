using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

internal static partial class Program
{
  private static void CheckListSelection ()
  {
    var grid = new GridView ();
    grid.Columns.Add (new GridViewColumn { Header = "Beziehung", DisplayMemberBinding = new Binding ("Relation"), Width = 130 });
    grid.Columns.Add (new GridViewColumn { Header = "Trigger", DisplayMemberBinding = new Binding ("Name"), Width = 210 });
    var list = new ListView
    {
      View = grid,
      ItemsSource = new[] { new { Relation = "deaktiviert", Name = "CF20bonus79-mission3" }, new { Relation = "aktiviert", Name = "CF20bonussaveA5" } },
      SelectedIndex = 0
    };
    var plain = new ListBox { ItemsSource = new[] { "Ressourcentext", "Weitere Ressource" }, SelectedIndex = 0 };
    var other = new Button { Content = "Fokus außerhalb der Liste" };
    var panel = new StackPanel ();
    panel.Children.Add (list);
    panel.Children.Add (plain);
    panel.Children.Add (other);
    var dialog = new Window { Content = panel, Width = 440, Height = 260, ShowInTaskbar = false };
    dialog.Show ();
    dialog.UpdateLayout ();
    var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex (0);
    var plainRow = (ListBoxItem)plain.ItemContainerGenerator.ContainerFromIndex (0);
    foreach (var focused in new[] { true, false })
    {
      if (focused) row.Focus (); else other.Focus ();
      dialog.UpdateLayout ();
      foreach (var item in new ListBoxItem[] { row, plainRow })
      {
        var border = (Border)item.Template.FindName ("Selection", item);
        if (!Equals (border.Background, Application.Current.FindResource ("SelectionBrush")) ||
            !Equals (border.BorderBrush, Application.Current.FindResource ("SelectionAccentBrush")))
          throw new Exception ("List selection must retain its dark background and accent with and without focus");
      }
      SavePreview (dialog, focused ? "list-selection-focused" : "list-selection-inactive");
    }
    var presenter = (GridViewRowPresenter)VisualTreeHelper.GetChild ((Border)row.Template.FindName ("Selection", row), 0);
    if (presenter.Columns != grid.Columns || presenter.ActualWidth <= 0)
      throw new Exception ("Selection template must retain the GridView columns");
    list.ItemsSource = Array.Empty<object> ();
    dialog.UpdateLayout ();
    dialog.Close ();
    Console.WriteLine ("PASS ListBox and GridView selections stay readable with and without focus; GridView columns are preserved");
  }
}
