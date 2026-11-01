using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using MissionEditor.Core.Model;

namespace MissionEditor.App.Controls;

/// <summary>Read-only formatted resource text with an optional XML source view.</summary>
public sealed class RdlTextViewer : UserControl
{
  /// <summary>Original resource content, including RDL embedded in strings.</summary>
  public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(RdlTextViewer),
      new PropertyMetadata(string.Empty, (sender, _) => ((RdlTextViewer)sender).Refresh()));
  /// <summary>Original resource text; viewing never changes it.</summary>
  public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
  private readonly RichTextBox _formatted = new() { IsReadOnly = true, IsDocumentEnabled = true, IsInactiveSelectionHighlightEnabled = true,
      BorderThickness = new Thickness(0), Padding = new Thickness(8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
  private readonly TextBox _source = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
      BorderThickness = new Thickness(0), Padding = new Thickness(8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
  private readonly StackPanel _modes = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
  private readonly Button _view = new() { Content = "Ansicht", ToolTip = "Formatierte Leseansicht", Padding = new Thickness(8, 3, 8, 3) };
  private readonly Button _xml = new() { Content = "XML", ToolTip = "Originalen XML-Quelltext lesen", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(4, 0, 0, 0) };
  private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3) };
  private bool _hasRdl;
  /// <summary>Create a selectable viewer without editing commands or save actions.</summary>
  public RdlTextViewer()
  {
    var root = new DockPanel(); _modes.Children.Add(_view); _modes.Children.Add(_xml);
    DockPanel.SetDock(_modes, Dock.Top); root.Children.Add(_modes);
    DockPanel.SetDock(_message, Dock.Top); root.Children.Add(_message);
    var content = new Grid(); content.Children.Add(_formatted); content.Children.Add(_source); root.Children.Add(content); Content = root;
    _view.Click += (_, _) => Mode(false); _xml.Click += (_, _) => Mode(true);
    Refresh();
  }
  private void Refresh()
  {
    if (_source is null) return;
    string text = Text ?? string.Empty; _source.Text = text;
    _formatted.Document = new FlowDocument(); _view.IsEnabled = false;
    _hasRdl = ResourcePresentation.IsRdl(text); _modes.Visibility = _hasRdl ? Visibility.Visible : Visibility.Collapsed;
    _message.Text = string.Empty; _message.Visibility = Visibility.Collapsed;
    _source.FontFamily = _hasRdl ? new FontFamily("Consolas") : FontFamily;
    if (!_hasRdl) { Mode(true); return; }
    try
    {
      var parsed = InfocardDocument.Parse(text); var styles = parsed.TextStyles(); int index = 0;
      var document = new FlowDocument { PagePadding = new Thickness(0), FontSize = 15 };
      var paragraph = NewParagraph(); document.Blocks.Add(paragraph); TextAlignment alignment = TextAlignment.Left;
      foreach (var node in XDocument.Parse(text).Descendants())
      {
        switch (node.Name.LocalName.ToUpperInvariant())
        {
          case "PARA": paragraph = NewParagraph(); paragraph.TextAlignment = alignment; document.Blocks.Add(paragraph); break;
          case "JUST":
            alignment = ((string?)node.Attribute("loc"))?.ToUpperInvariant() switch { "C" => TextAlignment.Center, "R" => TextAlignment.Right, _ => TextAlignment.Left };
            paragraph.TextAlignment = alignment; break;
          case "TEXT":
            var style = styles[index++];
            var run = new Run(node.Value) { FontWeight = (style.Data & 1) != 0 ? FontWeights.Bold : FontWeights.Normal,
              FontStyle = (style.Data & 2) != 0 ? FontStyles.Italic : FontStyles.Normal };
            if ((style.Data & 4) != 0) run.TextDecorations = TextDecorations.Underline;
            if ((style.KnownMask & 0xFFFFFF00) == 0xFFFFFF00)
              run.Foreground = new SolidColorBrush(Color.FromRgb((byte)(style.Data >> 8), (byte)(style.Data >> 16), (byte)(style.Data >> 24)));
            paragraph.Inlines.Add(run); break;
        }
      }
      _formatted.Document = document; _view.IsEnabled = true; Mode(false);
    }
    catch (Exception error) when (error is InvalidDataException or XmlException or ArgumentException or FormatException or OverflowException)
    {
      _message.Text = "RDL nicht darstellbar; XML-Quelltext bleibt lesbar. " + error.Message;
      _message.Visibility = Visibility.Visible; Mode(true);
    }
  }
  private static Paragraph NewParagraph() => new() { Margin = new Thickness(0, 0, 0, 7) };
  private void Mode(bool xml)
  {
    if (!xml && !_view.IsEnabled) return;
    _source.Visibility = xml ? Visibility.Visible : Visibility.Collapsed;
    _formatted.Visibility = xml ? Visibility.Collapsed : Visibility.Visible;
    _view.SetResourceReference(Button.BackgroundProperty, xml ? "InputBrush" : "SelectionBrush");
    _xml.SetResourceReference(Button.BackgroundProperty, xml ? "SelectionBrush" : "InputBrush");
  }
}
