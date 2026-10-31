using MissionEditor.Core.Model;

namespace MissionEditor.Core.Graph;

/// <summary>A point in layout coordinates.</summary>
/// <param name="X">Horizontal position.</param>
/// <param name="Y">Vertical position.</param>
public readonly record struct LayoutPoint(double X, double Y);

/// <summary>
/// A box in the layered layout: a trigger node, or a dummy that carries an edge across a layer so
/// that long edges run between the nodes instead of through them.
/// </summary>
public sealed class LayoutNode
{
    internal LayoutNode(TriggerNode? node, int layer, double width, double height)
    {
        Node = node;
        Layer = layer;
        Width = width;
        Height = height;
    }

    /// <summary>The graph node, or null for a dummy.</summary>
    public TriggerNode? Node { get; }

    /// <summary>True for an edge dummy.</summary>
    public bool IsDummy => Node is null;

    /// <summary>Column, from 0 (left).</summary>
    public int Layer { get; internal set; }

    /// <summary>Position inside the column, from 0 (top).</summary>
    public int Order { get; internal set; }

    /// <summary>Left edge.</summary>
    public double X { get; internal set; }

    /// <summary>Top edge.</summary>
    public double Y { get; internal set; }

    /// <summary>Width.</summary>
    public double Width { get; }

    /// <summary>Height.</summary>
    public double Height { get; }

    /// <summary>Vertical centre.</summary>
    public double CenterY => Y + Height / 2;

    /// <summary>Edges of the node that lead out of the shown part (not followed because of the depth or the limit).</summary>
    public int HiddenEdgeCount { get; internal set; }

    /// <inheritdoc/>
    public override string ToString() => Node?.Name ?? $"(dummy L{Layer})";
}

/// <summary>An edge between two placed trigger nodes, with its route.</summary>
public sealed class LayoutEdge
{
    internal LayoutEdge(LayoutNode from, LayoutNode to, TriggerEdge edge)
    {
        From = from;
        To = to;
        Edge = edge;
    }

    /// <summary>Start node (flow direction).</summary>
    public LayoutNode From { get; }

    /// <summary>End node (flow direction).</summary>
    public LayoutNode To { get; }

    /// <summary>The graph edge.</summary>
    public TriggerEdge Edge { get; }

    /// <summary>True when the edge points against the left-to-right flow (it closes a cycle).</summary>
    public bool IsBackward { get; internal set; }

    /// <summary>True for an edge from a node to itself.</summary>
    public bool IsSelfLoop => From == To;

    /// <summary>
    /// The route from the start port to the arrow tip. Consecutive points with different X are meant
    /// to be joined by an S-curve with horizontal tangents, points with the same Y by a straight line.
    /// Empty for a self loop.
    /// </summary>
    public IReadOnlyList<LayoutPoint> Points { get; internal set; } = [];

    internal List<LayoutNode> Chain { get; } = [];
}

/// <summary>Options for <see cref="GraphLayout.Build"/>.</summary>
public sealed class GraphLayoutOptions
{
    /// <summary>How many steps to follow incoming edges.</summary>
    public int DepthBackward { get; init; } = 2;

    /// <summary>How many steps to follow outgoing edges.</summary>
    public int DepthForward { get; init; } = 2;

    /// <summary>Maximum nodes per BFS step; further nodes are left out and counted.</summary>
    public int MaxNodesPerLayer { get; init; } = 30;

    /// <summary>The edge kinds that are followed and drawn.</summary>
    public IReadOnlySet<TriggerReferenceKind> Kinds { get; init; } = new HashSet<TriggerReferenceKind>
    {
        TriggerReferenceKind.Activate, TriggerReferenceKind.Watch, TriggerReferenceKind.Save,
    };

    /// <summary>Width of a node box.</summary>
    public double NodeWidth { get; init; } = 230;

    /// <summary>Height of a node box.</summary>
    public double NodeHeight { get; init; } = 40;

    /// <summary>Horizontal space between columns (room for the curves).</summary>
    public double LayerGap { get; init; } = 110;

    /// <summary>Vertical space between two nodes.</summary>
    public double NodeGap { get; init; } = 16;

    /// <summary>Vertical space taken by an edge passing a column.</summary>
    public double EdgeGap { get; init; } = 8;

    /// <summary>Space around the drawing.</summary>
    public double Margin { get; init; } = 24;
}

/// <summary>
/// A layered (Sugiyama style) left-to-right layout of the neighbourhood of one trigger: cycles are
/// broken, every node gets the column of its longest incoming path, long edges get dummies, the
/// columns are ordered by barycentre sweeps (keeping the order with the fewest crossings) and the
/// nodes are pulled towards their neighbours vertically.
/// </summary>
public sealed class GraphLayout
{
    private GraphLayout(LayoutNode center, IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutNode> dummies,
        IReadOnlyList<LayoutEdge> edges, int hiddenNodeCount, double width, double height)
    {
        Center = center;
        Nodes = nodes;
        Dummies = dummies;
        Edges = edges;
        HiddenNodeCount = hiddenNodeCount;
        Width = width;
        Height = height;
    }

    /// <summary>The selected node.</summary>
    public LayoutNode Center { get; }

    /// <summary>All placed trigger nodes (no dummies).</summary>
    public IReadOnlyList<LayoutNode> Nodes { get; }

    /// <summary>The edge dummies (for debugging and tests).</summary>
    public IReadOnlyList<LayoutNode> Dummies { get; }

    /// <summary>All drawn edges.</summary>
    public IReadOnlyList<LayoutEdge> Edges { get; }

    /// <summary>Nodes left out because a BFS step was full.</summary>
    public int HiddenNodeCount { get; }

    /// <summary>Total width including margins.</summary>
    public double Width { get; }

    /// <summary>Total height including margins.</summary>
    public double Height { get; }

    /// <summary>Lays out the neighbourhood of <paramref name="center"/>.</summary>
    public static GraphLayout Build(TriggerNode center, GraphLayoutOptions options)
    {
        (List<TriggerNode> selected, int hiddenNodeCount) = SelectNodes(center, options);
        var boxes = selected.ToDictionary(node => node, node => new LayoutNode(node, 0, options.NodeWidth, options.NodeHeight));

        // Edges among the selected nodes; one per (from, to, kind).
        var edges = new List<LayoutEdge>();
        var seen = new HashSet<(TriggerNode, TriggerNode, TriggerReferenceKind)>();
        foreach (TriggerNode node in selected)
        {
            foreach (TriggerEdge edge in node.Outgoing)
            {
                if (!options.Kinds.Contains(edge.Kind))
                    continue;
                if (!boxes.TryGetValue(edge.To, out LayoutNode? target))
                {
                    boxes[node].HiddenEdgeCount++;
                    continue;
                }
                if (seen.Add((edge.From, edge.To, edge.Kind)))
                    edges.Add(new LayoutEdge(boxes[node], target, edge));
            }
            boxes[node].HiddenEdgeCount += node.Incoming.Count(edge => options.Kinds.Contains(edge.Kind) && !boxes.ContainsKey(edge.From));
        }

        List<LayoutNode> nodes = selected.Select(node => boxes[node]).ToList();
        MarkBackwardEdges(nodes, edges);
        AssignLayers(nodes, edges);
        List<LayoutNode> dummies = InsertDummies(edges, options);
        List<List<LayoutNode>> columns = OrderColumns(nodes, dummies, edges);
        (double width, double height) = AssignCoordinates(columns, edges, options);
        RouteEdges(edges, options);
        return new GraphLayout(boxes[center], nodes, dummies, edges, hiddenNodeCount, width, height);
    }

    // Breadth-first in both directions from the centre, at most MaxNodesPerLayer new nodes per step.
    private static (List<TriggerNode> Nodes, int Hidden) SelectNodes(TriggerNode center, GraphLayoutOptions options)
    {
        var selected = new List<TriggerNode> { center };
        var known = new HashSet<TriggerNode> { center };
        int hidden = 0;
        foreach ((int direction, int depth) in new[] { (-1, options.DepthBackward), (+1, options.DepthForward) })
        {
            var frontier = new List<TriggerNode> { center };
            for (int step = 1; step <= depth && frontier.Count > 0; step++)
            {
                var next = new List<TriggerNode>();
                foreach (TriggerNode node in frontier)
                {
                    foreach (TriggerEdge edge in direction > 0 ? node.Outgoing : node.Incoming)
                    {
                        if (!options.Kinds.Contains(edge.Kind))
                            continue;
                        TriggerNode neighbour = direction > 0 ? edge.To : edge.From;
                        if (known.Contains(neighbour))
                            continue;
                        if (next.Count >= options.MaxNodesPerLayer)
                        {
                            hidden++;
                            continue;
                        }
                        known.Add(neighbour);
                        selected.Add(neighbour);
                        next.Add(neighbour);
                    }
                }
                frontier = next;
            }
        }
        return (selected, hidden);
    }

    // Depth-first search; an edge to a node still on the stack closes a cycle and is reversed for layering.
    private static void MarkBackwardEdges(List<LayoutNode> nodes, List<LayoutEdge> edges)
    {
        var outgoing = nodes.ToDictionary(node => node, _ => new List<LayoutEdge>());
        foreach (LayoutEdge edge in edges.Where(edge => !edge.IsSelfLoop))
            outgoing[edge.From].Add(edge);
        var state = new Dictionary<LayoutNode, int>(); // 1 = on stack, 2 = done
        foreach (LayoutNode root in nodes)
        {
            if (state.ContainsKey(root))
                continue;
            var stack = new Stack<(LayoutNode Node, int Next)>();
            stack.Push((root, 0));
            state[root] = 1;
            while (stack.Count > 0)
            {
                (LayoutNode node, int next) = stack.Pop();
                if (next >= outgoing[node].Count)
                {
                    state[node] = 2;
                    continue;
                }
                stack.Push((node, next + 1));
                LayoutEdge edge = outgoing[node][next];
                int targetState = state.GetValueOrDefault(edge.To);
                if (targetState == 1)
                    edge.IsBackward = true;
                else if (targetState == 0)
                {
                    state[edge.To] = 1;
                    stack.Push((edge.To, 0));
                }
            }
        }
    }

    private static (LayoutNode Upper, LayoutNode Lower) LayeringDirection(LayoutEdge edge) =>
        edge.IsBackward ? (edge.To, edge.From) : (edge.From, edge.To);

    // Longest path from the sources; sources are then pulled right next to their first successor.
    private static void AssignLayers(List<LayoutNode> nodes, List<LayoutEdge> edges)
    {
        var successors = nodes.ToDictionary(node => node, _ => new List<LayoutNode>());
        var predecessorCount = nodes.ToDictionary(node => node, _ => 0);
        foreach (LayoutEdge edge in edges.Where(edge => !edge.IsSelfLoop))
        {
            (LayoutNode upper, LayoutNode lower) = LayeringDirection(edge);
            successors[upper].Add(lower);
            predecessorCount[lower]++;
        }

        var remaining = new Dictionary<LayoutNode, int>(predecessorCount);
        var queue = new Queue<LayoutNode>(nodes.Where(node => remaining[node] == 0));
        var topological = new List<LayoutNode>();
        foreach (LayoutNode node in nodes)
            node.Layer = 0;
        while (queue.Count > 0)
        {
            LayoutNode node = queue.Dequeue();
            topological.Add(node);
            foreach (LayoutNode successor in successors[node])
            {
                successor.Layer = Math.Max(successor.Layer, node.Layer + 1);
                if (--remaining[successor] == 0)
                    queue.Enqueue(successor);
            }
        }

        for (int i = topological.Count - 1; i >= 0; i--)
        {
            LayoutNode node = topological[i];
            if (predecessorCount[node] == 0 && successors[node].Count > 0)
                node.Layer = successors[node].Min(successor => successor.Layer) - 1;
        }
    }

    private static List<LayoutNode> InsertDummies(List<LayoutEdge> edges, GraphLayoutOptions options)
    {
        var dummies = new List<LayoutNode>();
        foreach (LayoutEdge edge in edges.Where(edge => !edge.IsSelfLoop))
        {
            (LayoutNode upper, LayoutNode lower) = LayeringDirection(edge);
            edge.Chain.Add(upper);
            for (int layer = upper.Layer + 1; layer < lower.Layer; layer++)
            {
                var dummy = new LayoutNode(null, layer, options.NodeWidth, 0);
                dummies.Add(dummy);
                edge.Chain.Add(dummy);
            }
            edge.Chain.Add(lower);
        }
        return dummies;
    }

    private static List<List<LayoutNode>> OrderColumns(List<LayoutNode> nodes, List<LayoutNode> dummies, List<LayoutEdge> edges)
    {
        int layerCount = nodes.Max(node => node.Layer) + 1;
        var columns = Enumerable.Range(0, layerCount).Select(_ => new List<LayoutNode>()).ToList();
        foreach (LayoutNode node in nodes.Concat(dummies))
            columns[node.Layer].Add(node);

        var upperNeighbours = new Dictionary<LayoutNode, List<LayoutNode>>();
        var lowerNeighbours = new Dictionary<LayoutNode, List<LayoutNode>>();
        foreach (LayoutNode node in nodes.Concat(dummies))
        {
            upperNeighbours[node] = [];
            lowerNeighbours[node] = [];
        }
        foreach (LayoutEdge edge in edges)
        {
            for (int i = 0; i + 1 < edge.Chain.Count; i++)
            {
                lowerNeighbours[edge.Chain[i]].Add(edge.Chain[i + 1]);
                upperNeighbours[edge.Chain[i + 1]].Add(edge.Chain[i]);
            }
        }

        SetOrder(columns);
        List<List<LayoutNode>> best = columns.Select(column => column.ToList()).ToList();
        int bestCrossings = CountCrossings(columns, lowerNeighbours);
        for (int iteration = 0; iteration < 12 && bestCrossings > 0; iteration++)
        {
            if (iteration % 2 == 0)
            {
                for (int layer = 1; layer < layerCount; layer++)
                    SortByBarycenter(columns[layer], upperNeighbours);
            }
            else
            {
                for (int layer = layerCount - 2; layer >= 0; layer--)
                    SortByBarycenter(columns[layer], lowerNeighbours);
            }
            int crossings = CountCrossings(columns, lowerNeighbours);
            if (crossings < bestCrossings)
            {
                bestCrossings = crossings;
                best = columns.Select(column => column.ToList()).ToList();
            }
        }
        SetOrder(best);
        return best;
    }

    private static void SetOrder(List<List<LayoutNode>> columns)
    {
        foreach (List<LayoutNode> column in columns)
        {
            for (int i = 0; i < column.Count; i++)
                column[i].Order = i;
        }
    }

    private static void SortByBarycenter(List<LayoutNode> column, Dictionary<LayoutNode, List<LayoutNode>> neighbours)
    {
        double Barycenter(LayoutNode node) =>
            neighbours[node].Count > 0 ? neighbours[node].Average(other => other.Order) : node.Order;

        List<LayoutNode> sorted = column.OrderBy(Barycenter).ThenBy(node => node.Order).ToList();
        column.Clear();
        column.AddRange(sorted);
        for (int i = 0; i < column.Count; i++)
            column[i].Order = i;
    }

    private static int CountCrossings(List<List<LayoutNode>> columns, Dictionary<LayoutNode, List<LayoutNode>> lowerNeighbours)
    {
        int crossings = 0;
        for (int layer = 0; layer + 1 < columns.Count; layer++)
        {
            var segments = new List<(int Upper, int Lower)>();
            foreach (LayoutNode node in columns[layer])
                segments.AddRange(lowerNeighbours[node].Select(lower => (node.Order, lower.Order)));
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    if ((segments[i].Upper - segments[j].Upper) * (segments[i].Lower - segments[j].Lower) < 0)
                        crossings++;
                }
            }
        }
        return crossings;
    }

    // X from the column; Y stacked in order, then pulled towards the neighbours a few times while
    // keeping the order and the minimum gaps.
    private static (double Width, double Height) AssignCoordinates(List<List<LayoutNode>> columns, List<LayoutEdge> edges, GraphLayoutOptions options)
    {
        var neighbours = columns.SelectMany(column => column).ToDictionary(node => node, _ => new List<LayoutNode>());
        foreach (LayoutEdge edge in edges)
        {
            for (int i = 0; i + 1 < edge.Chain.Count; i++)
            {
                neighbours[edge.Chain[i]].Add(edge.Chain[i + 1]);
                neighbours[edge.Chain[i + 1]].Add(edge.Chain[i]);
            }
        }

        double Gap(LayoutNode upper, LayoutNode lower) => upper.IsDummy || lower.IsDummy ? options.EdgeGap : options.NodeGap;

        foreach (List<LayoutNode> column in columns)
        {
            double y = 0;
            for (int i = 0; i < column.Count; i++)
            {
                if (i > 0)
                    y += Gap(column[i - 1], column[i]);
                column[i].Y = y;
                y += column[i].Height;
            }
        }

        for (int iteration = 0; iteration < 16; iteration++)
        {
            IEnumerable<int> order = iteration % 2 == 0 ? Enumerable.Range(1, columns.Count - 1) : Enumerable.Range(0, columns.Count - 1).Reverse();
            int referenceOffset = iteration % 2 == 0 ? -1 : +1;
            foreach (int layer in order)
            {
                List<LayoutNode> column = columns[layer];
                var desired = column.Select(node =>
                {
                    var references = neighbours[node].Where(other => other.Layer == layer + referenceOffset).ToList();
                    return references.Count > 0 ? references.Average(other => other.CenterY) - node.Height / 2 : node.Y;
                }).ToArray();
                PlaceWithGaps(column, desired, Gap);
            }
        }

        double top = columns.SelectMany(column => column).Min(node => node.Y);
        double bottom = 0;
        foreach (List<LayoutNode> column in columns)
        {
            foreach (LayoutNode node in column)
            {
                node.X = options.Margin + node.Layer * (options.NodeWidth + options.LayerGap);
                node.Y += options.Margin - top;
                bottom = Math.Max(bottom, node.Y + node.Height);
            }
        }
        double width = 2 * options.Margin + columns.Count * (options.NodeWidth + options.LayerGap) - options.LayerGap;
        return (width, bottom + options.Margin + 30);
    }

    // Moves the nodes as close to their desired Y as the order and the gaps allow (the overlap is
    // split evenly by shifting the whole packed column to the mean offset).
    private static void PlaceWithGaps(List<LayoutNode> column, double[] desired, Func<LayoutNode, LayoutNode, double> gap)
    {
        var positions = new double[column.Count];
        for (int i = 0; i < column.Count; i++)
        {
            positions[i] = i == 0 ? desired[0] : Math.Max(desired[i], positions[i - 1] + column[i - 1].Height + gap(column[i - 1], column[i]));
        }
        for (int i = column.Count - 2; i >= 0; i--)
            positions[i] = Math.Min(positions[i], positions[i + 1] - column[i].Height - gap(column[i], column[i + 1]));
        double shift = 0;
        for (int i = 0; i < column.Count; i++)
            shift += desired[i] - positions[i];
        shift /= Math.Max(1, column.Count);
        for (int i = 0; i < column.Count; i++)
            column[i].Y = positions[i] + shift;
    }

    // Ports spread over the right (outgoing) and left (incoming) side, sorted by the other end so
    // that edges do not cross at the node.
    private static void RouteEdges(List<LayoutEdge> edges, GraphLayoutOptions options)
    {
        var rightPorts = new Dictionary<LayoutNode, List<(LayoutEdge Edge, double OtherY)>>();
        var leftPorts = new Dictionary<LayoutNode, List<(LayoutEdge Edge, double OtherY)>>();
        foreach (LayoutEdge edge in edges.Where(edge => !edge.IsSelfLoop))
        {
            LayoutNode upper = edge.Chain[0];
            LayoutNode lower = edge.Chain[^1];
            Add(rightPorts, upper, (edge, edge.Chain[1].CenterY));
            Add(leftPorts, lower, (edge, edge.Chain[^2].CenterY));
        }

        var startY = new Dictionary<LayoutEdge, double>();
        var endY = new Dictionary<LayoutEdge, double>();
        Spread(rightPorts, startY);
        Spread(leftPorts, endY);

        foreach (LayoutEdge edge in edges.Where(edge => !edge.IsSelfLoop))
        {
            var points = new List<LayoutPoint> { new(edge.Chain[0].X + edge.Chain[0].Width, startY[edge]) };
            for (int i = 1; i + 1 < edge.Chain.Count; i++)
            {
                LayoutNode dummy = edge.Chain[i];
                points.Add(new LayoutPoint(dummy.X, dummy.CenterY));
                points.Add(new LayoutPoint(dummy.X + dummy.Width, dummy.CenterY));
            }
            points.Add(new LayoutPoint(edge.Chain[^1].X, endY[edge]));
            if (edge.IsBackward)
                points.Reverse();
            edge.Points = points;
        }

        static void Add(Dictionary<LayoutNode, List<(LayoutEdge, double)>> ports, LayoutNode node, (LayoutEdge, double) entry)
        {
            if (!ports.TryGetValue(node, out List<(LayoutEdge, double)>? list))
                ports[node] = list = [];
            list.Add(entry);
        }

        static void Spread(Dictionary<LayoutNode, List<(LayoutEdge Edge, double OtherY)>> ports, Dictionary<LayoutEdge, double> result)
        {
            foreach ((LayoutNode node, List<(LayoutEdge Edge, double OtherY)> list) in ports)
            {
                var sorted = list.OrderBy(entry => entry.OtherY).ToList();
                for (int i = 0; i < sorted.Count; i++)
                    result[sorted[i].Edge] = node.Y + node.Height * (0.2 + 0.6 * (i + 1) / (sorted.Count + 1));
            }
        }
    }
}
