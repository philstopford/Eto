using System.Collections.Generic;
using Eto.Drawing;

namespace Eto.NodeGraph;

/// <summary>Converts the shared graph document to the Eto canvas model.</summary>
public static class GraphDocumentRenderer
{
    public static NodeGraph ToNodeGraph(GraphDocument document, out Dictionary<NodeItem, string> ids)
    {
        ids = new Dictionary<NodeItem, string>();
        var graph = new NodeGraph();
        var sockets = new Dictionary<(string, string), NodeSocket>();
        foreach (var item in document.Nodes)
        {
            var node = new NodeItem { Title = item.Title, Position = new PointF((float)item.X, (float)item.Y), Tag = item.Id };
            foreach (var port in item.Ports)
            {
                var socket = port.Direction == PortDirection.Input
                    ? node.AddInput(port.Name, SocketType(port.Type))
                    : node.AddOutput(port.Name, SocketType(port.Type));
                socket.AllowMultipleConnections = port.AllowMultiple;
                sockets[(item.Id, port.Id)] = socket;
            }
            graph.AddNode(node);
            ids[node] = item.Id;
        }
        foreach (var edge in document.Edges)
            if (sockets.TryGetValue((edge.SourceNodeId, edge.SourcePortId), out var source) && sockets.TryGetValue((edge.TargetNodeId, edge.TargetPortId), out var target))
                graph.Connect(source, target);
        return graph;
    }

    private static NodeSocketType SocketType(GraphPortType type) => type switch
    {
        GraphPortType.Geometry => NodeSocketType.Geometry,
        GraphPortType.Number => NodeSocketType.Float,
        GraphPortType.Integer => NodeSocketType.Int,
        GraphPortType.Boolean => NodeSocketType.Bool,
        GraphPortType.String => NodeSocketType.String,
        GraphPortType.Control => NodeSocketType.ControlFlow,
        _ => NodeSocketType.Any
    };
}
