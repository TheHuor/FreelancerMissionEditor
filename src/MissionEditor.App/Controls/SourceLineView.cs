using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MissionEditor.App.ViewModels;

namespace MissionEditor.App.Controls;

/// <summary>One uninterrupted physical source row with per-parameter context links.</summary>
public sealed class SourceLineView : UserControl
{
  /// <summary>Selects the exact parameter's context without opening a resource window.</summary>
  public static readonly RoutedEvent ResourceSelectedEvent = EventManager.RegisterRoutedEvent (
      "ResourceSelected", RoutingStrategy.Bubble, typeof (RoutedEventHandler), typeof (SourceLineView));
  /// <summary>Explicit inspector action to open a resource editor.</summary>
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

  /// <summary>Render one physical line; all explanatory text stays in the inspector.</summary>
  private void Rebuild ()
  {
    if (Line is not { } line) { Content = null; return; }
    var row = new Grid { Height = 24, Background = Brushes.Transparent, ToolTip = line.ToolTip };
    row.ColumnDefinitions.Add (new ColumnDefinition { Width = new GridLength (54) });
    row.ColumnDefinitions.Add (new ColumnDefinition { Width = new GridLength (20) });
    row.ColumnDefinitions.Add (new ColumnDefinition { Width = GridLength.Auto });
    row.Children.Add (new TextBlock { Text = line.NumberText, Foreground = (Brush)FindResource ("MutedTextBrush"),
      TextAlignment = TextAlignment.Right, Margin = new Thickness (0, 2, 8, 0), FontFamily = new FontFamily ("Consolas"), FontSize = 13 });
    var marker = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, VerticalAlignment = VerticalAlignment.Center,
      HorizontalAlignment = HorizontalAlignment.Center,
      Fill = (Brush)FindResource (line.HasError || line.ResourceErrors.Count > 0 ? "ErrorBrush" : "AccentBrush"),
      Visibility = line.ResourceLinks.Count > 0 || line.ResourceErrors.Count > 0 ? Visibility.Visible : Visibility.Hidden,
      ToolTip = $"Zusatzinfos · {line.ResourceLinks.Count} Ressourcenverweise" };
    Grid.SetColumn (marker, 1); row.Children.Add (marker);
    var code = new TextBlock { Name = "CodeText", FontFamily = new FontFamily ("Consolas"), FontSize = 13,
      TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
    string text = line.Text;
    int position = 0;
    foreach (var span in ParameterSpans (text, line.ResourceLinks))
    {
      code.Inlines.Add (new Run (text[position..span.Start]));
      var hyperlink = new Hyperlink (new Run (text.Substring (span.Start, span.Length)))
      { Foreground = Brush (span.Link), ToolTip = span.Link.ToolTip,
        TextDecorations = span.Link.IsResolved ? null : TextDecorations.Underline };
      var link = span.Link;
      hyperlink.Click += (_, _) => RaiseEvent (new RoutedEventArgs (ResourceSelectedEvent, link));
      code.Inlines.Add (hyperlink);
      position = span.Start + span.Length;
    }
    code.Inlines.Add (new Run (text[position..]));
    Grid.SetColumn (code, 2); row.Children.Add (code);
    Content = row;
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
  /// direkten IDS-Verweis übereinstimmen; indirekte Nicknames stammen ausschließlich aus bekannten Parameterpositionen.
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
          && (item.Reference.Provenance is not null || (int.TryParse (text[first..last], out int numeric) && numeric == item.Ids)));

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
