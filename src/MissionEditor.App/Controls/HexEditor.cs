using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MissionEditor.App.Controls;

/// <summary>Virtualized, fixed-length byte editor with a shared selection in both columns.</summary>
public sealed class HexEditor : UserControl
{
  private byte[] _original = [], _bytes = [];
  private readonly ListBox _rows = new ();
  private readonly TextBlock _status = new () { Margin = new Thickness (6), TextWrapping = TextWrapping.Wrap };
  private readonly Stack<(int Offset, byte Before, byte After)> _undo = new (), _redo = new ();
  private bool _textSide;
  private int? _nibble;
  /// <summary>Shared byte offset for both columns.</summary>
  public int SelectedOffset { get; private set; }
  /// <summary>Disable byte replacement while retaining selection.</summary>
  public bool IsReadOnly { get; set; }
  /// <summary>Snapshot of the current working bytes.</summary>
  public byte[] Bytes => _bytes.ToArray ();
  /// <summary>Raised after replacement, undo or redo.</summary>
  public event EventHandler? BytesChanged;

  /// <summary>Create the virtualized byte view.</summary>
  public HexEditor ()
  {
    var panel = new DockPanel ();
    DockPanel.SetDock (_status, Dock.Top);
    panel.Children.Add (_status);
    panel.Children.Add (_rows);
    Content = panel;
    _rows.SetValue (VirtualizingPanel.IsVirtualizingProperty, true);
    _rows.SetValue (VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
    _rows.SetValue (ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
    var factory = new FrameworkElementFactory (typeof (HexRow));
    _rows.ItemTemplate = new DataTemplate { VisualTree = factory };
    _rows.ItemContainerStyle = new Style (typeof (ListBoxItem))
    {
      Setters = { new Setter (PaddingProperty, new Thickness (0)), new Setter (MarginProperty, new Thickness (0)),
        new Setter (Control.TemplateProperty, new ControlTemplate (typeof (ListBoxItem)) { VisualTree = new FrameworkElementFactory (typeof (ContentPresenter)) }) }
    };
    _rows.PreviewTextInput += Input;
    _rows.PreviewKeyDown += Key;
  }

  /// <summary>Load an original and optional working version without raising a change.</summary>
  public void LoadBytes (byte[] original, byte[]? edited = null)
  {
    _original = original.ToArray (); _bytes = (edited ?? original).ToArray ();
    _undo.Clear (); _redo.Clear (); _nibble = null;
    SelectedOffset = Math.Clamp (SelectedOffset, 0, Math.Max (0, _bytes.Length - 1));
    _rows.ItemsSource = Enumerable.Range (0, (_bytes.Length + 15) / 16).Select (index => new Row (this, index * 16)).ToArray ();
    Refresh ();
  }

  /// <summary>Select and reveal a byte in both columns.</summary>
  public void SelectByte (int offset, bool textSide = false)
  {
    if (offset < 0 || offset >= _bytes.Length) return;
    SelectedOffset = offset; _textSide = textSide; _nibble = null;
    _rows.SelectedIndex = offset / 16;
    _rows.ScrollIntoView (_rows.SelectedItem);
    Refresh ();
  }

  /// <summary>Replace the selected byte and record an undo step.</summary>
  public void ReplaceByte (byte value)
  {
    if (IsReadOnly || _bytes.Length == 0 || _bytes[SelectedOffset] == value) return;
    _undo.Push ((SelectedOffset, _bytes[SelectedOffset], value)); _redo.Clear ();
    _bytes[SelectedOffset] = value; _nibble = null;
    Refresh (); BytesChanged?.Invoke (this, EventArgs.Empty);
  }

  private void Input (object sender, TextCompositionEventArgs e)
  {
    if (IsReadOnly || Keyboard.Modifiers.HasFlag (ModifierKeys.Control)) return;
    e.Handled = true;
    foreach (char character in e.Text)
    {
      if (_textSide)
      {
        if (character > 255 || char.IsControl (character)) continue;
        ReplaceByte ((byte)character);
        SelectByte (SelectedOffset + 1, true);
      }
      else if (Uri.IsHexDigit (character))
      {
        int digit = int.Parse (character.ToString (), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (_nibble is { } first) { ReplaceByte ((byte)(first * 16 + digit)); SelectByte (SelectedOffset + 1); }
        else { _nibble = digit; Refresh (); }
      }
    }
  }

  private void Key (object sender, KeyEventArgs e)
  {
    if (Keyboard.Modifiers.HasFlag (ModifierKeys.Control) && e.Key is System.Windows.Input.Key.Z or System.Windows.Input.Key.Y)
    {
      e.Handled = true;
      var source = e.Key == System.Windows.Input.Key.Z ? _undo : _redo;
      var target = e.Key == System.Windows.Input.Key.Z ? _redo : _undo;
      if (IsReadOnly || !source.TryPop (out var change)) return;
      _bytes[change.Offset] = e.Key == System.Windows.Input.Key.Z ? change.Before : change.After;
      target.Push (change); SelectByte (change.Offset, _textSide); BytesChanged?.Invoke (this, EventArgs.Empty); return;
    }
    int next = e.Key switch
    {
      System.Windows.Input.Key.Left => SelectedOffset - 1, System.Windows.Input.Key.Right => SelectedOffset + 1,
      System.Windows.Input.Key.Up => SelectedOffset - 16, System.Windows.Input.Key.Down => SelectedOffset + 16,
      System.Windows.Input.Key.Home => SelectedOffset / 16 * 16,
      System.Windows.Input.Key.End => Math.Min (_bytes.Length - 1, SelectedOffset / 16 * 16 + 15), _ => -1
    };
    if (next >= 0) { e.Handled = true; SelectByte (next, _textSide); }
    if (e.Key == System.Windows.Input.Key.Escape) { _nibble = null; Refresh (); }
  }

  private void Refresh ()
  {
    _status.Text = $"{_bytes.Length} Bytes · Offset {SelectedOffset:X8} · Hex links / Bytezeichen rechts (Latin-1, Steuerbytes: ·)\n"
        + (IsReadOnly ? "Schreibgeschützt: Änderungen im Text-Reiter erscheinen hier automatisch." : "Byte anklicken und überschreiben: 2 Hexziffern oder 1 Zeichen · Strg+Z/Y · Orange = geändert")
        + (_nibble is { } digit ? $" · Eingabe: {digit:X}_" : "");
    foreach (var row in Descendants (_rows).OfType<HexRow> ()) row.InvalidateVisual ();
  }
  private static IEnumerable<DependencyObject> Descendants (DependencyObject root)
  {
    for (int i = 0; i < VisualTreeHelper.GetChildrenCount (root); i++)
    {
      var child = VisualTreeHelper.GetChild (root, i); yield return child;
      foreach (var nested in Descendants (child)) yield return nested;
    }
  }
  private sealed record Row (HexEditor Owner, int Offset);
  /// <summary>One recycled row displaying sixteen hex/character pairs.</summary>
  public sealed class HexRow : FrameworkElement
  {
    private const double HexStart = 86, HexWidth = 26, TextStart = 520, TextWidth = 12;
    /// <summary>Create a fixed-size byte row.</summary>
    public HexRow () { Height = 24; Width = 720; DataContextChanged += (_, _) => InvalidateVisual (); }

    /// <summary>
    /// Rendert eine Zeile des Hex-Editors mit Offset, Hexadezimalwerten und
    /// druckbaren Zeichen.
    /// </summary>
    /// <param name="dc">
    /// Der Zeichenkontext, in den die Zeile gerendert wird.
    /// </param>
    /// <remarks>
    /// Geänderte Bytes werden hervorgehoben. Das aktuell ausgewählte Byte wird
    /// zusätzlich mit dem Auswahl- und Akzentpinsel markiert.
    /// </remarks>
    protected override void OnRender (DrawingContext dc)
    {
      // Ohne gültigen Zeilenkontext gibt es keine Daten, die dargestellt werden können.
      if (DataContext is not Row row)
        return;

      var owner = row.Owner;
      var foreground = (Brush)owner.FindResource ("TextBrush");
      var muted = (Brush)owner.FindResource ("MutedTextBrush");

      // Zeichnet Text im für den Hex-Editor verwendeten Monospace-Schriftbild.
      void Text (string text, double x, Brush brush) => dc.DrawText (new FormattedText (text, CultureInfo.InvariantCulture,
          FlowDirection.LeftToRight, new Typeface ("Consolas"), 13, brush, VisualTreeHelper.GetDpi (this).PixelsPerDip), new Point (x, 3));

      // Hintergrund der Zeile zurücksetzen, bevor die Bytewerte gezeichnet werden.
      dc.DrawRectangle (Brushes.Transparent, null, new Rect (0, 0, Width, Height));

      // Den Offset des ersten Bytes am linken Rand der Zeile anzeigen.
      Text (row.Offset.ToString ("X8"), 4, muted);

      // Jede Zeile enthält höchstens sechzehn Bytes.
      for (int i = 0; i < 16 && row.Offset + i < owner._bytes.Length; i++)
      {
        int offset = row.Offset + i;
        byte value = owner._bytes[offset];

        // Ein Byte gilt als geändert, wenn es neu hinzugefügt oder vom Original abweichend ist.
        bool changed = offset >= owner._original.Length || value != owner._original[offset];

        // Hexadezimale und textuelle Darstellung desselben Bytes gemeinsam hervorheben.
        foreach (var cell in new[] { (HexStart + i * HexWidth, HexWidth - 2), (TextStart + i * TextWidth, TextWidth) })
        {
          if (changed)
            dc.DrawRectangle (new SolidColorBrush (Color.FromRgb (91, 58, 26)), null, new Rect (cell.Item1, 1, cell.Item2, 22));

          // Das aktuell ausgewählte Byte mit Rahmen und Auswahlfarbe markieren.
          if (offset == owner.SelectedOffset)
            dc.DrawRectangle ((Brush)owner.FindResource ("SelectionBrush"),
                new Pen ((Brush)owner.FindResource ("AccentBrush"), 1),
                new Rect (cell.Item1, 1, cell.Item2, 22));

          // Geänderte Zellen zusätzlich durch eine farbige Linie am unteren Rand kennzeichnen.
          if (changed)
            dc.DrawRectangle ((Brush)owner.FindResource ("WarningBrush"), null, new Rect (cell.Item1, 21, cell.Item2, 2));
        }

        // Bytewert als zweistellige Hexadezimalzahl darstellen.
        Text (value.ToString ("X2"), HexStart + i * HexWidth, foreground);

        // Druckbare Bytes als Zeichen, Steuerzeichen als Punkt darstellen.
        Text (value >= 32 && (value < 127 || value >= 160) ? ((char)value).ToString () : "·",
            TextStart + i * TextWidth, foreground);
      }
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown (MouseButtonEventArgs e)
    {
      if (DataContext is not Row row) return;
      double x = e.GetPosition (this).X;
      bool text = x >= TextStart;
      int index = (int)((x - (text ? TextStart : HexStart)) / (text ? TextWidth : HexWidth));
      if (x < HexStart || index is < 0 or >= 16) return;
      row.Owner.SelectByte (row.Offset + index, text);
      row.Owner._rows.Focus (); e.Handled = true;
    }
  }
}
