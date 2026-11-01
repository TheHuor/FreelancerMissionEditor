using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using MissionEditor.App;
using MissionEditor.App.Controls;
using MissionEditor.App.ViewModels;
using MissionEditor.Core.Model;
using MissionEditor.TestFixtures;

internal static partial class Program
{
  private static void CheckSourceWorkspace (MainWindow owner)
  {
    string path = Path.Combine (Path.GetTempPath (), "source-workspace-" + Guid.NewGuid () + ".ini");
    File.WriteAllText (path, "[Trigger]\nnickname = compact\nCnd_True = no_params\nAct_PopUpDialog = 123, 456\nAct_SetTitle = 789\n");
    try
    {
      var vm = new MainViewModel ();
      vm.Load (path);
      if (vm.IsGraphView || vm.SourceLines.Any (line => line.IsCaption) || vm.SourceLines.Count != 5)
        throw new Exception ("Source is default workspace and contains only physical INI lines");
      vm.SelectedSourceLine = vm.SourceLines.Single (line => line.Number == 4);
      if (vm.SourceContextLinks.Count != 2) throw new Exception ("Popup context retains distinct title and text");
      vm.SelectedResourceLink = vm.SourceContextLinks[1];
      vm.IsGraphView = true; vm.IsGraphView = false;
      if (vm.SelectedSourceLine.Number != 4 || vm.SelectedResourceLink.Ids != 456)
        throw new Exception ("Graph switching must preserve code and resource selection");
      vm.ReplaceSection (vm.Script!.Document.Sections[0], "[Trigger]\nnickname = compact\nCnd_True = no_params\nAct_PopUpDialog = 123, 999\n");
      if (vm.SourceContextLinks.Any (link => link.Ids == 456)) throw new Exception ("Old resource context must not survive a document edit");
      vm.UndoCommand.Execute (null);
      if (!vm.ResourceReferences.Any (reference => reference.Ids == 456)) throw new Exception ("Undo restores references");
      var source = new SourceWindow (vm) { Owner = owner };
      source.Show (); source.UpdateLayout ();
      SavePreview (source, "source-workspace-detached");
      source.Close ();
      CheckSourceWorkspaceInteractions (owner);
      Console.WriteLine ("PASS physical source rows, distinct context, preserved mode selection and undo refresh");
    }
    finally { File.Delete (path); }
  }

  private static void CheckSourceWorkspaceInteractions (MainWindow owner)
  {
    using var fixture = new ResourceDllFixture ();
    var resources = GameData.Load (fixture.Root).Texts!;
    ResourceWriter.Save ([ResourceDraft.Create (resources.Resolve (2, 1033), "It must have been an audience for the Renaissance to listen to our requests.\n\nBut they are the first of a clever species that will help us."),
        ResourceDraft.Create (resources.Resolve (3, 1033), "The Renaissance")]);
    string path = Path.Combine (fixture.Root, "context.ini");
    File.WriteAllText (path, "[Trigger]\nnickname = CF20bonus79-mission3\nCnd_True = no_params\nAct_PopUpDialog = 3, 2, CLOSE\nAct_SetTitle = 3\n"
        + string.Join ("\n", Enumerable.Range (1, 100).Select (index => "; Originalkommentar " + index)) + "\n");
    var window = new MainWindow { Owner = owner, WindowState = WindowState.Normal, Width = 1400, Height = 900 };
    var vm = (MainViewModel)window.DataContext;
    vm.ApplyGameFolder (new GameFolderSettings { GameFolder = fixture.Root }); vm.Load (path);
    window.Show (); vm.ShowSourceLine (4); window.UpdateLayout ();
    static IEnumerable<DependencyObject> Descendants (DependencyObject parent)
    {
      for (int index = 0; index < VisualTreeHelper.GetChildrenCount (parent); index++)
      {
        DependencyObject child = VisualTreeHelper.GetChild (parent, index); yield return child;
        foreach (var descendant in Descendants (child)) yield return descendant;
      }
    }
    var row = Descendants (window).OfType<SourceLineView> ().Single (view => view.Line?.Number == 4);
    var code = ((Grid)row.Content).Children.OfType<TextBlock> ().Single (view => view.Name == "CodeText");
    code.Inlines.OfType<Hyperlink> ().Last ().RaiseEvent (new RoutedEventArgs (Hyperlink.ClickEvent));
    if (vm.SelectedResourceLink?.Ids != 2 || window.OwnedWindows.OfType<ResourceBrowserWindow> ().Any ())
      throw new Exception ("Inline token selects exact context without automatically opening the resource editor");
    var selected = vm.SelectedSourceLine;
    var preview = (ListBox)window.FindName ("SectionPreview");
    var sourceScroller = Descendants (preview).OfType<ScrollViewer> ().First ();
    sourceScroller.ScrollToVerticalOffset (12); window.UpdateLayout ();
    double sourceOffset = sourceScroller.VerticalOffset;
    ((Button)window.FindName ("GraphModeButton")).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent)); window.UpdateLayout ();
    ((Button)window.FindName ("SourceModeButton")).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent)); window.UpdateLayout ();
    if (vm.SelectedSourceLine != selected || vm.SelectedResourceLink?.Ids != 2)
      throw new Exception ("Actual workspace buttons preserve source and resource selection");
    if (sourceOffset <= 0 || Math.Abs (sourceScroller.VerticalOffset - sourceOffset) > .1)
      throw new Exception ("Workspace switching must preserve source scroll position");
    sourceScroller.ScrollToTop (); window.UpdateLayout ();
    SavePreview (window, "source-workspace-populated-main");
    var inspector = Descendants (window).OfType<SourceContextView> ().Single ();
    var editResource = Descendants (inspector).OfType<Button> ().Single (button => Equals (button.Content, "Ressource bearbeiten…"));
    editResource.RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent));
    var browser = window.OwnedWindows.OfType<ResourceBrowserWindow> ().Single ();
    if (((TextBlock)browser.FindName ("IdText")).Text != "IDS 2") throw new Exception ("Explicit editor action must open selected ID");
    browser.Close ();
    window.Width = 1120; window.Height = 700; window.UpdateLayout ();
    SavePreview (window, "source-workspace-populated-minimum");
    var contextScroller = (ScrollViewer)inspector.Content;
    contextScroller.ScrollToBottom (); window.UpdateLayout ();
    var editBounds = editResource.TransformToAncestor (inspector).TransformBounds (new Rect (editResource.RenderSize));
    if (editBounds.Bottom > inspector.ActualHeight + 2 || editBounds.Top < -2)
      throw new Exception ("Inspector edit actions must remain reachable by scrolling at minimum size");
    var source = window.OpenSource (); source.SelectLine (4); source.UpdateLayout ();
    SavePreview (source, "source-workspace-populated-detached");
    source.Close (); window.Close ();
    Console.WriteLine ("PASS actual inline context selection, workspace toggles, explicit resource action and populated layouts");
  }
}
