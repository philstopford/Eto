using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Eto.NodeGraph;

/// <summary>XML persistence for optional graph metadata.</summary>
public static class GraphDocumentXml
{
    public const string ElementName = "nodeGraph";
    public const string Version = "2";

    public static XElement ToXml(GraphDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        return new XElement(ElementName,
            new XAttribute("version", Version),
            new XAttribute("schema", "graph-document-v2"),
            new XElement("nodes", document.Nodes.Select(node =>
                new XElement("node", new XAttribute("id", node.Id), new XAttribute("title", node.Title),
                    new XAttribute("kind", node.Kind), new XAttribute("x", node.X.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("y", node.Y.ToString(CultureInfo.InvariantCulture)),
                    new XElement("ports", node.Ports.Select(port => new XElement("port",
                        new XAttribute("id", port.Id), new XAttribute("name", port.Name),
                        new XAttribute("type", port.Type), new XAttribute("direction", port.Direction),
                        new XAttribute("allowMultiple", port.AllowMultiple)))),
                    new XElement("properties", node.Properties.Select(p => new XElement("property",
                        new XAttribute("name", p.Key), new XAttribute("value", p.Value ?? string.Empty))))))),
            new XElement("edges", document.Edges.Select(edge => new XElement("edge",
                new XAttribute("sourceNode", edge.SourceNodeId), new XAttribute("sourcePort", edge.SourcePortId),
                new XAttribute("targetNode", edge.TargetNodeId), new XAttribute("targetPort", edge.TargetPortId)))));
    }

    public static GraphDocument FromXml(XElement element, Func<string, GraphDocumentNode> nodeFactory)
    {
        if (element == null) throw new ArgumentNullException(nameof(element));
        if (nodeFactory == null) throw new ArgumentNullException(nameof(nodeFactory));
        var document = new GraphDocument();
        foreach (var xmlNode in element.Element("nodes")?.Elements("node") ?? Enumerable.Empty<XElement>())
        {
            var node = nodeFactory((string)xmlNode.Attribute("id"));
            if (node == null) continue;
            node.Title = (string)xmlNode.Attribute("title") ?? node.Title;
            node.X = ParseDouble((string)xmlNode.Attribute("x"));
            node.Y = ParseDouble((string)xmlNode.Attribute("y"));
            if (node.Ports.Count == 0)
            {
                foreach (var port in xmlNode.Element("ports")?.Elements("port") ?? Enumerable.Empty<XElement>())
                    node.Ports.Add(new GraphDocumentPort(
                        (string)port.Attribute("id") ?? string.Empty,
                        (string)port.Attribute("name") ?? string.Empty,
                        ParseEnum((string)port.Attribute("type"), GraphPortType.Any),
                        ParseEnum((string)port.Attribute("direction"), PortDirection.Input),
                        bool.TryParse((string)port.Attribute("allowMultiple"), out var allowMultiple) && allowMultiple));
            }
            foreach (var property in xmlNode.Element("properties")?.Elements("property") ?? Enumerable.Empty<XElement>())
                node.Properties[(string)property.Attribute("name") ?? string.Empty] = (string)property.Attribute("value") ?? string.Empty;
            document.Add(node);
        }
        foreach (var edge in element.Element("edges")?.Elements("edge") ?? Enumerable.Empty<XElement>())
            document.Connect((string)edge.Attribute("sourceNode"), (string)edge.Attribute("sourcePort"),
                (string)edge.Attribute("targetNode"), (string)edge.Attribute("targetPort"));
        return document;
    }

    private static double ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static T ParseEnum<T>(string value, T fallback) where T : struct =>
        Enum.TryParse(value, true, out T result) ? result : fallback;
}
