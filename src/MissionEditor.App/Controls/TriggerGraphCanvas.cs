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
/// Draws a <see cref="GraphLayout"/>: layers as columns from left (predecessors) to right
/// (successors), edges as curves coloured by kind. Clicking a node raises <see cref="NodeClicked"/>.
/// </summary>
public sealed class TriggerGraphCanvas : Canvas
{
    /// <summary>The layout to draw.</summary>
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(GraphLayout), typeof(TriggerGraphCanvas),
        new PropertyMetadata(null, (owner, _) => ((TriggerGraphCanvas)owner).Redraw()));

    private const double NodeWidth = 230;
    private const double NodeHeight = 40;
    private const double ColumnGap = 90;
    private const double RowGap = 14;
    private const double OuterMargin = 24;

    private static readonly FontFamily MonoFont = new("Consolas");

    /// <summary>Raised when a node is clicked.</summary>
    public event EventHandler<TriggerNode>? NodeClicked;

    /// <summary>Raised after a redraw with the bounds of the centre node (unscaled canvas coordinates).</summary>
    public event EventHandler<Rect>? Redrawn;

    /// <summary>The layout to draw.</summary>
    public GraphLayout? Layout
    {
        get => (GraphLayout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>The brush of an edge kind; also used for the legend.</summary>
    public static Brush BrushOf(TriggerReferenceKind kind) => kind switch
    {
        TriggerReferenceKind.Activate => Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))),
        TriggerReferenceKind.Deactivate => Freeze(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E))),
        TriggerReferenceKind.Watch => Freeze(new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0))),
        _ => Freeze(new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00))),
    };

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void Redraw()
    {
        Children.Clear();
        GraphLayout? layout = Layout;
        if (layout is null)
        {
            Width = Height = 0;
            return;
        }

        int maxRows = Enumerable.Range(layout.MinLayer, layout.MaxLayer - layout.MinLayer + 1)
            .Max(layer => layout.GetLayer(layer).Count());
        double totalHeight = maxRows * (NodeHeight + RowGap) - RowGap;
        var positions = new Dictionary<LayoutNode, Point>();
        for (int layer = layout.MinLayer; layer <= layout.MaxLayer; layer++)
        {
            List<LayoutNode> column = layout.GetLayer(layer).ToList();
            double columnHeight = column.Count * (NodeHeight + RowGap) - RowGap;
            double top = OuterMargin + (totalHeight - columnHeight) / 2;
            double left = OuterMargin + (layer - layout.MinLayer) * (NodeWidth + ColumnGap);
            foreach (LayoutNode node in column)
                positions[node] = new Point(left, top + node.Order * (NodeHeight + RowGap));
        }

        foreach (LayoutEdge edge in layout.Edges)
            Children.Add(CreateEdge(edge, positions[edge.From], positions[edge.To]));
        foreach ((LayoutNode node, Point position) in positions)
        {
            FrameworkElement element = CreateNode(node, node == layout.Center);
            SetLeft(element, position.X);
            SetTop(element, position.Y);
            Children.Add(element);
        }

        Width = 2 * OuterMargin + (layout.MaxLayer - layout.MinLayer + 1) * (NodeWidth + ColumnGap) - ColumnGap;
        Height = 2 * OuterMargin + totalHeight + 40;
        Redrawn?.Invoke(this, new Rect(positions[layout.Center], new Size(NodeWidth, NodeHeight)));
    }

    private FrameworkElement CreateNode(LayoutNode layoutNode, bool isCenter)
    {
        TriggerNode node = layoutNode.Node;
        Trigger? trigger = node.Trigger;
        Brush background = node.IsMissing ? new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE))
            : isCenter ? new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xFD))
            : trigger!.InitialState == TriggerState.Active ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9))
            : Brushes.White;
        Brush border = node.IsMissing ? Brushes.Firebrick : isCenter ? BrushOf(TriggerReferenceKind.Watch) : Brushes.DimGray;

        var title = new TextBlock
        {
            Text = node.IsMissing ? $"? {node.Name}" : node.Name,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var subtitle = new TextBlock
        {
            Text = trigger?.ConditionText ?? "nicht definiert",
            FontFamily = MonoFont,
            FontSize = 10.5,
            Foreground = Brushes.DimGray,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var content = new DockPanel();
        if (layoutNode.HiddenEdgeCount > 0)
        {
            var badge = new TextBlock
            {
                Text = $"+{layoutNode.HiddenEdgeCount}",
                FontSize = 10,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Weitere Verweise außerhalb des gezeigten Ausschnitts",
            };
            DockPanel.SetDock(badge, Dock.Right);
            content.Children.Add(badge);
        }
        var texts = new StackPanel();
        texts.Children.Add(title);
        texts.Children.Add(subtitle);
        content.Children.Add(texts);

        var element = new Border
        {
            Width = NodeWidth,
            Height = NodeHeight,
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(isCenter ? 2.5 : 1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Child = content,
            Cursor = Cursors.Hand,
            ToolTip = trigger is null
                ? $"{node.Name}\nwird referenziert, aber nicht definiert"
                : $"{trigger.Nickname}  (Zeile {trigger.LineNumber})\n{trigger.ConditionText}\n{trigger.Actions.Count} Aktionen",
        };
        element.MouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            NodeClicked?.Invoke(this, node);
        };
        return element;
    }

    private static Path CreateEdge(LayoutEdge edge, Point fromPosition, Point toPosition)
    {
        Point start;
        Point end;
        Point control1;
        Point control2;
        if (edge.To.Layer > edge.From.Layer)
        {
            start = new Point(fromPosition.X + NodeWidth, fromPosition.Y + NodeHeight / 2);
            end = new Point(toPosition.X, toPosition.Y + NodeHeight / 2);
            double bend = (end.X - start.X) / 2;
            control1 = new Point(start.X + bend, start.Y);
            control2 = new Point(end.X - bend, end.Y);
        }
        else
        {
            // Backward or same-layer edge: leave at the bottom, arc below, enter at the bottom.
            start = new Point(fromPosition.X + NodeWidth * 0.4, fromPosition.Y + NodeHeight);
            end = new Point(toPosition.X + NodeWidth * 0.6, toPosition.Y + NodeHeight);
            double depth = 30 + Math.Abs(start.X - end.X) * 0.15;
            control1 = new Point(start.X, start.Y + depth);
            control2 = new Point(end.X, end.Y + depth);
        }

        var figure = new PathFigure { StartPoint = start, IsFilled = false };
        figure.Segments.Add(new BezierSegment(control1, control2, end, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Figures.Add(CreateArrowHead(control2, end));

        TriggerReferenceKind kind = edge.Edge.Kind;
        Brush brush = BrushOf(kind);
        var path = new Path
        {
            Data = geometry,
            Stroke = brush,
            Fill = brush,
            StrokeThickness = 1.6,
            ToolTip = $"Zeile {edge.Edge.Reference.Line.Number}: {edge.Edge.Reference.Line.Text.Trim()}\n(in {edge.Edge.Reference.Source.Nickname})",
        };
        if (kind is TriggerReferenceKind.Watch or TriggerReferenceKind.Deactivate)
            path.StrokeDashArray = kind == TriggerReferenceKind.Watch ? [5, 3] : [2, 2];
        return path;
    }

    private static PathFigure CreateArrowHead(Point from, Point tip)
    {
        Vector direction = tip - from;
        if (direction.Length < 0.01)
            direction = new Vector(1, 0);
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        const double Length = 9;
        const double HalfWidth = 4;
        Point basePoint = tip - direction * Length;
        var head = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
        head.Segments.Add(new LineSegment(basePoint + normal * HalfWidth, true));
        head.Segments.Add(new LineSegment(basePoint - normal * HalfWidth, true));
        return head;
    }
}
