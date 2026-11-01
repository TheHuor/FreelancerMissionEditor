using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using MissionEditor.App;
using MissionEditor.App.Controls;
using MissionEditor.App.ViewModels;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;

internal static partial class Program
{
  private static void CheckDefinitionEditing (MainWindow owner)
  {
    string folder = Path.Combine (Path.GetTempPath (), "definition-ui-" + Guid.NewGuid ());
    Directory.CreateDirectory (folder);
    string path = Path.Combine (folder, "origin.ini");
    string text = "[Object]\nnickname = origin\nids_name = 123\n\n[Zone]\nnickname = preserved\n";
    File.WriteAllText (path, text);
    var reference = new IdsReference (123, "Basis · Name", "Name", null, 0, "Trigger", "start", 1, 0, "Cnd_BaseEnter = origin")
        { DefinitionPath = path, DefinitionLineNumber = 1 };
    var link = new ResourceLinkItem (reference, null);
    var line = new SectionLineItem (1, reference.SourceText, [])
    { ResourceLinks = [link, new ResourceLinkItem (reference with { Ids = 124, Role = "Basis · Infocard" }, null)] };
    var view = new SourceLineView { Line = line };
    int origins = ((StackPanel)view.Content).Children.OfType<TextBlock> ().SelectMany (item => item.Inlines.OfType<Hyperlink> ()).Count ();
    if (origins != 1) throw new Exception ("One definition must have one compact edit link for name and infocard");
    void Edit (string label, string replacement)
    {
      owner.Dispatcher.BeginInvoke (() =>
      {
        var editor = owner.OwnedWindows.OfType<SectionEditorWindow> ().Single ();
        if (editor.Text.Contains ("[Zone]")) throw new Exception ("Definition editor must show only the defining section");
        var layout = (DockPanel)editor.Content;
        layout.Children.OfType<TextBox> ().Single ().Text = replacement;
        SavePreview (editor, "definition-section-editor");
        layout.Children.OfType<StackPanel> ().Single ().Children.OfType<Button> ()
            .Single (button => button.Content.ToString ()!.StartsWith (label)).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent));
      });
      typeof (MainWindow).GetMethod ("OnDefinitionRequested", BindingFlags.NonPublic | BindingFlags.Instance)!
          .Invoke (owner, [owner, new RoutedEventArgs (SourceLineView.DefinitionRequestedEvent, link)]);
    }
    var vm = (MainViewModel)owner.DataContext;
    string? previous = vm.FilePath;
    try
    {
      Edit ("Speichern", "[Object]\nnickname = origin\nids_name = 456\n");
      if (IniDocument.Load (path).Sections[0].Find ("ids_name")!.GetValue (0) != "456"
          || !File.ReadAllText (path).Contains ("[Zone]\nnickname = preserved") || Directory.GetFiles (folder, "*.bak").Length != 1)
        throw new Exception ("Definition dialog must save only its section and create backup");
      File.WriteAllText (path, text);
      vm.Load (path);
      Edit ("Übernehmen", "[Object]\nnickname = origin\nids_name = 789\n");
      if (!vm.IsModified || vm.Script!.Document.Sections[0].Find ("ids_name")!.GetValue (0) != "789" || File.ReadAllText (path) != text)
        throw new Exception ("Current-mission definition edit must stay in the unsaved document");
      vm.UndoCommand.Execute (null);
      if (vm.IsModified) throw new Exception ("Mission undo must undo definition editing");
      Console.WriteLine ("PASS compact definition link, section-only external save/backup, live mission edit and undo");
    }
    finally
    {
      if (previous is not null && File.Exists (previous)) vm.Load (previous);
      Directory.Delete (folder, true);
    }
  }
}
