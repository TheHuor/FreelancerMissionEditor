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
  private static void CheckRdlSourceViewer(Window owner)
  {
    using var fixture = new ResourceDllFixture();
    var resources = GameData.Load(fixture.Root).Texts!;
    const string raw = "<RDL><TRA bold=\"true\" color=\"red\"/><TEXT>Alpha </TEXT><TRA bold=\"false\"/><TEXT>Beta</TEXT><PARA/><TEXT>Gamma</TEXT></RDL>";
    ResourceWriter.Save([ResourceDraft.Create(resources.Resolve(3, 1033), raw)]);
    string path = Path.Combine(fixture.Root, "viewer.ini");
    File.WriteAllText(path, "[Trigger]\nnickname = viewer\nAct_SetTitle = 3\nAct_SetOffer = 1\n");
    var window = new MainWindow { Owner = owner, Width = 1400, Height = 900 };
    var vm = (MainViewModel)window.DataContext;
    vm.ApplyGameFolder(new GameFolderSettings { GameFolder = fixture.Root }); vm.Load(path);
    window.Show(); vm.ShowSourceLine(3);
    ((TabItem)window.FindName("ContextTab")).IsSelected = true; window.UpdateLayout(); // texts show on click; the findings come first
    static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
      for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
      { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    var viewer = Children(window).OfType<RdlTextViewer>().Single();
    var rich = Children(viewer).OfType<RichTextBox>().Single();
    var xml = Children(viewer).OfType<TextBox>().Single();
    if (!rich.IsReadOnly || !xml.IsReadOnly || viewer.Text != raw || rich.Visibility != Visibility.Visible
        || vm.SourceContextText != "Alpha Beta\nGamma" || vm.SelectedResourceLink!.Label.Contains("<RDL")
        || rich.Document.Blocks.Count != 2)
      throw new Exception("Source inspector must display RDL strings as formatted read-only text with real paragraphs");
    var first = (Run)((Paragraph)rich.Document.Blocks.FirstBlock!).Inlines.FirstInline!;
    if (first.FontWeight != FontWeights.Bold || ((SolidColorBrush)first.Foreground).Color != Color.FromRgb(0xBF, 0x1D, 0x1D))
      throw new Exception("Source RDL viewer must render FL colors and bold text");
    Children(viewer).OfType<Button>().Single(button => Equals(button.Content, "XML")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (xml.Visibility != Visibility.Visible || xml.Text != raw) throw new Exception("Source XML mode must show exact original content read-only");
    Children(viewer).OfType<Button>().Single(button => Equals(button.Content, "Ansicht")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (rich.Visibility != Visibility.Visible || vm.IsModified) throw new Exception("Viewer switches must not edit the mission");
    SavePreview(window, "source-rdl-viewer");
    var detached = window.OpenSource(); detached.SelectLine(3); detached.UpdateLayout();
    if (Children(detached).OfType<RdlTextViewer>().Single().Text != raw) throw new Exception("Detached source must use the same RDL viewer");
    detached.Close();
    vm.ShowSourceLine(4); window.UpdateLayout();
    if (rich.Visibility != Visibility.Collapsed || xml.Text != vm.SourceContextText || vm.SelectedResourceLink?.Ids != 1) throw new Exception("Plain resource selection must replace the previous formatted content");
    viewer.Text = "<RDL><TEXT>broken";
    if (xml.Visibility != Visibility.Visible || rich.Document.Blocks.Count != 0) throw new Exception("Malformed RDL must show original XML without stale rendered content");
    window.Close();
    Console.WriteLine("PASS source and detached inspectors render RDL read-only, show exact XML on demand and retain plain/malformed resource handling");
  }
}
