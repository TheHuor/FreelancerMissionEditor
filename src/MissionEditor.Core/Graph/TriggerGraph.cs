using MissionEditor.Core.Model;

namespace MissionEditor.Core.Graph;

/// <summary>A node of the trigger graph: a trigger, or a nickname that is referenced but not defined.</summary>
public sealed class TriggerNode
{
  internal TriggerNode (uint id, string name, Trigger? trigger)
  {
    Id = id;
    Name = name;
    Trigger = trigger;
  }

  /// <summary>CreateID of the nickname.</summary>
  public uint Id
  {
    get;
  }

  /// <summary>The nickname (as defined, or as first referenced for a missing trigger).</summary>
  public string Name
  {
    get;
  }

  /// <summary>The trigger, or null when the nickname is not defined.</summary>
  public Trigger? Trigger
  {
    get;
  }

  /// <summary>True when no [Trigger] has this nickname.</summary>
  public bool IsMissing => Trigger is null;

  /// <summary>Edges that end here.</summary>
  public IReadOnlyList<TriggerEdge> Incoming => _incoming;

  /// <summary>Edges that start here.</summary>
  public IReadOnlyList<TriggerEdge> Outgoing => _outgoing;

  private readonly List<TriggerEdge> _incoming = [];
  private readonly List<TriggerEdge> _outgoing = [];

  internal static void Connect (TriggerEdge edge)
  {
    edge.From._outgoing.Add (edge);
    edge.To._incoming.Add (edge);
  }

  /// <inheritdoc/>
  public override string ToString () => Name;
}

/// <summary>
/// A directed edge in flow direction: ActTrig, DeactTrig and Save point from the trigger that holds
/// the action to the target; WatchTrigger points from the watched trigger to the watcher.
/// </summary>
/// <param name="From">Start of the edge.</param>
/// <param name="To">End of the edge.</param>
/// <param name="Kind">The kind of reference.</param>
/// <param name="Reference">The script line behind the edge.</param>
public sealed record TriggerEdge (TriggerNode From, TriggerNode To, TriggerReferenceKind Kind, TriggerReference Reference);

/// <summary>The trigger graph of a mission script. Shadowed duplicate triggers are left out, as in the engine.</summary>
public sealed class TriggerGraph
{
  private readonly Dictionary<uint, TriggerNode> _nodes = [];

  /// <summary>Builds the graph.</summary>
  public TriggerGraph (MissionScript script)
  {
    Script = script;
    foreach (Trigger trigger in script.EffectiveTriggers)
      _nodes[trigger.Id] = new TriggerNode (trigger.Id, trigger.Nickname, trigger);

    var edges = new List<TriggerEdge> ();
    foreach (Trigger trigger in script.EffectiveTriggers)
    {
      TriggerNode source = _nodes[trigger.Id];
      foreach (TriggerReference reference in trigger.References)
      {
        if (!reference.IsEffective)
          continue;
        if (!_nodes.TryGetValue (reference.TargetId, out TriggerNode? target))
        {
          target = new TriggerNode (reference.TargetId, reference.TargetName, null);
          _nodes[reference.TargetId] = target;
        }
        TriggerEdge edge = reference.Kind == TriggerReferenceKind.Watch
            ? new TriggerEdge (target, source, reference.Kind, reference)
            : new TriggerEdge (source, target, reference.Kind, reference);
        TriggerNode.Connect (edge);
        edges.Add (edge);
      }
    }
    Edges = edges;
  }

  /// <summary>The script the graph was built from.</summary>
  public MissionScript Script
  {
    get;
  }

  /// <summary>All nodes.</summary>
  public IEnumerable<TriggerNode> Nodes => _nodes.Values;

  /// <summary>All edges.</summary>
  public IReadOnlyList<TriggerEdge> Edges
  {
    get;
  }

  /// <summary>The node for a nickname (case-insensitive), or null.</summary>
  public TriggerNode? FindNode (string nickname) => _nodes.GetValueOrDefault (Hashing.FlHash.CreateId (nickname));

  /// <summary>The node for an id, or null.</summary>
  public TriggerNode? FindNode (uint id) => _nodes.GetValueOrDefault (id);
}
