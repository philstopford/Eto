using System;
using System.Collections.Generic;
using System.Linq;

namespace Eto.NodeGraph;

/// <summary>
/// Small, application-neutral graph document used by products that host
/// <see cref="NodeGraphView"/>.  Product projects keep their domain objects and
/// adapters; this type owns the common node/port/edge representation and rules.
/// </summary>
public sealed class GraphDocument
{
    private readonly List<GraphDocumentNode> nodes = new();
    private readonly List<GraphDocumentEdge> edges = new();

    public IReadOnlyList<GraphDocumentNode> Nodes => nodes;
    public IReadOnlyList<GraphDocumentEdge> Edges => edges;
    public bool IsLocked { get; private set; }
    public string SelectedNodeId { get; private set; }
    public event EventHandler Changed;
    public event EventHandler SelectionChanged;

    public void SetLocked(bool locked) => IsLocked = locked;

    public void Select(string nodeId)
    {
        if (nodeId != null && nodes.All(n => n.Id != nodeId))
            throw new ArgumentException("Unknown graph node.", nameof(nodeId));
        if (SelectedNodeId == nodeId) return;
        SelectedNodeId = nodeId;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Add(GraphDocumentNode node)
    {
        EnsureEditable();
        if (node == null || string.IsNullOrWhiteSpace(node.Id) || nodes.Any(n => n.Id == node.Id))
            throw new InvalidOperationException("Graph node ids must be non-empty and unique.");
        nodes.Add(node);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Changes a node identity and rewrites all connected edges.</summary>
    /// <remarks>This is useful when an adapter upgrades legacy index-based ids
    /// to persistent domain ids while retaining the common graph document.</remarks>
    public bool RemapNodeId(string nodeId, string replacementId)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(replacementId) || nodes.Any(n => n.Id == replacementId)) return false;
        var node = nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null) return false;

        node.IdValue = replacementId;
        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge.SourceNodeId == nodeId || edge.TargetNodeId == nodeId)
                edges[i] = new GraphDocumentEdge(
                    edge.SourceNodeId == nodeId ? replacementId : edge.SourceNodeId,
                    edge.SourcePortId,
                    edge.TargetNodeId == nodeId ? replacementId : edge.TargetNodeId,
                    edge.TargetPortId);
        }
        if (SelectedNodeId == nodeId) SelectedNodeId = replacementId;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public GraphDocumentEdge Connect(string sourceNodeId, string sourcePortId, string targetNodeId, string targetPortId)
    {
        EnsureEditable();
        var source = FindPort(sourceNodeId, sourcePortId, PortDirection.Output);
        var target = FindPort(targetNodeId, targetPortId, PortDirection.Input);
        if (source == null || target == null || !IsCompatible(source.Type, target.Type)) return null;
        if (WouldCycle(sourceNodeId, targetNodeId)) return null;
        if (!target.AllowMultiple) edges.RemoveAll(e => e.TargetNodeId == targetNodeId && e.TargetPortId == targetPortId);
        var edge = new GraphDocumentEdge(sourceNodeId, sourcePortId, targetNodeId, targetPortId);
        edges.Add(edge);
        Changed?.Invoke(this, EventArgs.Empty);
        return edge;
    }

    public bool Disconnect(GraphDocumentEdge edge)
    {
        EnsureEditable();
        bool removed = edges.Remove(edge);
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    private GraphDocumentPort FindPort(string nodeId, string portId, PortDirection direction) =>
        nodes.FirstOrDefault(n => n.Id == nodeId)?.Ports.FirstOrDefault(p => p.Id == portId && p.Direction == direction);

    private bool WouldCycle(string source, string target)
    {
        if (source == target) return true;
        var pending = new Stack<string>();
        var seen = new HashSet<string>();
        pending.Push(target);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            if (current == source) return true;
            foreach (var edge in edges.Where(e => e.SourceNodeId == current)) pending.Push(edge.TargetNodeId);
        }
        return false;
    }

    private static bool IsCompatible(GraphPortType source, GraphPortType target) =>
        source == GraphPortType.Any || target == GraphPortType.Any || source == target ||
        (source == GraphPortType.Integer && target == GraphPortType.Number);

    private void EnsureEditable()
    {
        if (IsLocked) throw new InvalidOperationException("The graph is locked while the application is busy.");
    }
}

public sealed class GraphDocumentNode
{
    public GraphDocumentNode(string id, string title, string kind, double x = 0, double y = 0)
    {
        IdValue = id ?? throw new ArgumentNullException(nameof(id));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Kind = kind ?? throw new ArgumentNullException(nameof(kind));
        X = x; Y = y;
    }
    internal string IdValue { get; set; }
    public string Id => IdValue;
    public string Title { get; set; }
    public string Kind { get; }
    public double X { get; set; }
    public double Y { get; set; }
    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IList<GraphDocumentPort> Ports { get; } = new List<GraphDocumentPort>();
}

public sealed class GraphDocumentPort
{
    public GraphDocumentPort(string id, string name, GraphPortType type, PortDirection direction, bool allowMultiple = false)
    { Id = id; Name = name; Type = type; Direction = direction; AllowMultiple = allowMultiple; }
    public string Id { get; }
    public string Name { get; set; }
    public GraphPortType Type { get; }
    public PortDirection Direction { get; }
    public bool AllowMultiple { get; set; }
}

public sealed record GraphDocumentEdge(string SourceNodeId, string SourcePortId, string TargetNodeId, string TargetPortId);
public enum PortDirection { Input, Output }
public enum GraphPortType { Any, Number, Integer, Boolean, String, Geometry, Control }
