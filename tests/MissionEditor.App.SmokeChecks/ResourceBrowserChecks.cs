using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MissionEditor.App;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;
using MissionEditor.TestFixtures;

internal static partial class Program
{
  private static void CheckResourceEditing (Window owner)
  {
    using var fixture = new ResourceDllFixture ();
    GameData game = GameData.Load (fixture.Root);
    string mission = Path.Combine (fixture.Root, "mission.ini");
    var script = MissionScript.FromDocument (IniDocument.ParseText (
        "[Trigger]\nnickname = resources\nAct_PopUpDialog = 1, 2\nAct_SetOffer = 50\n"), mission);
    var browser = new ResourceBrowserWindow { Owner = owner, Width = 1120, Height = 760 };
    void Check (bool value, string message)
    {
      if (!value) throw new Exception (message);
      Console.WriteLine ("PASS " + message);
    }
    void Click (string name) => ((Button)browser.FindName (name)).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent));
    void Guard (string label) => browser.Dispatcher.BeginInvoke (() =>
    {
      Window dialog = browser.OwnedWindows.Cast<Window> ().Single (window => window.Title == "Ungespeicherte Ressourcenänderungen");
      var panel = (StackPanel)dialog.Content;
      var buttons = (WrapPanel)panel.Children[1];
      buttons.Children.OfType<Button> ().Single (button => Equals (button.Content, label)).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent));
    });
    bool WriteCopy (string path) => (bool)typeof (ResourceBrowserWindow).GetMethod ("WriteFile", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke (browser, [fixture.DllPath, path])!;
    browser.RefreshContext (script, game, mission);
    browser.Show ();
    Check (browser.NavigateToId (999), "missing IDS in a readable DLL stays navigable");
    Check (((TextBlock)browser.FindName ("MetadataText")).Text == "Keine aufgelöste Ressource"
        && ((ComboBox)browser.FindName ("LanguageBox")).SelectedItem!.ToString () == "Keine Sprachvariante"
        && !((Button)browser.FindName ("SaveButton")).IsEnabled,
        "missing IDS has no invented culture or editable resource");
    browser.NavigateToId (1);
    var languages = (ComboBox)browser.FindName ("LanguageBox");
    languages.SelectedItem = languages.Items.Cast<object> ().Single (choice =>
        ((ResourceEntry)choice.GetType ().GetProperty ("Entry")!.GetValue (choice)!).Identity?.Language == 1033);
    var editor = (TextBox)browser.FindName ("StringEditor");
    var preview = (TextBox)browser.FindName ("PreviewText");
    Check (preview.Text == ResourceDllFixture.LongText, "browser preserves full long text and paragraphs in actual language");
    Check (languages.SelectedItem!.ToString ()!.Length < 100, "language selector uses a compact actual-language label");
    SavePreview (browser, "resource-browser-long-text");
    string edited = "Updated Ω\n\nSecond paragraph";
    editor.Text = edited;
    Check (browser.NavigateToId (1) && editor.Text == edited, "reopening same IDS retains live editor contents");
    Guard ("Abbrechen");
    Check (!browser.NavigateToId (2) && editor.Text == edited, "cancel keeps resource selection and unsaved editor");
    using (var locked = new FileStream (fixture.DllPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
      Guard ("Speichern");
      Check (!browser.NavigateToId (2) && editor.Text == edited, "locked original prevents navigation and retains draft");
    }
    string copy = Path.Combine (fixture.Root, "export.dll");
    byte[] original = File.ReadAllBytes (fixture.DllPath);
    Check (WriteCopy (copy) && File.Exists (copy) && original.SequenceEqual (File.ReadAllBytes (fixture.DllPath)),
        "browser Save As exports a complete DLL without changing original");
    Guard ("Abbrechen");
    Check (!browser.TryLeave ("Test"), "exported drafts still require an explicit close decision");
    Click ("SaveButton");
    Check (game.Texts!.Resolve (1, 1033).Text == edited && game.Texts.Resolve (2, 1033).Text == "Neighbor"
        && game.Texts.Resolve (1, 1031).Text == "Deutsch", "browser direct save reloads text and preserves neighbors and languages");
    Check (Directory.GetFiles (Path.GetDirectoryName (fixture.DllPath)!, "*.bak*").Length > 0,
        "browser save creates a backup");
    editor.Text = "Local draft after external change";
    var external = GameData.Load (fixture.Root).Texts!.Resolve (1, 1033);
    ResourceWriter.Save ([ResourceDraft.Create (external, "External replacement")]);
    Click ("ApplyButton");
    string conflictCopy = Path.Combine (fixture.Root, "conflict-export.dll");
    Check (WriteCopy (conflictCopy), "external resource change before apply still permits exporting the retained draft");
    Click ("SaveButton");
    Check (((TextBlock)browser.FindName ("StatusText")).Text.Contains ("fehlgeschlagen")
        && editor.Text == "Local draft after external change", "external conflict keeps draft and refuses original overwrite");
    Guard ("Verwerfen");
    browser.NavigateToId (50);
    var xml = (TextBox)browser.FindName ("XmlEditor");
    xml.Text = "<RDL>";
    Click ("ApplyButton");
    Check (((TextBlock)browser.FindName ("StatusText")).Text.Contains ("nicht übernommen"), "invalid RDL cannot become a resource draft");
    Guard ("Verwerfen");
    browser.NavigateToId (2);
    browser.NavigateToId (50);
    ((TabControl)browser.FindName ("ContentTabs")).SelectedIndex = 1;
    SavePreview (browser, "resource-browser-infocard");
    browser.Width = 840; browser.Height = 560;
    browser.UpdateLayout ();
    Check (((TabControl)browser.FindName ("ContentTabs")).ActualHeight >= 115,
        "minimum browser size preserves usable text editing area");
    SavePreview (browser, "resource-browser-infocard-minimum");
    foreach (double scale in new[] { 1.25, 1.5, 2.0 })
      SavePreview (browser, $"resource-browser-infocard-{scale * 100:0}", scale);
    browser.RefreshContext (null, null, null);
    Check (!preview.Text.Contains ("First") && xml.Text.Length == 0,
        "installation switch clears previous texts and edit buffers");
    browser.Close ();
    CheckResourceIndexCompletion (owner);
    owner.Activate ();
  }

  private static void CheckResourceIndexCompletion (Window owner)
  {
    using var fixture = new ResourceDllFixture ();
    fixture.AddInfocard (1, "<RDL><TEXT>Collision card</TEXT></RDL>");
    string second = Path.Combine (fixture.Root, "EXE", "second.dll");
    File.Copy (fixture.DllPath, second);
    File.AppendAllText (Path.Combine (fixture.Root, "EXE", "freelancer.ini"), "\n[Resources]\nDLL = second.dll\n");
    GameData game = GameData.Load (fixture.Root);
    var script = MissionScript.FromDocument (IniDocument.ParseText ("[Trigger]\nnickname = index_test\nAct_SetTitle = 1\n"));
    var browser = new ResourceBrowserWindow { Owner = owner };
    browser.RefreshContext (script, game, "index-test.ini"); browser.Show (); browser.NavigateToId (1);
    var scope = (ComboBox)browser.FindName ("ScopeBox");
    var catalogReady = typeof (ResourceBrowserWindow).GetField ("_catalogReady", BindingFlags.NonPublic | BindingFlags.Instance)!;
    void PumpIndex ()
    {
      var frame = new System.Windows.Threading.DispatcherFrame ();
      DateTime deadline = DateTime.UtcNow.AddSeconds (10);
      var timer = new System.Windows.Threading.DispatcherTimer (TimeSpan.FromMilliseconds (10),
          System.Windows.Threading.DispatcherPriority.Background, (_, _) =>
          { if ((bool)catalogReady.GetValue (browser)! || DateTime.UtcNow > deadline) frame.Continue = false; }, browser.Dispatcher);
      timer.Start (); System.Windows.Threading.Dispatcher.PushFrame (frame); timer.Stop ();
      if (!(bool)catalogReady.GetValue (browser)!) throw new Exception ("Installation catalog did not finish: " + ((TextBlock)browser.FindName ("StatusText")).Text);
    }
    scope.SelectedIndex = 1; PumpIndex ();
    var search = (TextBox)browser.FindName ("SearchBox");
    var list = (ListBox)browser.FindName ("ResourceList");
    search.Text = "Grüße";
    if (list.Items.Count != 2) throw new Exception ("Catalog must find text in non-primary language variants in both DLL slots");
    search.Text = "";
    var kind = (ComboBox)browser.FindName ("KindBox");
    kind.SelectedIndex = 2;
    list.SelectedItem = list.Items.Cast<object> ().Single (row => (int)row.GetType ().GetProperty ("Ids")!.GetValue (row)! == 1);
    if (!((TextBlock)browser.FindName ("MetadataText")).Text.Contains ("Infocard")
        || ((TextBox)browser.FindName ("PreviewText")).Text != "Collision card")
      throw new Exception ("Infocard filter must include a colliding string ID and select the matching resource type");
    kind.SelectedIndex = 0;
    browser.NavigateToId (50); browser.NavigateToId (1);
    scope.SelectedIndex = 1;
    // Hold only the second DLL: the reload can synchronously rebuild the selected
    // mission row from the first DLL, then its background index blocks here.
    var gateType = typeof (GameText).Assembly.GetType ("MissionEditor.Core.Model.ResourceFileAccess")!;
    var gate = (ReaderWriterLockSlim)gateType.GetMethod ("Gate", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke (null, [second])!;
    using var held = new ManualResetEventSlim ();
    using var release = new ManualResetEventSlim ();
    var blocker = Task.Run (() => { gate.EnterWriteLock (); held.Set (); try { release.Wait (); } finally { gate.ExitWriteLock (); } });
    held.Wait ();
    typeof (ResourceBrowserWindow).GetMethod ("OnReload", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke (browser, [browser, new RoutedEventArgs ()]);
    var editor = (TextBox)browser.FindName ("StringEditor");
    editor.Text = "Typed while installation index is still running";
    release.Set (); blocker.GetAwaiter ().GetResult (); PumpIndex ();
    if (editor.Text != "Typed while installation index is still running")
      throw new Exception ("Background reload completion must preserve new live editor changes");
    ((Button)browser.FindName ("DiscardButton")).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent));
    browser.Close ();
    Console.WriteLine ("PASS catalog searches every language and background reload completion preserves new edits");
  }
}
