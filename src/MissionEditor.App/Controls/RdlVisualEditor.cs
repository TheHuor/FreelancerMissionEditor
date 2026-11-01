using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using MissionEditor.Core.Model;

namespace MissionEditor.App.Controls;

/// <summary>Continuous RDL editing surface with a compact formatting toolbar.</summary>
public sealed class RdlVisualEditor : UserControl
{
  private static readonly DependencyProperty SourceIndexProperty = DependencyProperty.RegisterAttached(
      "SourceIndex", typeof(int), typeof(RdlVisualEditor), new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.Inherits));
  private readonly RichTextBox _editor = new() { Padding = new Thickness(12), Margin = new Thickness(3), FontSize = 15,
      VerticalScrollBarVisibility = ScrollBarVisibility.Auto, AcceptsTab = false, IsInactiveSelectionHighlightEnabled = true };
  private readonly TextBlock _notice = new() { Margin = new Thickness(6, 3, 6, 5), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
  private readonly Dictionary<string, Button> _buttons = [];
  private readonly TextBox _color = new() { Width = 95, Text = "#FFFFFF", Margin = new Thickness(3), ToolTip = "Eigene Farbe #RRGGBB" };
  private readonly Popup _palette = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
  private InfocardDocument? _source;
  private IReadOnlyList<RdlTextStyle> _styles = [];
  private bool _loading;
  /// <summary>Raised after a text, paragraph or formatting change.</summary>
  public event EventHandler? RdlChanged;
  /// <summary>Current RDL source including preserved game-specific controls.</summary>
  public string Rdl { get; private set; } = string.Empty;

  /// <summary>Create the continuous writing surface and accessible symbol toolbar.</summary>
  public RdlVisualEditor()
  {
    var root = new DockPanel();
    var tools = new WrapPanel { Margin = new Thickness(3) };
    Button Tool(string key, object glyph, string description, Action action)
    {
      var button = new Button { Content = glyph, Tag = key, Width = key == "color" ? 45 : 34, Height = 31,
        Padding = new Thickness(3), Margin = new Thickness(2), Focusable = false, ToolTip = description };
      AutomationProperties.SetName(button, description);
      button.Click += (_, _) => action(); _buttons[key] = button; tools.Children.Add(button); return button;
    }
    Tool("bold", new TextBlock { Text = "B", FontWeight = FontWeights.Bold }, "Fett (Strg+B)", () => Toggle(TextElement.FontWeightProperty, FontWeights.Bold, FontWeights.Normal));
    Tool("italic", new TextBlock { Text = "I", FontStyle = FontStyles.Italic }, "Kursiv (Strg+I)", () => Toggle(TextElement.FontStyleProperty, FontStyles.Italic, FontStyles.Normal));
    Tool("underline", new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline }, "Unterstreichen (Strg+U)", Underline);
    var colorGlyph = new TextBlock { Text = "A ▾", TextDecorations = TextDecorations.Underline };
    var colorButton = Tool("color", colorGlyph, "Textfarbe", () => { _palette.IsOpen = !_palette.IsOpen; });
    _palette.PlacementTarget = colorButton;
    var colorPanel = new StackPanel { Margin = new Thickness(8), Width = 200 };
    var swatches = new WrapPanel();
    foreach (var entry in new[] { ("Weiß", 0xFFFFFFu), ("Grau", 0x808080u), ("Blau", 0x4848E0u), ("Grün", 0x3BBF1Du),
        ("Aqua", 0x87C3E0u), ("Rot", 0xBF1D1Du), ("Fuchsia", 0x8800C2u), ("Gelb", 0xF5EA52u) })
    {
      var chip = new Button { Width = 36, Height = 30, Margin = new Thickness(3), Padding = new Thickness(3),
        Tag = entry.Item2, ToolTip = entry.Item1, Focusable = false,
        Content = new Border { Background = Brush(entry.Item2), Width = 23, Height = 16 } };
      AutomationProperties.SetName(chip, "Textfarbe " + entry.Item1);
      chip.Click += (_, _) => { _color.Text = $"#{entry.Item2:X6}"; ApplyColor(); }; swatches.Children.Add(chip);
    }
    colorPanel.Children.Add(swatches);
    var custom = new DockPanel(); custom.Children.Add(_color);
    var apply = new Button { Content = "✓", Tag = "apply-color", Width = 34, Margin = new Thickness(3), Focusable = false, ToolTip = "Eigene Farbe anwenden" };
    AutomationProperties.SetName(apply, "Eigene Farbe anwenden"); apply.Click += (_, _) => ApplyColor(); custom.Children.Add(apply); colorPanel.Children.Add(custom);
    var paletteBorder = new Border { Child = colorPanel, BorderThickness = new Thickness(1) };
    paletteBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); paletteBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
    _palette.Child = paletteBorder;
    tools.Children.Add(new Separator { Width = 1, Margin = new Thickness(5, 4, 5, 4) });
    Tool("undo", "↶", "Rückgängig (Strg+Z)", () => { _editor.Undo(); _editor.Focus(); });
    Tool("redo", "↷", "Wiederholen (Strg+Y)", () => { _editor.Redo(); _editor.Focus(); });
    DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
    DockPanel.SetDock(_notice, Dock.Top); root.Children.Add(_notice); root.Children.Add(_editor); Content = root;
    _editor.TextChanged += (_, _) => Changed();
    _editor.SelectionChanged += (_, _) => UpdateTools();
    _editor.PreviewKeyDown += (_, e) =>
    {
      if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.B or Key.I or Key.U)
      { e.Handled = true; if (e.Key == Key.B) Toggle(TextElement.FontWeightProperty, FontWeights.Bold, FontWeights.Normal);
        else if (e.Key == Key.I) Toggle(TextElement.FontStyleProperty, FontStyles.Italic, FontStyles.Normal); else Underline(); }
    };
    DataObject.AddPastingHandler(_editor, (_, e) =>
    {
      if (e.SourceDataObject.GetData(DataFormats.UnicodeText) is string text) e.DataObject = new DataObject(DataFormats.UnicodeText, text);
      else e.CancelCommand();
    });
  }

  /// <summary>Load RDL into one document, breaking paragraphs only at PARA nodes.</summary>
  public void LoadRdl(string raw)
  {
    _loading = true; _source = null; Rdl = raw; _palette.IsOpen = false;
    _editor.Document = new FlowDocument { PagePadding = new Thickness(0) };
    try
    {
      _source = InfocardDocument.Parse(raw);
      if (!_source.CanEditSegments || _source.TextSegments.Count == 0) throw new InvalidDataException("Diese TEXT-Struktur bitte im XML-Modus bearbeiten.");
      _styles = _source.TextStyles(); int index = 0;
      var paragraph = Paragraph(); _editor.Document.Blocks.Add(paragraph);
      foreach (var element in XDocument.Parse(raw).Descendants())
      {
        if (element.Name.LocalName.Equals("PARA", StringComparison.OrdinalIgnoreCase))
        { paragraph = Paragraph(); _editor.Document.Blocks.Add(paragraph); }
        else if (element.Name.LocalName.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
        {
          var style = _styles[index];
          var run = new Run(_source.TextSegments[index]) { FontWeight = (style.Data & 1) != 0 ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = (style.Data & 2) != 0 ? FontStyles.Italic : FontStyles.Normal, Foreground = Brush(ColorFor(style)) };
          if ((style.Data & 4) != 0) run.TextDecorations = TextDecorations.Underline;
          var span = new Span(run); span.SetValue(SourceIndexProperty, index++); paragraph.Inlines.Add(span);
        }
      }
      _editor.IsReadOnly = false;
      _notice.Text = "Enter: neuer Absatz · Schrift und geerbte FL-Standardstile sind angenähert.";
      _editor.IsUndoEnabled = false; _editor.IsUndoEnabled = true;
    }
    catch (Exception error) when (error is InvalidDataException or XmlException or ArgumentException or FormatException or OverflowException)
    { _source = null; _editor.IsReadOnly = true; _notice.Text = "Visuelle Bearbeitung nicht verfügbar: " + error.Message; }
    finally { _loading = false; UpdateTools(); }
  }
  private static Paragraph Paragraph() => new() { Margin = new Thickness(0, 0, 0, 7) };
  private static SolidColorBrush Brush(uint rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
  private uint ColorFor(RdlTextStyle style)
  {
    if ((style.KnownMask & 0xFFFFFF00) == 0xFFFFFF00) return (style.Data >> 8 & 255) << 16 | (style.Data >> 16 & 255) << 8 | style.Data >> 24;
    var color = ((SolidColorBrush)FindResource("TextBrush")).Color; return (uint)(color.R << 16 | color.G << 8 | color.B);
  }
  private void Toggle(DependencyProperty property, object on, object off)
  {
    if (_source is null) return;
    _editor.Selection.ApplyPropertyValue(property, Equals(_editor.Selection.GetPropertyValue(property), on) ? off : on);
    _editor.Focus(); UpdateTools();
  }
  private void Underline()
  {
    if (_source is null) return;
    bool on = _editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations && decorations.Count > 0;
    _editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, on ? new TextDecorationCollection() : TextDecorations.Underline);
    _editor.Focus(); UpdateTools();
  }
  private void ApplyColor()
  {
    if (_source is null) return;
    if (_color.Text.Length != 7 || _color.Text[0] != '#' || !uint.TryParse(_color.Text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
    { _notice.Text = "Farbe als #RRGGBB eingeben, zum Beispiel #FF8040."; return; }
    _editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brush(rgb));
    _palette.IsOpen = false; _editor.Focus(); UpdateTools();
  }
  private void UpdateTools()
  {
    if (_buttons.Count == 0) return;
    foreach (var key in new[] { "bold", "italic", "underline" })
    {
      object value = _editor.Selection.GetPropertyValue(key == "bold" ? TextElement.FontWeightProperty : key == "italic" ? TextElement.FontStyleProperty : Inline.TextDecorationsProperty);
      bool active = key == "bold" ? Equals(value, FontWeights.Bold) : key == "italic" ? Equals(value, FontStyles.Italic)
          : value is TextDecorationCollection decorations && decorations.Count > 0;
      _buttons[key].SetResourceReference(Button.BackgroundProperty, active ? "SelectionBrush" : "InputBrush");
      _buttons[key].SetResourceReference(Button.BorderBrushProperty, active ? "AccentBrush" : "BorderBrush");
    }
    foreach (var button in _buttons.Values) button.IsEnabled = _source is not null;
    _buttons["undo"].IsEnabled = _source is not null && _editor.CanUndo;
    _buttons["redo"].IsEnabled = _source is not null && _editor.CanRedo;
  }
  private static IEnumerable<Inline> Leaves(InlineCollection inlines)
  {
    foreach (var inline in inlines)
      if (inline is Span span) { foreach (var child in Leaves(span.Inlines)) yield return child; }
      else yield return inline;
  }
  private void Changed()
  {
    if (_loading || _source is null) return;
    try
    {
      var sections = Enumerable.Range(0, _source.TextSegments.Count).Select(_ => new List<RdlTextRun>()).ToArray();
      int current = 0; bool first = true;
      foreach (var paragraph in _editor.Document.Blocks.OfType<Paragraph>())
      {
        if (!first) sections[current].Add(new RdlTextRun("\n")); first = false;
        foreach (var inline in Leaves(paragraph.Inlines))
        {
          int index = (int)inline.GetValue(SourceIndexProperty);
          if (index >= 0 && index < sections.Length) current = Math.Max(current, index);
          var baseline = _styles[current];
          string text = inline is Run run ? run.Text : inline is LineBreak ? "\n" : string.Empty;
          if (text.Length == 0) continue;
          bool bold = inline.FontWeight == FontWeights.Bold, italic = inline.FontStyle == FontStyles.Italic;
          bool underline = inline is Run formatted && formatted.TextDecorations?.Count > 0;
          Color color = (inline.Foreground as SolidColorBrush)?.Color ?? Brush(ColorFor(baseline)).Color;
          uint rgb = (uint)(color.R << 16 | color.G << 8 | color.B);
          sections[current].Add(new(text, bold == ((baseline.Data & 1) != 0) ? null : bold,
              italic == ((baseline.Data & 2) != 0) ? null : italic, underline == ((baseline.Data & 4) != 0) ? null : underline,
              rgb == ColorFor(baseline) ? null : rgb));
        }
      }
      string edited = _source.WithContinuousSegments(sections);
      if (edited != Rdl) { Rdl = edited; RdlChanged?.Invoke(this, EventArgs.Empty); }
      _notice.Text = "Enter: neuer Absatz · Änderungen im Entwurf; zum Übernehmen oder Speichern die Aktionen unten verwenden.";
      UpdateTools();
    }
    catch (Exception error) when (error is InvalidDataException or XmlException or ArgumentException)
    { _notice.Text = "Änderung noch ungültig: " + error.Message; }
  }
}
