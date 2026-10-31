using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MissionEditor.Core.Graph;
using MissionEditor.Core.Model;
using Trigger = MissionEditor.Core.Model.Trigger;

namespace MissionEditor.App.Controls;

/// <summary>
/// Draws a <see cref="GraphLayout"/>: nodes at their layout positions, edges as S-curves coloured by
/// kind. Hovering a node highlights its edges; clicking it raises <see cref="NodeClicked"/>.
/// </summary>
public sealed class TriggerGraphCanvas : Canvas
{
  /// <summary>The layout to draw.</summary>
  public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register (
      nameof (Layout), typeof (GraphLayout), typeof (TriggerGraphCanvas),
      new PropertyMetadata (null, (owner, _) => ((TriggerGraphCanvas)owner).Redraw ()));

  private const double FadedOpacity = 0.12;

  private static readonly FontFamily MonoFont = new ("Consolas");

  private readonly Dictionary<TriggerNode, List<Path>> _edgesByNode = [];
  private readonly List<Path> _allEdges = [];

  /// <summary>Creates the canvas; the transparent background makes empty space hit-testable for panning.</summary>
  public TriggerGraphCanvas ()
  {
    Background = Brushes.Transparent;
  }

  /// <summary>Raised when a node is clicked.</summary>
  public event EventHandler<TriggerNode>? NodeClicked;

  /// <summary>Raised after a redraw with the bounds of the centre node (unscaled canvas coordinates).</summary>
  public event EventHandler<Rect>? Redrawn;

  /// <summary>The layout to draw.</summary>
  public GraphLayout? Layout
  {
    get => (GraphLayout?)GetValue (LayoutProperty);
    set => SetValue (LayoutProperty, value);
  }

  /// <summary>The brush of an edge kind; also used for the legend.</summary>
  public static Brush BrushOf (TriggerReferenceKind kind) => ResourceBrush (kind switch
  {
    TriggerReferenceKind.Activate => "ActivateEdgeBrush",
    TriggerReferenceKind.Deactivate => "DeactivateEdgeBrush",
    TriggerReferenceKind.Watch => "WatchEdgeBrush",
    _ => "SaveEdgeBrush",
  });

  private static Brush ResourceBrush (string key) => (Brush)Application.Current.FindResource (key);

  private void Redraw ()
  {
    Children.Clear ();
    _edgesByNode.Clear ();
    _allEdges.Clear ();
    GraphLayout? layout = Layout;
    if (layout is null)
    {
      Width = Height = 0;
      return;
    }

    foreach (LayoutEdge edge in layout.Edges)
    {
      Path path = CreateEdge (edge);
      _allEdges.Add (path);
      Remember (edge.From.Node!, path);
      Remember (edge.To.Node!, path);
      Children.Add (path);
    }
    foreach (LayoutNode node in layout.Nodes)
    {
      FrameworkElement element = CreateNode (node, node == layout.Center);
      SetLeft (element, node.X);
      SetTop (element, node.Y);
      Children.Add (element);
    }

    Width = layout.Width;
    Height = layout.Height;
    Redrawn?.Invoke (this, new Rect (layout.Center.X, layout.Center.Y, layout.Center.Width, layout.Center.Height));
  }

  private void Remember (TriggerNode node, Path path)
  {
    if (!_edgesByNode.TryGetValue (node, out List<Path>? paths))
      _edgesByNode[node] = paths = [];
    paths.Add (path);
  }

  private void Highlight (TriggerNode? node)
  {
    if (node is null)
    {
      foreach (Path path in _allEdges)
      {
        path.Opacity = 1;
        path.StrokeThickness = 1.6;
      }
      return;
    }
    HashSet<Path> related = _edgesByNode.TryGetValue (node, out List<Path>? paths) ? [.. paths] : [];
    foreach (Path path in _allEdges)
    {
      bool isRelated = related.Contains (path);
      path.Opacity = isRelated ? 1 : FadedOpacity;
      path.StrokeThickness = isRelated ? 2.4 : 1.6;
    }
  }

  private FrameworkElement CreateNode (LayoutNode layoutNode, bool isCenter)
  {
    TriggerNode node = layoutNode.Node!;
    Trigger? trigger = node.Trigger;
    Brush background = ResourceBrush (node.IsMissing ? "ErrorSurfaceBrush" : isCenter ? "SelectionBrush" : "SurfaceBrush");
    Brush border = ResourceBrush (node.IsMissing ? "ErrorBrush" : isCenter ? "AccentBrush" : "BorderBrush");

    var title = new TextBlock
    {
      Text = node.IsMissing ? $"? {node.Name}" : node.Name,
      Foreground = ResourceBrush ("TextBrush"),
      FontSize = 14,
      FontWeight = FontWeights.SemiBold,
      TextTrimming = TextTrimming.CharacterEllipsis,
    };
    var subtitle = new TextBlock
    {
      Text = (trigger?.InitialState == TriggerState.Active ? "Aktiv · " : string.Empty) + (trigger?.ConditionText ?? "nicht definiert"),
      FontFamily = MonoFont,
      FontSize = 12,
      Foreground = ResourceBrush ("MutedTextBrush"),
      TextTrimming = TextTrimming.CharacterEllipsis,
    };
    var content = new DockPanel ();
    if (layoutNode.HiddenEdgeCount > 0)
    {
      var badge = new TextBlock
      {
        Text = $"+{layoutNode.HiddenEdgeCount}",
        FontSize = 12,
        Foreground = ResourceBrush ("MutedTextBrush"),
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness (4, 0, 0, 0),
        ToolTip = "Weitere Verweise außerhalb des gezeigten Ausschnitts",
      };
      DockPanel.SetDock (badge, Dock.Right);
      content.Children.Add (badge);
    }
    var texts = new StackPanel ();
    texts.Children.Add (title);
    texts.Children.Add (subtitle);
    content.Children.Add (texts);

    var element = new Border
    {
      Width = layoutNode.Width,
      Height = layoutNode.Height,
      Background = background,
      BorderBrush = border,
      BorderThickness = new Thickness (isCenter ? 2.5 : 1),
      CornerRadius = new CornerRadius (4),
      Padding = new Thickness (6, 3, 6, 3),
      Child = content,
      Cursor = Cursors.Hand,
      ToolTip = trigger is null
            ? $"{node.Name}\nwird referenziert, aber nicht definiert"
            : $"{trigger.Nickname}  (Zeile {trigger.LineNumber})\n{trigger.ConditionText}\n{trigger.Actions.Count} Aktionen",
    };
    element.MouseEnter += (_, _) => Highlight (node);
    element.MouseLeave += (_, _) => Highlight (null);
    element.MouseLeftButtonUp += (_, args) =>
    {
      args.Handled = true;
      NodeClicked?.Invoke (this, node);
    };
    return element;
  }

  private static Path CreateEdge (LayoutEdge edge)
  {
    var geometry = new PathGeometry ();
    if (edge.IsSelfLoop)
    {
      // A small loop over the top right corner.
      LayoutNode node = edge.From;
      var start = new Point (node.X + node.Width - 30, node.Y);
      var end = new Point (node.X + node.Width - 10, node.Y);
      var loop = new PathFigure { StartPoint = start, IsFilled = false };
      loop.Segments.Add (new BezierSegment (new Point (start.X - 10, start.Y - 28), new Point (end.X + 10, end.Y - 28), end, true));
      geometry.Figures.Add (loop);
      geometry.Figures.Add (CreateArrowHead (new Point (end.X + 4, end.Y - 10), end));
    }
    else
    {
      IReadOnlyList<LayoutPoint> points = edge.Points;
      var figure = new PathFigure { StartPoint = ToPoint (points[0]), IsFilled = false };
      for (int i = 1; i < points.Count; i++)
      {
        Point from = ToPoint (points[i - 1]);
        Point to = ToPoint (points[i]);
        if (Math.Abs (from.Y - to.Y) < 0.5)
        {
          figure.Segments.Add (new LineSegment (to, true));
          continue;
        }
        double middleX = (from.X + to.X) / 2;
        figure.Segments.Add (new BezierSegment (new Point (middleX, from.Y), new Point (middleX, to.Y), to, true));
      }
      geometry.Figures.Add (figure);
      Point tip = ToPoint (points[^1]);
      Point before = ToPoint (points[^2]);
      // The curves end horizontally, so the arrow points straight left or right.
      geometry.Figures.Add (CreateArrowHead (new Point (tip.X - Math.Sign (tip.X - before.X) * 10, tip.Y), tip));
    }

    TriggerReferenceKind kind = edge.Edge.Kind;
    Brush brush = BrushOf (kind);
    var path = new Path
    {
      Data = geometry,
      Stroke = brush,
      Fill = brush,
      StrokeThickness = 1.6,
      ToolTip = $"{edge.Edge.From.Name} → {edge.Edge.To.Name}\nZeile {edge.Edge.Reference.Line.Number}: {edge.Edge.Reference.Line.Text.Trim ()}"
            + (edge.IsBackward ? "\n(Rückwärtskante: schließt einen Kreis)" : string.Empty),
    };
    path.StrokeDashArray = kind switch
    {
      TriggerReferenceKind.Watch => [5, 3],
      TriggerReferenceKind.Save => [1, 3],
      TriggerReferenceKind.Deactivate => [5, 2, 1, 2],
      _ => null,
    };
    return path;
  }

  private static Point ToPoint (LayoutPoint point) => new (point.X, point.Y);

  private static PathFigure CreateArrowHead (Point from, Point tip)
  {
    Vector direction = tip - from;
    if (direction.Length < 0.01)
      direction = new Vector (1, 0);
    direction.Normalize ();
    var normal = new Vector (-direction.Y, direction.X);
    const double Length = 9;
    const double HalfWidth = 4;
    Point basePoint = tip - direction * Length;
    var head = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
    head.Segments.Add (new LineSegment (basePoint + normal * HalfWidth, true));
    head.Segments.Add (new LineSegment (basePoint - normal * HalfWidth, true));
    return head;
  }
}
