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
    ((TabControl)browser.FindName ("ContentTabs")).SelectedItem = browser.FindName("VisualTab");
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
    CheckResourceRepair (owner);
    owner.Activate ();
  }

  private static void CheckResourceRepair (Window owner)
  {
    using var fixture = new ResourceDllFixture ();
    byte[] malformed = System.Text.Encoding.Unicode.GetBytes ("\uFEFF" + ResourceDllFixture.Card + "  ").Concat (new byte[] { 0 }).ToArray ();
    fixture.AddInfocardBytes (51, malformed);
    var game = GameData.Load (fixture.Root);
    var browser = new ResourceBrowserWindow { Owner = owner, Width = 1120, Height = 760 };
    browser.RefreshContext (null, game, null); browser.Show (); browser.NavigateToId (51);
    var xml = (TextBox)browser.FindName ("XmlEditor");
    var tabs = (TabControl)browser.FindName ("ContentTabs");
    if (tabs.SelectedItem != browser.FindName ("VisualTab") || xml.Visibility != Visibility.Visible || !xml.Text.Contains ('\uFFFD')
        || !((MissionEditor.App.Controls.HexEditor)browser.FindName ("RawBytesEditor")).Bytes.SequenceEqual (malformed)
        || !((TextBlock)browser.FindName ("MetadataText")).Text.Contains ("1033")
        || !((Button)browser.FindName ("SaveButton")).IsEnabled)
      throw new Exception ("Malformed infocard must expose original bytes, actual language and editable repair XML");
    SavePreview (browser, "resource-infocard-repair-xml");
    tabs.SelectedItem = browser.FindName ("RawTab");
    SavePreview (browser, "resource-infocard-repair-bytes");
    tabs.SelectedItem = browser.FindName ("VisualTab");
    xml.Text += " ";
    ((Button)browser.FindName ("ApplyButton")).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent));
    if (!((TextBlock)browser.FindName ("StatusText")).Text.Contains ("Ersetzungszeichen"))
      throw new Exception ("Repair cannot silently save replacement characters");
    xml.Text = xml.Text.Replace ("\uFFFD", "");
    ((Button)browser.FindName ("SaveButton")).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent));
    if (game.Texts!.Resolve (51, 1033).Status != ResourceStatus.Resolved
        || game.Texts.Resolve (1, 1033).Text != ResourceDllFixture.LongText)
      throw new Exception ("Validated infocard repair must reload successfully and preserve other entries");
    var hex = (MissionEditor.App.Controls.HexEditor)browser.FindName ("RawBytesEditor");
    tabs.SelectedItem = browser.FindName ("RawTab");
    byte[] before = hex.Bytes;
    hex.SelectByte (0); hex.ReplaceByte (0);
    ((Button)browser.FindName ("SaveButton")).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent));
    if (!game.Texts.Resolve (51, 1033).RawBytes!.SequenceEqual (before)
        || !((TextBlock)browser.FindName ("StatusText")).Text.Contains ("nicht übernommen"))
      throw new Exception ("Invalid raw bytes must block saving rather than save the previous valid XML");
    hex.ReplaceByte (before[0]);
    // A text-node character can be changed without altering XML markup.
    string currentXml = xml.Text;
    int character = currentXml.IndexOf ("<TEXT>", StringComparison.Ordinal) + 6;
    byte[] needle = System.Text.Encoding.Unicode.GetBytes (currentXml.Substring (character, 1));
    int offset = -1;
    for (int i = 0; i < before.Length - 1; i++)
      if (before[i] == needle[0] && before[i + 1] == needle[1]) { offset = i; break; }
    if (offset < 0) throw new Exception ("Text byte not found");
    hex.SelectByte (offset, true); hex.ReplaceByte ((byte)'X');
    byte[] changed = hex.Bytes;
    if (!xml.Text.Contains ("<TEXT>X")) throw new Exception ("Byte edits must update XML");
    SavePreview (browser, "resource-hex-edited");
    ((Button)browser.FindName ("SaveButton")).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent));
    if (!game.Texts.Resolve (51, 1033).RawBytes!.SequenceEqual (changed)) throw new Exception ("Byte edits must save exactly");
    browser.Close ();
    Console.WriteLine ("PASS malformed infocard exposes original bytes and editable XML; repair validates and saves without losing neighbors");
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

  private static void CheckStringRdlPresentation (Window owner)
  {
    using var fixture = new ResourceDllFixture ();
    var game = GameData.Load (fixture.Root);
    const string raw = "<?xml version=\"1.0\" encoding=\"UTF-16\"?><RDL><TEXT>Sol Sector</TEXT><PARA/><TEXT>Bases</TEXT></RDL>";
    ResourceWriter.Save ([ResourceDraft.Create (game.Texts!.Resolve (3, 1033), raw)]);
    game.Texts.Reload ();
    var script = MissionScript.FromDocument (IniDocument.ParseText ("[Trigger]\nnickname = rdl_string\nAct_SetOffer = 3\n"));
    var browser = new ResourceBrowserWindow { Owner = owner };
    browser.RefreshContext (script, game, "rdl-string.ini"); browser.Show (); browser.NavigateToId (3);
    var editor = (TextBox)browser.FindName ("StringEditor");
    var preview = (TextBox)browser.FindName ("PreviewText");
    var list = (ListBox)browser.FindName ("ResourceList");
    var row = list.Items.Cast<object> ().Single ();
    string label = (string)row.GetType ().GetProperty ("Detail")!.GetValue (row)!;
    string rowPreview = (string)row.GetType ().GetProperty ("Preview")!.GetValue (row)!;
    if (!label.Contains ("Speicher: String") || !label.Contains ("Inhalt: RDL/XML") || !label.Contains ("Verwendung:")
        || rowPreview.Contains ("<") || !rowPreview.Contains ("Sol Sector")
        || preview.Text != "Sol Sector\nBases" || editor.Text != raw
        || !((TextBlock)browser.FindName ("EditingHintText")).Text.Contains ("weiterhin ein String")
        || ((TabItem)browser.FindName ("EditTab")).Visibility != Visibility.Collapsed
        || !Equals (((TabItem)browser.FindName ("VisualTab")).Header, "Editor"))
      throw new Exception ($"RDL string presentation: {label}; preview={preview.Text}; editor={editor.Text}; header={((TabItem)browser.FindName ("EditTab")).Header}; hint={((TextBlock)browser.FindName ("EditingHintText")).Text}");
    ((TabControl)browser.FindName ("ContentTabs")).SelectedItem = browser.FindName("VisualTab");
    ((Button)browser.FindName("XmlModeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    SavePreview (browser, "resource-string-rdl-description");
    editor.Text = "<RDL><TEXT>Broken";
    ((Button)browser.FindName ("SaveButton")).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent));
    if (game.Texts.Resolve (3, 1033).Text != raw) throw new Exception ("Malformed XML must not overwrite an RDL string");
    editor.Text = raw.Replace ("Sol Sector", "Changed Sector");
    if (preview.Text != "Changed Sector\nBases") throw new Exception ("RDL string edits must update the readable preview");
    ((Button)browser.FindName ("SaveButton")).RaiseEvent (new RoutedEventArgs (ButtonBase.ClickEvent));
    var saved = game.Texts.Resolve (3, 1033);
    if (saved.Identity!.Kind != ResourceKind.String || saved.Text != editor.Text
        || game.Texts.Resolve (1, 1033).Text != ResourceDllFixture.LongText)
      throw new Exception ("RDL string editing must preserve storage type and neighboring strings");
    browser.NavigateToId (1);
    if (!((TextBlock)browser.FindName ("ContentFormatText")).Text.Contains ("Klartext")
        || !Equals (((TabItem)browser.FindName ("EditTab")).Header, "Bearbeiten"))
      throw new Exception ("Plain strings must retain their plain text editing explanation");
    browser.Close ();
    Console.WriteLine ("PASS RDL inside a string has readable previews, explicit storage/content/usage guidance and validated saving without type conversion");
  }

  private static void CheckStringRawScope(Window owner)
  {
    using var fixture = new ResourceDllFixture();
    var game = GameData.Load(fixture.Root);
    var script = MissionScript.FromDocument(IniDocument.ParseText("[Trigger]\nnickname = raw_string\nAct_PopUpDialog = 1, 3\n"));
    var browser = new ResourceBrowserWindow { Owner = owner };
    browser.RefreshContext(script, game, "raw-string.ini"); browser.Show(); browser.NavigateToId(3);
    var hex = (MissionEditor.App.Controls.HexEditor)browser.FindName("RawBytesEditor");
    var editor = (TextBox)browser.FindName("StringEditor");
    byte[] original = System.Text.Encoding.Unicode.GetBytes("Third");
    if (!hex.Bytes.SequenceEqual(original) || !hex.IsReadOnly
        || !((TextBlock)browser.FindName("RawScopeText")).Text.Contains("Nur IDS 3"))
      throw new Exception("HEX must display the selected string, not the first or neighboring entries in its block");
    editor.Text = "Changed Ω\nLine two";
    if (!hex.Bytes.SequenceEqual(System.Text.Encoding.Unicode.GetBytes(editor.Text)))
      throw new Exception("HEX must immediately reflect text changes to the selected string");
    ((TabControl)browser.FindName("ContentTabs")).SelectedItem = browser.FindName("RawTab");
    SavePreview(browser, "resource-selected-string-bytes");
    ((Button)browser.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (!hex.Bytes.SequenceEqual(System.Text.Encoding.Unicode.GetBytes(editor.Text)))
      throw new Exception("HEX must retain the selected string draft after applying changes");
    ((Button)browser.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    if (game.Texts!.Resolve(3, 1033).Text != "Changed Ω\nLine two"
        || game.Texts.Resolve(1, 1033).Text != ResourceDllFixture.LongText
        || game.Texts.Resolve(2, 1033).Text != "Neighbor")
      throw new Exception("Selected string HEX view must preserve the neighboring entries on save");
    browser.Close();
    Console.WriteLine("PASS HEX displays only selected string bytes, follows text edits and applied drafts, and preserves neighbors on save");
  }
}
