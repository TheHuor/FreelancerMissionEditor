using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MissionEditor.App;
using MissionEditor.App.ViewModels;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;
using MissionEditor.TestFixtures;

internal static partial class Program
{
  private static void CheckResourceCompatibility(Window owner)
  {
    using var fixture = new ResourceDllFixture();
    var game = GameData.Load(fixture.Root);
    const string rdl = "<RDL><TEXT>Popup title</TEXT></RDL>";
    ResourceWriter.Save([ResourceDraft.Create(game.Texts!.Resolve(3, 1033), rdl)]); game.Texts.Reload();
    string mission = Path.Combine(fixture.Root, "compatibility.ini");
    const string source = "[Trigger]\nnickname = compatibility\nCnd_True = no_params\nAct_PopUpDialog = 3, 3, CLOSE\nAct_SetTitle = 3\n";
    File.WriteAllText(mission, source);
    var script = MissionScript.FromDocument(IniDocument.ParseText(source), mission);
    var browser = new ResourceBrowserWindow { Owner = owner };
    browser.RefreshContext(script, game, mission); browser.Show(); browser.NavigateToId(3);
    var label = (TextBlock)browser.FindName("CompatibilityText");
    if (!label.Text.Contains("Gemischt") || !label.Text.Contains("ungeprüft") || !label.Text.Contains("Dialogtitel")
        || !Equals(label.Foreground, browser.FindResource("WarningBrush")))
      throw new Exception("Resource browser must show mixed/unknown uses and a live literal-title warning");
    var uses = (ListBox)browser.FindName("UsageList");
    if (!uses.Items.Cast<object>().Any(item => item.GetType().GetProperty("Location")!.GetValue(item)!.ToString()!.Contains("Anzeige: Klartext")))
      throw new Exception("Individual resource uses must identify the actual rendering mode");
    ((Button)browser.FindName("XmlModeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    ((TextBox)browser.FindName("XmlEditor")).Text = "Plain title";
    if (label.Text.Contains("XML-Tags")) throw new Exception("Draft compatibility warning must disappear when content becomes plain");
    ((TextBox)browser.FindName("XmlEditor")).Text = rdl;
    if (!label.Text.Contains("XML-Tags")) throw new Exception("Draft compatibility warning must reappear for RDL at a literal use");
    ((Button)browser.FindName("DiscardButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    SavePreview(browser, "resource-rendering-warning"); browser.Close();
    var vm = new MainViewModel(); vm.ApplyGameFolder(new GameFolderSettings { GameFolder = fixture.Root }); vm.Load(mission);
    vm.ShowSourceLine(4);
    vm.SelectedResourceLink = vm.SourceContextLinks.Single(link => link.Reference.ParameterIndex == 0);
    if (!vm.SourceContextOrigin.Contains("Klartext") || !vm.SourceContextCompatibility.Contains("XML-Tags"))
      throw new Exception("Source context must explain RDL incompatible with a popup title");
    vm.SelectedResourceLink = vm.SourceContextLinks.Single(link => link.Reference.ParameterIndex == 1);
    if (!vm.SourceContextOrigin.Contains("RDL-fähig") || vm.SourceContextCompatibility.Length != 0)
      throw new Exception("Popup body must not get a false title-compatibility warning");
    Console.WriteLine("PASS rendering labels, per-use annotations, live draft warnings and source-context compatibility distinguish popup title and body");
  }
}
