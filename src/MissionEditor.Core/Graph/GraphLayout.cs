using MissionEditor.Core.Model;

namespace MissionEditor.Core.Graph;

/// <summary>A node placed in the layered layout.</summary>
public sealed class LayoutNode
{
    internal LayoutNode(TriggerNode node, int layer)
    {
        Node = node;
        Layer = layer;
    }

    /// <summary>The graph node.</summary>
    public TriggerNode Node { get; }

    /// <summary>Distance from the centre: negative = predecessors, 0 = centre, positive = successors.</summary>
    public int Layer { get; }

    /// <summary>Position inside the layer, from 0.</summary>
    public int Order { get; internal set; }

    /// <summary>Edges of the node that lead out of the shown part (not followed because of the depth or the limit).</summary>
    public int HiddenEdgeCount { get; internal set; }
}

/// <summary>An edge between two placed nodes.</summary>
/// <param name="From">Start node.</param>
/// <param name="To">End node.</param>
/// <param name="Edge">The graph edge.</param>
public sealed record LayoutEdge(LayoutNode From, LayoutNode To, TriggerEdge Edge);

/// <summary>Options for <see cref="GraphLayout.Build"/>.</summary>
public sealed class GraphLayoutOptions
{
    /// <summary>How many steps to follow incoming edges.</summary>
    public int DepthBackward { get; init; } = 2;

    /// <summary>How many steps to follow outgoing edges.</summary>
    public int DepthForward { get; init; } = 2;

    /// <summary>Maximum nodes per layer; further nodes are left out and counted.</summary>
    public int MaxNodesPerLayer { get; init; } = 30;

    /// <summary>The edge kinds that are followed and drawn.</summary>
    public IReadOnlySet<TriggerReferenceKind> Kinds { get; init; } = new HashSet<TriggerReferenceKind>
    {
        TriggerReferenceKind.Activate, TriggerReferenceKind.Watch, TriggerReferenceKind.Save,
    };
}

/// <summary>
/// A layered (left to right) layout of the neighbourhood of one trigger: predecessors in layers
/// left of the centre, successors right of it, ordered by the barycentre of their neighbours to
/// keep crossings low. Drawing coordinates are up to the view.
/// </summary>
public sealed class GraphLayout
{
    private GraphLayout(LayoutNode center, IReadOnlyList<LayoutNode> nodes, IReadOnlyList<LayoutEdge> edges, int hiddenNodeCount)
    {
        Center = center;
        Nodes = nodes;
        Edges = edges;
        HiddenNodeCount = hiddenNodeCount;
        MinLayer = nodes.Min(node => node.Layer);
        MaxLayer = nodes.Max(node => node.Layer);
    }

    /// <summary>The selected node in layer 0.</summary>
    public LayoutNode Center { get; }

    /// <summary>All placed nodes.</summary>
    public IReadOnlyList<LayoutNode> Nodes { get; }

    /// <summary>All edges between placed nodes whose kind is selected.</summary>
    public IReadOnlyList<LayoutEdge> Edges { get; }

    /// <summary>Nodes left out because a layer was full.</summary>
    public int HiddenNodeCount { get; }

    /// <summary>Leftmost layer.</summary>
    public int MinLayer { get; }

    /// <summary>Rightmost layer.</summary>
    public int MaxLayer { get; }

    /// <summary>The nodes of a layer in order.</summary>
    public IEnumerable<LayoutNode> GetLayer(int layer) => Nodes.Where(node => node.Layer == layer).OrderBy(node => node.Order);

    /// <summary>Lays out the neighbourhood of <paramref name="center"/>.</summary>
    public static GraphLayout Build(TriggerNode center, GraphLayoutOptions options)
    {
        var placed = new Dictionary<TriggerNode, LayoutNode> { [center] = new LayoutNode(center, 0) };
        var layerCounts = new Dictionary<int, int> { [0] = 1 };
        int hiddenNodeCount = 0;

        void Walk(int direction, int depth)
        {
            var frontier = new List<TriggerNode> { center };
            for (int step = 1; step <= depth && frontier.Count > 0; step++)
            {
                int layer = step * direction;
                var next = new List<TriggerNode>();
                foreach (TriggerNode node in frontier)
                {
                    IEnumerable<TriggerEdge> edges = direction > 0 ? node.Outgoing : node.Incoming;
                    foreach (TriggerEdge edge in edges)
                    {
                        if (!options.Kinds.Contains(edge.Kind))
                            continue;
                        TriggerNode neighbour = direction > 0 ? edge.To : edge.From;
                        if (placed.ContainsKey(neighbour))
                            continue;
                        if (layerCounts.GetValueOrDefault(layer) >= options.MaxNodesPerLayer)
                        {
                            hiddenNodeCount++;
                            continue;
                        }
                        placed[neighbour] = new LayoutNode(neighbour, layer);
                        layerCounts[layer] = layerCounts.GetValueOrDefault(layer) + 1;
                        next.Add(neighbour);
                    }
                }
                frontier = next;
            }
        }

        Walk(+1, options.DepthForward);
        Walk(-1, options.DepthBackward);

        var edgesInLayout = new List<LayoutEdge>();
        foreach (LayoutNode layoutNode in placed.Values)
        {
            foreach (TriggerEdge edge in layoutNode.Node.Outgoing)
            {
                if (!options.Kinds.Contains(edge.Kind))
                    continue;
                if (placed.TryGetValue(edge.To, out LayoutNode? target))
                    edgesInLayout.Add(new LayoutEdge(layoutNode, target, edge));
                else
                    layoutNode.HiddenEdgeCount++;
            }
            layoutNode.HiddenEdgeCount += layoutNode.Node.Incoming.Count(edge => options.Kinds.Contains(edge.Kind) && !placed.ContainsKey(edge.From));
        }

        // A hidden duplicate of a (From, To, Kind) edge adds nothing to the picture.
        edgesInLayout = edgesInLayout.DistinctBy(edge => (edge.From, edge.To, edge.Edge.Kind)).ToList();

        var nodes = placed.Values.ToList();
        OrderLayers(nodes, edgesInLayout);
        return new GraphLayout(placed[center], nodes, edgesInLayout, hiddenNodeCount);
    }

    private static void OrderLayers(List<LayoutNode> nodes, List<LayoutEdge> edges)
    {
        var layers = nodes.GroupBy(node => node.Layer).ToDictionary(group => group.Key, group => group.ToList());
        foreach (List<LayoutNode> layer in layers.Values)
        {
            for (int i = 0; i < layer.Count; i++)
                layer[i].Order = i;
        }

        var neighbours = nodes.ToDictionary(node => node, _ => new List<LayoutNode>());
        foreach (LayoutEdge edge in edges)
        {
            neighbours[edge.From].Add(edge.To);
            neighbours[edge.To].Add(edge.From);
        }

        int minLayer = layers.Keys.Min();
        int maxLayer = layers.Keys.Max();
        // Sweep outwards from the centre a few times; each layer is sorted by the mean position of its
        // neighbours in the layer next to it towards the centre.
        for (int sweep = 0; sweep < 4; sweep++)
        {
            for (int layer = 1; layer <= maxLayer; layer++)
                SortByBarycenter(layers[layer], layer - 1, neighbours);
            for (int layer = -1; layer >= minLayer; layer--)
                SortByBarycenter(layers[layer], layer + 1, neighbours);
        }
    }

    private static void SortByBarycenter(List<LayoutNode> layer, int referenceLayer, Dictionary<LayoutNode, List<LayoutNode>> neighbours)
    {
        double Barycenter(LayoutNode node)
        {
            var positions = neighbours[node].Where(other => other.Layer == referenceLayer).Select(other => (double)other.Order).ToList();
            return positions.Count > 0 ? positions.Average() : node.Order;
        }

        var sorted = layer.OrderBy(Barycenter).ThenBy(node => node.Node.Name, StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < sorted.Count; i++)
            sorted[i].Order = i;
        layer.Clear();
        layer.AddRange(sorted);
    }
}
