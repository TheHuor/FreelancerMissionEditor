using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Reflection;
using MissionEditor.App;
using MissionEditor.App.Controls;
using MissionEditor.Core.Ini;
using MissionEditor.Core.Model;
using MissionEditor.TestFixtures;

internal static partial class Program
{
  private static void CheckVisualRdl(Window owner)
  {
    using var fixture = new ResourceDllFixture();
    var game = GameData.Load(fixture.Root);
    ResourceWriter.Save([ResourceDraft.Create(game.Texts!.Resolve(3, 1033), ResourceDllFixture.Card)]);
    game.Texts.Reload();
    var mission = MissionScript.FromDocument(IniDocument.ParseText("[Trigger]\nnickname = visual\nAct_SetOffer = 50\nAct_SetTitle = 3\n"));
    var browser = new ResourceBrowserWindow { Owner = owner, Width = 1180, Height = 880 };
    browser.RefreshContext(mission, game, "visual.ini"); browser.Show();
    static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
      for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
      { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    foreach (int ids in new[] { 50, 3 })
    {
      browser.NavigateToId(ids);
      var source = (TextBox)browser.FindName(ids == 50 ? "XmlEditor" : "StringEditor");
      string original = source.Text;
      var tabs = (TabControl)browser.FindName("ContentTabs");
      tabs.SelectedItem = browser.FindName("VisualTab"); browser.UpdateLayout();
      if (tabs.Items.Cast<TabItem>().Count(tab => tab.Visibility == Visibility.Visible) != 2)
        throw new Exception("RDL must expose only Editor and HEX, without duplicate preview/source tabs");
      ((Button)browser.FindName("VisualModeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
      var visual = (RdlVisualEditor)browser.FindName("VisualEditor");
      var editors = Children(visual).OfType<RichTextBox>().ToArray();
      if (editors.Length != 1 || editors[0].Document.Blocks.Count != 2 || source.Text != original) throw new Exception("Opening visual RDL must retain original XML and expose actual RDL paragraphs in one editor");
      var rich = editors[0];
      if (!Equals(rich.Background, browser.FindResource("InputBrush"))) throw new Exception("Visual text editors must use the dark input background");
      rich.Focus();
      var paragraph = (Paragraph)rich.Document.Blocks.FirstBlock!;
      var run = ((Span)paragraph.Inlines.FirstInline!).Inlines.FirstInline as Run ?? throw new Exception("Missing text run");
      rich.Selection.Select(run.ContentStart, run.ContentStart.GetPositionAtOffset(2)!);
      Button Tool(string label) => Children(visual).OfType<Button>().Single(button => Equals(button.Tag, label));
      Tool("bold").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
      if (!InfocardDocument.Parse(source.Text).TextStyles().Any(style => (style.Data & 1) != 0)) throw new Exception("Visual bold must update RDL");
      Tool("italic").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
      Tool("underline").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
      if (!InfocardDocument.Parse(source.Text).TextStyles().Any(style => (style.Data & 7) == 7)) throw new Exception("Visual italic and underline must update selected RDL text");
      Tool("color").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); browser.UpdateLayout();
      var popup = (Popup)typeof(RdlVisualEditor).GetField("_palette", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(visual)!;
      var color = Children(popup.Child).OfType<TextBox>().Single(); color.Text = "#FF8040";
      void ApplyColor() => Children(popup.Child).OfType<Button>().Single(button => Equals(button.Tag, "apply-color")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
      ApplyColor();
      if (!source.Text.Contains("4080FF")) throw new Exception("Visual color must emit exact packed FL color");
      string formatted = source.Text;
      color.Text = "invalid"; ApplyColor();
      if (source.Text != formatted) throw new Exception("Invalid colors must not change the resource");
      color.Text = "#FF8040"; ApplyColor();
      if (rich.Document.Blocks.Count != 2 || InfocardDocument.Parse(source.Text).PlainText != "First\nSecond")
        throw new Exception("Inline formatting must not create extra paragraphs");
      // Enter creates a real RDL paragraph; undo removes it again.
      rich.CaretPosition = rich.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
      System.Windows.Documents.EditingCommands.EnterParagraphBreak.Execute(null, rich);
      if (rich.Document.Blocks.Count != 3 || !InfocardDocument.Parse(source.Text).PlainText.EndsWith("Second\n"))
        throw new Exception("Enter must create a real paragraph in continuous RDL");
      rich.Undo();
      if (InfocardDocument.Parse(source.Text).PlainText != "First\nSecond") throw new Exception("Undo must remove the inserted paragraph");
      formatted = source.Text;
      ((Button)browser.FindName("XmlModeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
      if (((TextBox)browser.FindName("XmlEditor")).Text != formatted || source.Text != formatted)
        throw new Exception("Switching to XML must retain the unsaved visual draft");
      var xmlSource = (TextBox)browser.FindName("XmlEditor");
      xmlSource.Text = formatted.Replace("Second", "Second edited");
      ((Button)browser.FindName("VisualModeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
      if (InfocardDocument.Parse(source.Text).PlainText != "First\nSecond edited") throw new Exception("XML edits must be retained when returning to visual mode");
      ((Button)browser.FindName("XmlModeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
      xmlSource.Text = "<broken";
      if (tabs.SelectedItem != browser.FindName("VisualTab") || xmlSource.Visibility != Visibility.Visible)
        throw new Exception("Incomplete XML must remain in the unified XML editor");
      xmlSource.Text = formatted;
      ((Button)browser.FindName("VisualModeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
      if (source.Text != formatted || Children(visual).OfType<RichTextBox>().Single().Document.Blocks.Count != 2)
        throw new Exception("Switching back to visual must retain the XML draft and actual paragraphs");
      SavePreview(browser, "resource-visual-rdl-" + ids);
      ((Button)browser.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
      browser.UpdateLayout();
      if (Children(visual).OfType<RichTextBox>().Single().Document.Blocks.Count != 2) throw new Exception("Applied visual editor must rebuild from current RDL");
      ((Button)browser.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
      var saved = game.Texts.Resolve(ids, 1033);
      if (saved.Identity!.Kind != (ids == 3 ? ResourceKind.String : ResourceKind.Infocard)
          || saved.RawText != formatted || InfocardDocument.Parse(saved.RawText!).PlainText != "First\nSecond")
        throw new Exception("Visual formatting must save without changing text or storage type");
    }
    browser.Close();
    var continuous = new RdlVisualEditor();
    var dialog = new Window { Content = continuous, Width = 800, Height = 440 };
    dialog.Show();
    continuous.LoadRdl("<RDL><TEXT>Alpha </TEXT><TRA bold=\"true\"/><TEXT>Beta</TEXT><PARA/><TEXT>Gamma</TEXT></RDL>");
    dialog.UpdateLayout();
    var input = Children(continuous).OfType<RichTextBox>().Single();
    if (input.Document.Blocks.Count != 2) throw new Exception("Three TEXT nodes with one PARA must be exactly two editor paragraphs");
    var firstParagraph = (Paragraph)input.Document.Blocks.FirstBlock!;
    if (new TextRange(firstParagraph.ContentStart, firstParagraph.ContentEnd).Text.TrimEnd('\r', '\n') != "Alpha Beta")
      throw new Exception("TEXT boundaries must remain inline in the continuous editor");
    input.Focus();
    input.CaretPosition = ((Paragraph)input.Document.Blocks.LastBlock!).ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
    EditingCommands.Backspace.Execute(null, input);
    if (input.Document.Blocks.Count != 1 || InfocardDocument.Parse(continuous.Rdl).PlainText != "Alpha BetaGamma")
      throw new Exception("Deleting a paragraph boundary must remove the actual RDL PARA");
    input.SelectAll(); input.Selection.Text = "Joined text";
    if (InfocardDocument.Parse(continuous.Rdl).PlainText != "Joined text")
      throw new Exception("Replacing text across source boundaries must not retain or duplicate old text");
    input.CaretPosition = input.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
    EditingCommands.EnterParagraphBreak.Execute(null, input);
    input.CaretPosition.InsertTextInRun("Next line");
    if (InfocardDocument.Parse(continuous.Rdl).PlainText != "Joined text\nNext line")
      throw new Exception("Typing after Enter must preserve a real paragraph");
    dialog.Close();
    Console.WriteLine("PASS visual RDL selection formatting and colors save for infocards and RDL strings without changing storage type");
  }
}
