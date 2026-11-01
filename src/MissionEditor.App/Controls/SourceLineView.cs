using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MissionEditor.App.ViewModels;

namespace MissionEditor.App.Controls;

/// <summary>Unmodified source with per-parameter links and width-bounded resource hints.</summary>
public sealed class SourceLineView : UserControl
{
  /// <summary>Raised by both the inline ID and its short hint.</summary>
  public static readonly RoutedEvent ResourceRequestedEvent = EventManager.RegisterRoutedEvent (
      nameof (ResourceRequested), RoutingStrategy.Bubble, typeof (RoutedEventHandler), typeof (SourceLineView));

  /// <summary>Requests the structured definition INI rather than its resource DLL.</summary>
  public static readonly RoutedEvent DefinitionRequestedEvent = EventManager.RegisterRoutedEvent (
      "DefinitionRequested", RoutingStrategy.Bubble, typeof (RoutedEventHandler), typeof (SourceLineView));

  /// <summary>Navigation event carrying a <see cref="ResourceLinkItem"/> as OriginalSource.</summary>
  public event RoutedEventHandler ResourceRequested
  {
    add => AddHandler (ResourceRequestedEvent, value);
    remove => RemoveHandler (ResourceRequestedEvent, value);
  }

  /// <summary>The physical source row.</summary>
  public static readonly DependencyProperty LineProperty = DependencyProperty.Register (
      nameof (Line), typeof (SectionLineItem), typeof (SourceLineView), new PropertyMetadata (null, Changed));

  /// <summary>The physical source row.</summary>
  public SectionLineItem? Line
  {
    get => (SectionLineItem?)GetValue (LineProperty); set => SetValue (LineProperty, value);
  }

  private static void Changed (DependencyObject source, DependencyPropertyChangedEventArgs args) => ((SourceLineView)source).Rebuild ();

  private void Request (ResourceLinkItem link) => RaiseEvent (new RoutedEventArgs (ResourceRequestedEvent, link));

    /// <summary>
    /// Baut den visuellen Inhalt für die aktuelle Quellzeile neu auf:
    /// <list type="bullet">
    /// <item><description>zeigt die Quellzeile mit klickbaren Parameter-Links an,</description></item>
    /// <item><description>rendert darunter aufgelöste/ungeklärte Ressourcenhinweise,</description></item>
    /// <item><description>und ergänzt gefundene Fehlertexte als eigene Zeilen.</description></item>
    /// </list>
    /// </summary>
  private void Rebuild ()
  {
    // Ohne aktive Zeile wird die Anzeige vollständig geleert.
    if (Line is not { } line)
    {
      Content = null;
      return;
    }

    // Hauptcontainer: oben Quelltext, darunter Link-Hinweise und Fehler.
    var panel = new StackPanel ();

    // Quelltextdarstellung (Caption proportional, sonst Monospace für Code).
    var code = new TextBlock { FontFamily = new FontFamily (line.IsCaption ? "Segoe UI" : "Consolas"), FontSize = 13 };
    string text = line.Text;
    int position = 0;

    // Zerlegt den Text in normale Runs + klickbare Parameterbereiche.
    foreach (var span in ParameterSpans (text, line.ResourceLinks))
    {
      // Unverlinkter Abschnitt vor dem aktuellen Parameter.
      code.Inlines.Add (new Run (text[position..span.Start]));

      // Verlinkter Parameterbereich mit Statusfarbe/Tooltip.
      var hyperlink = new Hyperlink (new Run (text.Substring (span.Start, span.Length)))
      {
        Foreground = Brush (span.Link),
        ToolTip = span.Link.ToolTip,
        TextDecorations = span.Link.IsResolved ? null : TextDecorations.Underline,
      };

      // Lokale Kopie für korrektes Capturing in der Lambda.
      var link = span.Link;
      hyperlink.Click += (_, _) => Request (link);
      code.Inlines.Add (hyperlink);

      // Cursor auf das Ende des verarbeiteten Parameterbereichs setzen.
      position = span.Start + span.Length;
    }

    // Restlicher Text nach dem letzten Parameter.
    code.Inlines.Add (new Run (text[position..]));

    // Quelltext in horizontal scrollbarfähiger Fläche anzeigen.
    panel.Children.Add (new ScrollViewer
    {
      HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
      VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
      Content = code,
      Focusable = false
    });

    // Zusätzliche, kompakte Linkliste unterhalb der Quellzeile.
    foreach (ResourceLinkItem link in line.ResourceLinks)
    {
      var button = new Button
      {
        Content = new TextBlock { Text = link.Label, TextTrimming = TextTrimming.CharacterEllipsis },
        HorizontalContentAlignment = HorizontalAlignment.Left,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Padding = new Thickness (0, 1, 0, 1),
        Margin = new Thickness (0, 1, 0, 0),
        Foreground = Brush (link),
        ToolTip = link.ToolTip,
        FontFamily = new FontFamily ("Segoe UI"),
        FontSize = 12
      };

      // Schlichtes Template, damit der Inhalt über die volle Breite links ausgerichtet bleibt.
      var presenter = new FrameworkElementFactory (typeof (ContentPresenter));
      presenter.SetValue (HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
      button.Template = new ControlTemplate (typeof (Button)) { VisualTree = presenter };
      button.Cursor = System.Windows.Input.Cursors.Hand;

      // Unaufgelöste Ressourcen optisch markieren.
      if (!link.IsResolved && button.Content is TextBlock label)
        label.TextDecorations = TextDecorations.Underline;

      // Klick fordert die Ressource an.
      button.Click += (_, _) => Request (link);
      panel.Children.Add (button);

    }

    // Eine Definition kann Name und Infocard liefern; der Bearbeitungslink erscheint nur einmal.
    foreach (ResourceLinkItem link in line.ResourceLinks.Where (item => item.Reference.DefinitionPath is not null)
        .DistinctBy (item => (item.Reference.DefinitionPath!.ToUpperInvariant (), item.Reference.DefinitionLineNumber)))
    {
      string definition = link.Reference.DefinitionPath!;
      var origin = new Hyperlink (new Run ("Definition bearbeiten · " + System.IO.Path.GetFileName (definition)))
      {
        Foreground = (Brush)FindResource ("AccentBrush"),
        ToolTip = $"{definition}\nSektion ab Zeile {link.Reference.DefinitionLineNumber} bearbeiten",
      };
      origin.Click += (_, _) => RaiseEvent (new RoutedEventArgs (DefinitionRequestedEvent, link));
      var originLine = new TextBlock { FontFamily = new FontFamily ("Segoe UI"), FontSize = 11, Margin = new Thickness (0, 3, 0, 2) };
      originLine.Inlines.Add (origin);
      panel.Children.Add (originLine);
    }
    // Alle erkannten Ressourcenfehler als gut sichtbare Textblöcke ausgeben.
    foreach (string error in line.ResourceErrors)
      panel.Children.Add (new TextBlock
      {
        Text = error,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily ("Segoe UI"),
        FontSize = 12,
        Foreground = (Brush)FindResource ("ErrorBrush")
      });

    // Fertig aufgebautes UI in das Control übernehmen.
    Content = panel;
  }

  private Brush Brush (ResourceLinkItem link) => (Brush)FindResource (link.IsResolved ? "AccentBrush" : "ErrorBrush");

  /// <summary>
  /// Ermittelt die Textspannen numerischer Parameterwerte in einer Quellzeile und ordnet sie
  /// vorhandenen <see cref="ResourceLinkItem"/>-Einträgen zu.
  /// </summary>
  /// <param name="text">Die vollständige Quellzeile (inklusive Zuweisung und Parameterliste).</param>
  /// <param name="links">Alle für die Zeile bekannten Ressourcenverweise.</param>
  /// <returns>
  /// Eine Folge aus Startindex, Länge und zugehörigem Link für jeden auflösbaren Parameterwert.
  /// </returns>
  /// <remarks>
  /// Das Parsing beginnt hinter dem ersten <c>=</c>, trennt Parameter über <c>,</c> und beendet die
  /// Verarbeitung bei <c>;</c> oder am Zeilenende. Leerzeichen um Parameterwerte werden ignoriert.
  /// Es werden nur numerische Parameter berücksichtigt, deren Parameterindex sowie ID mit einem
  /// Link ohne <c>Provenance</c> übereinstimmen.
  /// </remarks>
  private static IEnumerable<(int Start, int Length, ResourceLinkItem Link)> ParameterSpans (string text, IReadOnlyList<ResourceLinkItem> links)
  {
    // Ohne Zuweisungsteil gibt es keine Parameterliste zu verarbeiten.
    int equals = text.IndexOf ('=');
    if (equals < 0)
      yield break;

    // Start hinter '='; parameter zählt die aktuelle Parameterposition (0-basiert).
    int start = equals + 1, parameter = 0;

    while (start < text.Length)
    {
      // Nächstes Parametertoken bis ',' oder ';' ermitteln.
      int end = start;
      while (end < text.Length && text[end] != ',' && text[end] != ';')
        end++;

      // Token auf inhaltliche Grenzen trimmen (führende/nachfolgende Whitespaces entfernen).
      int first = start, last = end;
      while (first < last && char.IsWhiteSpace (text[first]))
        first++;
      while (last > first && char.IsWhiteSpace (text[last - 1]))
        last--;

      // Passenden Link für die aktuelle Parameterposition und numerische ID suchen.
      ResourceLinkItem? link = links.FirstOrDefault (item => item.Reference.ParameterIndex == parameter
          && item.Reference.Provenance is null && int.TryParse (text[first..last], out int numeric) && numeric == item.Ids);

      if (link is not null)
        yield return (first, last - first, link);

      // ';' markiert das Ende der relevanten Parameterliste.
      if (end >= text.Length || text[end] == ';')
        yield break;

      // Mit dem nächsten Parameter hinter ',' fortfahren.
      start = end + 1;
      parameter++;
    }
  }
}
