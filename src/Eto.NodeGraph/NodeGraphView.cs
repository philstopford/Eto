namespace Eto.NodeGraph;

/// <summary>
/// Defines the data type that can flow through a <see cref="NodeSocket"/>, including its display color
/// and compatibility rules used when connecting sockets.
/// </summary>
/// <remarks>
/// Two sockets may be connected when the output socket's type considers the input socket's type
/// to be compatible (via <see cref="IsCompatibleWith"/>).  The built-in <see cref="Any"/> type is
/// compatible with every other type.
/// </remarks>
public class NodeSocketType
{
	private readonly HashSet<string> _compatibleNames;

	/// <summary>Gets the unique name of this socket type (e.g. "Float", "Geometry").</summary>
	public string Name { get; }

	/// <summary>Gets the color used to render sockets and connections of this type.</summary>
	public Color Color { get; }

	/// <summary>
	/// Initializes a new instance of <see cref="NodeSocketType"/>.
	/// </summary>
	/// <param name="name">Unique name for this type.</param>
	/// <param name="color">Display color.</param>
	/// <param name="compatibleTypeNames">
	/// Names of other types that this type can connect to (in addition to itself).
	/// </param>
	public NodeSocketType(string name, Color color, IEnumerable<string> compatibleTypeNames = null)
	{
		Name = name ?? throw new ArgumentNullException(nameof(name));
		Color = color;
		_compatibleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
		if (compatibleTypeNames != null)
			foreach (var t in compatibleTypeNames)
				_compatibleNames.Add(t);
	}

	/// <summary>
	/// Returns <c>true</c> when an output socket of this type can be connected to an input socket
	/// of <paramref name="other"/>'s type.
	/// </summary>
	public bool IsCompatibleWith(NodeSocketType other)
	{
		if (other == null) return false;
		if (Name == "Any" || other.Name == "Any") return true;
		return _compatibleNames.Contains(other.Name);
	}

	// ── Built-in types ─────────────────────────────────────────────────────────

	/// <summary>Single-precision floating-point value.</summary>
	public static readonly NodeSocketType Float    = new NodeSocketType("Float",    Color.FromRgb(0xF5A623));

	/// <summary>Integer value (also accepts <see cref="Float"/> connections).</summary>
	public static readonly NodeSocketType Int      = new NodeSocketType("Int",      Color.FromRgb(0x4A90D9), new[] { "Float" });

	/// <summary>Boolean (true/false) value.</summary>
	public static readonly NodeSocketType Bool     = new NodeSocketType("Bool",     Color.FromRgb(0xD0021B));

	/// <summary>String / text value.</summary>
	public static readonly NodeSocketType String   = new NodeSocketType("String",   Color.FromRgb(0x7ED321));

	/// <summary>3-D geometry object.</summary>
	public static readonly NodeSocketType Geometry = new NodeSocketType("Geometry", Color.FromRgb(0x50E3C2));

	/// <summary>
	/// Control-flow (execution order) socket.  Rendered as a right-pointing triangle instead of a circle.
	/// Use it to express evaluation order between nodes — for example connecting a shape to a simulation
	/// input, or a pattern element to a pattern output.  Compatible only with other ControlFlow sockets.
	/// </summary>
	public static readonly NodeSocketType ControlFlow = new NodeSocketType("ControlFlow", Color.FromRgb(0xEEEEEE));

	/// <summary>Universal type – compatible with every other type.</summary>
	public static readonly NodeSocketType Any      = new NodeSocketType("Any",      Color.FromRgb(0x888888));
}

// ───────────────────────────────────────────────────────────────────────────────

/// <summary>Indicates whether a <see cref="NodeSocket"/> is an input or output port.</summary>
public enum NodeSocketDirection
{
	/// <summary>Data flows into the node through this socket.</summary>
	Input,
	/// <summary>Data flows out of the node through this socket.</summary>
	Output,
}

// ───────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single input or output port on a <see cref="NodeItem"/>.
/// </summary>
public class NodeSocket
{
	/// <summary>Gets or sets the display name of the socket.</summary>
	public string Name { get; set; }

	/// <summary>Gets or sets the data type carried by this socket.</summary>
	public NodeSocketType SocketType { get; set; }

	/// <summary>Gets the flow direction (input or output).</summary>
	public NodeSocketDirection Direction { get; }

	/// <summary>Gets the node that owns this socket (set automatically when added to a node).</summary>
	public NodeItem Node { get; internal set; }

	/// <summary>
	/// Gets or sets whether this socket can hold more than one connection at the same time.
	/// Output sockets default to <c>true</c>; input sockets default to <c>false</c>.
	/// </summary>
	public bool AllowMultipleConnections { get; set; }

	/// <summary>
	/// Gets or sets an optional default value string shown when the socket is not connected.
	/// </summary>
	public string Value { get; set; }

	/// <summary>
	/// Gets or sets whether this socket is displayed as a draggable connector on the canvas.
	/// When <c>false</c> the socket and all of its connections are hidden from the canvas view
	/// but remain present in the graph model and are accessible via a properties panel.
	/// Defaults to <c>true</c>.
	/// </summary>
	public bool IsPinned { get; set; } = true;

	/// <summary>Gets the list of active connections that include this socket.</summary>
	public List<NodeConnection> Connections { get; } = new List<NodeConnection>();

	/// <summary>Gets whether at least one connection exists on this socket.</summary>
	public bool IsConnected => Connections.Count > 0;

	/// <summary>
	/// Initializes a new socket with the specified name, type and direction.
	/// </summary>
	public NodeSocket(string name, NodeSocketType type, NodeSocketDirection direction)
	{
		Name = name;
		SocketType = type ?? NodeSocketType.Any;
		Direction = direction;
		AllowMultipleConnections = (direction == NodeSocketDirection.Output);
	}
}

// ───────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single node (block) in a <see cref="NodeGraph"/>.
/// </summary>
public class NodeItem
{
	/// <summary>Gets or sets the title shown in the node header.</summary>
	public string Title { get; set; } = "Node";

	/// <summary>Gets or sets the position of the node's top-left corner in graph space.</summary>
	public PointF Position { get; set; }

	/// <summary>Gets or sets the header background color.</summary>
	public Color HeaderColor { get; set; }

	/// <summary>Gets the list of input sockets.</summary>
	public List<NodeSocket> Inputs { get; } = new List<NodeSocket>();

	/// <summary>Gets the list of output sockets.</summary>
	public List<NodeSocket> Outputs { get; } = new List<NodeSocket>();

	/// <summary>Gets or sets arbitrary user data associated with this node.</summary>
	public object Tag { get; set; }

	/// <summary>
	/// Gets the number of sockets that are not currently pinned (i.e. hidden from the canvas).
	/// A positive value triggers the "N more…" indicator at the bottom of the rendered node.
	/// </summary>
	public int HiddenSocketCount =>
		Inputs.Count(s => !s.IsPinned) + Outputs.Count(s => !s.IsPinned);

	/// <summary>Width of the node in graph units (calculated by <see cref="NodeGraphView"/>).</summary>
	internal float ComputedWidth { get; set; } = NodeGraphView.DefaultNodeWidth;

	/// <summary>Height of the node in graph units (calculated by <see cref="NodeGraphView"/>).</summary>
	internal float ComputedHeight { get; set; }

	/// <summary>Gets the bounding rectangle of the node in graph space.</summary>
	public RectangleF Bounds => new RectangleF(Position.X, Position.Y, ComputedWidth, ComputedHeight);

	/// <summary>Creates and adds an input socket.</summary>
	/// <param name="name">Display name.</param>
	/// <param name="type">Socket data type.</param>
	/// <param name="defaultValue">Optional value shown when unconnected.</param>
	/// <returns>The newly created socket.</returns>
	public NodeSocket AddInput(string name, NodeSocketType type, string defaultValue = null)
	{
		var socket = new NodeSocket(name, type, NodeSocketDirection.Input) { Node = this, Value = defaultValue };
		Inputs.Add(socket);
		return socket;
	}

	/// <summary>Creates and adds an output socket.</summary>
	/// <param name="name">Display name.</param>
	/// <param name="type">Socket data type.</param>
	/// <returns>The newly created socket.</returns>
	public NodeSocket AddOutput(string name, NodeSocketType type)
	{
		var socket = new NodeSocket(name, type, NodeSocketDirection.Output) { Node = this };
		Outputs.Add(socket);
		return socket;
	}
}

// ───────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a directed connection from an output <see cref="NodeSocket"/> to an input socket.
/// </summary>
public class NodeConnection
{
	/// <summary>Gets the output socket that is the source of data.</summary>
	public NodeSocket Source { get; }

	/// <summary>Gets the input socket that receives data.</summary>
	public NodeSocket Target { get; }

	/// <summary>Initializes a new connection.</summary>
	/// <param name="source">Output socket (data origin).</param>
	/// <param name="target">Input socket (data destination).</param>
	public NodeConnection(NodeSocket source, NodeSocket target)
	{
		Source = source;
		Target = target;
	}

	/// <summary>
	/// Ordered intermediate waypoints in graph space used to route the wire around
	/// other nodes.  When empty the wire renders as a direct cubic bezier.
	/// When non-empty the wire threads through each waypoint in order.
	/// <para>
	/// <b>To add a waypoint:</b> hold Ctrl and left-click on the wire.<br/>
	/// <b>To move a waypoint:</b> drag its circular handle.<br/>
	/// <b>To remove a waypoint:</b> right-click its circular handle.
	/// </para>
	/// </summary>
	public List<PointF> WayPoints { get; } = new List<PointF>();
}

// ───────────────────────────────────────────────────────────────────────────────

/// <summary>Event arguments carrying a <see cref="NodeConnection"/>.</summary>
public class NodeConnectionEventArgs : EventArgs
{
	/// <summary>Gets the connection involved in the event.</summary>
	public NodeConnection Connection { get; }

	/// <summary>Initializes a new instance.</summary>
	public NodeConnectionEventArgs(NodeConnection connection) => Connection = connection;
}

/// <summary>Event arguments carrying a <see cref="NodeItem"/>.</summary>
public class NodeItemEventArgs : EventArgs
{
	/// <summary>Gets the node involved in the event, or <c>null</c> when the selection was cleared.</summary>
	public NodeItem Node { get; }

	/// <summary>Initializes a new instance.</summary>
	public NodeItemEventArgs(NodeItem node) => Node = node;
}

// ───────────────────────────────────────────────────────────────────────────────
//  GraphBookmark

/// <summary>
/// A named camera snapshot that the user can jump back to at any time.
/// Stores the <see cref="NodeGraphView.Offset"/> and <see cref="NodeGraphView.Zoom"/>
/// that were active when the bookmark was created.
/// </summary>
/// <remarks>
/// Add a bookmark by pressing <b>Ctrl+B</b> or calling
/// <see cref="NodeGraphView.AddBookmark"/>.  Use a <see cref="BookmarkPanel"/> to list,
/// navigate and delete bookmarks interactively.
/// </remarks>
public class GraphBookmark
{
	/// <summary>Unique identifier – stable across renames, suitable for serialisation.</summary>
	public string Id { get; } = Guid.NewGuid().ToString("N");

	/// <summary>Display name shown in the bookmark list.</summary>
	public string Name { get; set; } = "Bookmark";

	/// <summary>Canvas pan offset (<see cref="NodeGraphView.Offset"/>) to restore.</summary>
	public PointF Offset { get; set; }

	/// <summary>Zoom level (<see cref="NodeGraphView.Zoom"/>) to restore.</summary>
	public float Zoom { get; set; } = 1f;

	/// <summary>Arbitrary user-defined data.</summary>
	public object Tag { get; set; }
}

// ───────────────────────────────────────────────────────────────────────────────
//  NodeGroupBox

/// <summary>
/// A resizable, titled rectangle drawn behind nodes for visual organisation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Create</b> a group box by holding <b>Ctrl</b> and dragging on empty canvas space.
/// </para>
/// <para>
/// <b>Move</b> a box by dragging its title header.  All nodes whose bounds are
/// <em>fully</em> contained within the box at the time the drag starts are moved
/// along with the box.
/// </para>
/// <para>
/// When a contained node is later dragged <em>partially</em> outside the box the
/// box automatically <b>expands</b> to keep the node contained.
/// When a node is dragged <em>completely</em> outside the box it simply leaves,
/// leaving the box unchanged.
/// </para>
/// <para>
/// Press <b>Delete / Backspace</b> while a box is selected to remove it (its
/// member nodes are not affected).
/// </para>
/// </remarks>
public class NodeGroupBox
{
	/// <summary>Unique identifier – stable across renames, suitable for serialisation.</summary>
	public string Id { get; } = Guid.NewGuid().ToString("N");

	/// <summary>Display title shown in the box header band.</summary>
	public string Title { get; set; } = "Group";

	/// <summary>Accent colour used for the border and header.</summary>
	public Color Color { get; set; } = Color.FromRgb(0x4A90D9);

	/// <summary>Position and size of the box in graph space.</summary>
	public RectangleF Bounds { get; set; }

	/// <summary>Arbitrary user-defined data.</summary>
	public object Tag { get; set; }
}

// ───────────────────────────────────────────────────────────────────────────────
//  New event-arg types

/// <summary>Event arguments carrying a <see cref="GraphBookmark"/>.</summary>
public class GraphBookmarkEventArgs : EventArgs
{
	/// <summary>Gets the bookmark involved in the event.</summary>
	public GraphBookmark Bookmark { get; }
	/// <summary>Initializes a new instance.</summary>
	public GraphBookmarkEventArgs(GraphBookmark bookmark) => Bookmark = bookmark;
}

/// <summary>Event arguments carrying a <see cref="NodeGroupBox"/>.</summary>
public class NodeGroupBoxEventArgs : EventArgs
{
	/// <summary>Gets the group box involved in the event.</summary>
	public NodeGroupBox GroupBox { get; }
	/// <summary>Initializes a new instance.</summary>
	public NodeGroupBoxEventArgs(NodeGroupBox groupBox) => GroupBox = groupBox;
}

// ───────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents the data model for a node graph: a collection of <see cref="NodeItem"/>s
/// connected by <see cref="NodeConnection"/>s.
/// </summary>
public class NodeGraph
{
	/// <summary>Gets the ordered list of nodes.  The last entry is rendered on top.</summary>
	public List<NodeItem> Nodes { get; } = new List<NodeItem>();

	/// <summary>Gets all active connections in the graph.</summary>
	public List<NodeConnection> Connections { get; } = new List<NodeConnection>();

	/// <summary>Gets all camera bookmarks in the graph.</summary>
	public List<GraphBookmark> Bookmarks { get; } = new List<GraphBookmark>();

	/// <summary>Gets all group boxes in the graph.</summary>
	public List<NodeGroupBox> GroupBoxes { get; } = new List<NodeGroupBox>();

	/// <summary>Raised after a connection is added.</summary>
	public event EventHandler<NodeConnectionEventArgs> ConnectionAdded;

	/// <summary>Raised after a connection is removed.</summary>
	public event EventHandler<NodeConnectionEventArgs> ConnectionRemoved;

	/// <summary>Raised after a node is added.</summary>
	public event EventHandler<NodeItemEventArgs> NodeAdded;

	/// <summary>Raised after a node is removed.</summary>
	public event EventHandler<NodeItemEventArgs> NodeRemoved;

	/// <summary>Raised after a bookmark is added.</summary>
	public event EventHandler<GraphBookmarkEventArgs> BookmarkAdded;

	/// <summary>Raised after a bookmark is removed.</summary>
	public event EventHandler<GraphBookmarkEventArgs> BookmarkRemoved;

	/// <summary>Raised after a group box is added.</summary>
	public event EventHandler<NodeGroupBoxEventArgs> GroupBoxAdded;

	/// <summary>Raised after a group box is removed.</summary>
	public event EventHandler<NodeGroupBoxEventArgs> GroupBoxRemoved;

	/// <summary>Adds a node to the graph and raises <see cref="NodeAdded"/>.</summary>
	public NodeItem AddNode(NodeItem node)
	{
		if (node == null) throw new ArgumentNullException(nameof(node));
		Nodes.Add(node);
		NodeAdded?.Invoke(this, new NodeItemEventArgs(node));
		return node;
	}

	/// <summary>
	/// Removes a node and all its connections from the graph.
	/// </summary>
	public void RemoveNode(NodeItem node)
	{
		if (node == null || !Nodes.Contains(node)) return;
		foreach (var conn in Connections.Where(c => c.Source.Node == node || c.Target.Node == node).ToList())
			InternalRemove(conn);
		Nodes.Remove(node);
		NodeRemoved?.Invoke(this, new NodeItemEventArgs(node));
	}

	/// <summary>
	/// Creates a connection from <paramref name="output"/> to <paramref name="input"/>,
	/// replacing any existing connection on <paramref name="input"/> if it does not allow
	/// multiple connections.
	/// </summary>
	/// <returns>The new connection, or <c>null</c> if the types are incompatible.</returns>
	public NodeConnection Connect(NodeSocket output, NodeSocket input)
	{
		if (output == null) throw new ArgumentNullException(nameof(output));
		if (input  == null) throw new ArgumentNullException(nameof(input));
		if (output.Direction != NodeSocketDirection.Output)
			throw new InvalidOperationException("First argument must be an output socket.");
		if (input.Direction != NodeSocketDirection.Input)
			throw new InvalidOperationException("Second argument must be an input socket.");
		if (!output.SocketType.IsCompatibleWith(input.SocketType))
			return null;

		if (!input.AllowMultipleConnections)
			foreach (var existing in input.Connections.ToList())
				InternalRemove(existing);

		var conn = new NodeConnection(output, input);
		output.Connections.Add(conn);
		input.Connections.Add(conn);
		Connections.Add(conn);
		ConnectionAdded?.Invoke(this, new NodeConnectionEventArgs(conn));
		return conn;
	}

	/// <summary>Removes the specified connection from the graph.</summary>
	public void RemoveConnection(NodeConnection connection)
	{
		if (connection == null || !Connections.Contains(connection)) return;
		InternalRemove(connection);
	}

	/// <summary>Adds <paramref name="bookmark"/> to the graph and raises <see cref="BookmarkAdded"/>.</summary>
	public GraphBookmark AddBookmark(GraphBookmark bookmark)
	{
		if (bookmark == null) throw new ArgumentNullException(nameof(bookmark));
		Bookmarks.Add(bookmark);
		BookmarkAdded?.Invoke(this, new GraphBookmarkEventArgs(bookmark));
		return bookmark;
	}

	/// <summary>Removes <paramref name="bookmark"/> from the graph and raises <see cref="BookmarkRemoved"/>.</summary>
	public void RemoveBookmark(GraphBookmark bookmark)
	{
		if (bookmark == null || !Bookmarks.Contains(bookmark)) return;
		Bookmarks.Remove(bookmark);
		BookmarkRemoved?.Invoke(this, new GraphBookmarkEventArgs(bookmark));
	}

	/// <summary>Adds <paramref name="box"/> to the graph and raises <see cref="GroupBoxAdded"/>.</summary>
	public NodeGroupBox AddGroupBox(NodeGroupBox box)
	{
		if (box == null) throw new ArgumentNullException(nameof(box));
		GroupBoxes.Add(box);
		GroupBoxAdded?.Invoke(this, new NodeGroupBoxEventArgs(box));
		return box;
	}

	/// <summary>Removes <paramref name="box"/> from the graph and raises <see cref="GroupBoxRemoved"/>.</summary>
	public void RemoveGroupBox(NodeGroupBox box)
	{
		if (box == null || !GroupBoxes.Contains(box)) return;
		GroupBoxes.Remove(box);
		GroupBoxRemoved?.Invoke(this, new NodeGroupBoxEventArgs(box));
	}

	private void InternalRemove(NodeConnection connection)
	{
		connection.Source.Connections.Remove(connection);
		connection.Target.Connections.Remove(connection);
		Connections.Remove(connection);
		ConnectionRemoved?.Invoke(this, new NodeConnectionEventArgs(connection));
	}
}

// ───────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A cross-platform, interactive node-graph canvas control built on <see cref="Drawable"/>.
/// </summary>
/// <remarks>
/// <para>
/// The view renders a <see cref="NodeGraph"/> data model and allows the user to:
/// <list type="bullet">
///   <item>Drag nodes to reposition them (Shift+click for multi-select).</item>
///   <item>Left-drag from an output socket to an input socket to create a connection.</item>
///   <item>Left-drag from a connected input socket to re-route its connection.</item>
///   <item>Right-click a connection to delete it; Delete/Backspace removes selected nodes/boxes.</item>
///   <item>Mouse-wheel to zoom; middle-mouse-drag (or Alt+left-drag) to pan.</item>
///   <item>Press F to frame all nodes; Ctrl+A to select all nodes.</item>
///   <item><b>Bookmarks:</b> Ctrl+B to add a bookmark at the current camera position; use <see cref="BookmarkPanel"/> to navigate and delete.</item>
///   <item><b>Wire pins:</b> Ctrl+left-click a wire to insert a waypoint; drag waypoints to reroute; right-click a waypoint to remove it.</item>
///   <item><b>Group boxes:</b> Ctrl+drag on empty canvas to draw a new box; drag a box header to move the box and its enclosed nodes; Delete removes a selected box.</item>
/// </list>
/// </para>
/// <para>
/// Because the control is a pure <see cref="Drawable"/> subclass it runs unchanged on all
/// Eto targets: WinForms, WPF, GTK and macOS.
/// </para>
/// </remarks>
public class NodeGraphView : Drawable
{
	// ── Layout constants ────────────────────────────────────────────────────────

	/// <summary>Default node width in graph units.</summary>
	public const float DefaultNodeWidth = 180f;

	private const float HeaderHeight         = 28f;
	private const float SocketRowHeight      = 24f;
	private const float SocketRadius         = 7f;
	private const float SocketHitRadius      = 12f;
	private const float ControlFlowHalfSize  = 7f;    // half the side length of the exec triangle
	private const float NodePadding          = 10f;
	private const float HiddenLabelRowHeight = 18f;
	private const float MinZoom              = 0.15f;
	private const float MaxZoom              = 4f;
	private const float GridSize             = 20f;

	// ── Minimap constants ────────────────────────────────────────────────────────
	private const float MinimapWidth    = 200f;
	private const float MinimapHeight   = 140f;
	private const float MinimapMargin   = 12f;   // gap from the canvas edge
	private const float MinimapPadding  = 6f;    // inner padding inside the minimap frame

	// ── Group-box & waypoint constants ───────────────────────────────────────────
	private const float BoxHeaderHeight  = 24f;   // height of the title band on a group box
	private const float MinBoxBodyHeight = 20f;   // minimum body height below the header band
	private const float WaypointRadius   = 5f;    // drawn circle radius for a wire-pin handle
	private const float WaypointHitRadius = 10f;  // click / drag hit-test radius for wire-pin handles
	private const float MinBoxCreateSize = 40f;   // rubber-band must exceed this in both axes

	// ── Display colors ──────────────────────────────────────────────────────────
	private static readonly Color s_canvasColor        = Color.FromRgb(0x1E1E2E);
	private static readonly Color s_gridColor          = Color.FromArgb(50, 50, 70, 255);
	private static readonly Color s_nodeBodyColor      = Color.FromRgb(0x2A2A3E);
	private static readonly Color s_nodeBorderColor    = Color.FromRgb(0x3A3A55);
	private static readonly Color s_nodeSelColor       = Colors.White;
	private static readonly Color s_headerTextColor    = Colors.White;
	private static readonly Color s_socketLabelColor   = Color.FromRgb(0xCCCCCC);
	private static readonly Color s_valueColor         = Color.FromRgb(0xF5A623);
	private static readonly Color s_hiddenLabelColor   = Color.FromRgb(0x777788);
	private static readonly Color s_defaultHeaderColor = Color.FromRgb(0x3D3D6B);

	// ── Minimap colors ───────────────────────────────────────────────────────────
	private static readonly Color s_minimapBgColor       = Color.FromArgb(20, 20, 32, 210);
	private static readonly Color s_minimapBorderColor   = Color.FromRgb(0x55557A);
	private static readonly Color s_minimapViewportColor = Color.FromArgb(255, 255, 255, 50);
	private static readonly Color s_minimapViewBorder    = Color.FromArgb(255, 255, 255, 160);
	private static readonly Color s_minimapConnColor     = Color.FromArgb(160, 160, 160, 100);

	// ── State ───────────────────────────────────────────────────────────────────
	private NodeGraph _graph;
	private PointF    _offset = new PointF(30f, 30f);
	private float     _zoom   = 1f;
	private Font      _headerFont;
	private Font      _labelFont;

	// ── Interaction ─────────────────────────────────────────────────────────────
	private NodeItem                    _dragNode;
	private PointF                      _dragMouseStart;
	private Dictionary<NodeItem, PointF> _dragStartPositions;
	private bool                        _isPanning;
	private PointF                      _panStart;
	private bool                        _spaceDown;
	private NodeSocket                  _connectingFrom;
	private PointF                      _connectingToView;   // view-space endpoint of pending connection
	private readonly List<NodeItem>     _selection = new List<NodeItem>();
	private NodeConnection              _hoveredConnection;
	private NodeSocket                  _hoveredSocket;

	// ── Minimap state ────────────────────────────────────────────────────────────
	private bool   _minimapDragging;

	// ── Group-box interaction ────────────────────────────────────────────────────
	private NodeGroupBox                        _selectedBox;
	private NodeGroupBox                        _dragBox;
	private PointF                              _dragBoxMouseStart;
	private RectangleF                          _dragBoxOrigBounds;
	private List<(NodeItem node, PointF start)> _dragBoxNodeStarts;

	// rubber-band new-box creation (Ctrl+drag on empty canvas)
	private bool   _drawingBox;
	private PointF _drawBoxAnchor;    // graph-space corner where the drag began
	private PointF _drawBoxCurrent;   // graph-space current mouse position

	// ── Wire-pin (waypoint) interaction ──────────────────────────────────────────
	private bool           _draggingPin;
	private NodeConnection _dragPinConn;
	private int            _dragPinIdx;
	private PointF         _dragPinStart;       // graph-space position at drag start
	private PointF         _dragPinMouseStart;  // view-space mouse position at drag start

	// ── Node-drag / box-membership tracking ──────────────────────────────────────
	// For each dragged node: which boxes fully contained it before the drag started?
	private Dictionary<NodeItem, List<NodeGroupBox>> _dragNodeBoxMemberships;

	// ── Monotonically increasing counter for default bookmark names ───────────────
	private int _bookmarkAutoNameIndex;

	// ── Public API ──────────────────────────────────────────────────────────────

	/// <summary>Gets or sets the graph data model displayed by the control.</summary>
	public NodeGraph Graph
	{
		get => _graph;
		set
		{
			UnsubscribeGraph();
			_graph = value;
			_selection.Clear();
			_connectingFrom = null;
			SubscribeGraph();
			LayoutAllNodes();
			Invalidate();
			GraphChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	/// <summary>Raised when the <see cref="Graph"/> property is assigned a new value.</summary>
	public event EventHandler<EventArgs> GraphChanged;

	/// <summary>Gets or sets the current zoom level (clamped to [0.15, 4.0]).</summary>
	public float Zoom
	{
		get => _zoom;
		set { _zoom = Math.Max(MinZoom, Math.Min(MaxZoom, value)); Invalidate(); }
	}

	/// <summary>Gets or sets the canvas pan offset in view pixels.</summary>
	public PointF Offset
	{
		get => _offset;
		set { _offset = value; Invalidate(); }
	}

	/// <summary>Gets the current node selection.</summary>
	public IReadOnlyList<NodeItem> SelectedNodes => _selection.AsReadOnly();

	/// <summary>
	/// Returns the visible socket under a view-space point, or <c>null</c> when the
	/// point is not over a socket. This is intended for host applications that
	/// provide socket-specific gestures such as inline editing.
	/// </summary>
	public NodeSocket HitTestSocketAt(PointF viewPoint) => HitTestSocket(viewPoint);

	/// <summary>Raised when the node selection changes.</summary>
	public event EventHandler<NodeItemEventArgs> SelectionChanged;

	/// <summary>Raised after a connection is created via user interaction.</summary>
	public event EventHandler<NodeConnectionEventArgs> ConnectionCreated;

	/// <summary>Raised after a connection is deleted via user interaction.</summary>
	public event EventHandler<NodeConnectionEventArgs> ConnectionDeleted;

	/// <summary>
	/// Gets or sets whether the minimap overlay is visible in the bottom-right corner.
	/// Defaults to <c>true</c>.
	/// </summary>
	public bool ShowMinimap { get; set; } = true;

	/// <summary>
	/// Creates a bookmark from the current camera position and zoom level and adds it to
	/// <see cref="Graph"/>.  Returns <c>null</c> when no graph is set.
	/// </summary>
	/// <param name="name">
	/// Display name for the new bookmark.
	/// Defaults to <c>"Bookmark N"</c> where N is the one-based bookmark count.
	/// </param>
	public GraphBookmark AddBookmark(string name = null)
	{
		if (_graph == null) return null;
		var bm = new GraphBookmark
		{
			Name   = name ?? $"Bookmark {++_bookmarkAutoNameIndex}",
			Offset = _offset,
			Zoom   = _zoom,
		};
		return _graph.AddBookmark(bm);
	}

	/// <summary>
	/// Restores the camera offset and zoom stored in <paramref name="bookmark"/>.
	/// Does nothing when <paramref name="bookmark"/> is <c>null</c>.
	/// </summary>
	public void JumpToBookmark(GraphBookmark bookmark)
	{
		if (bookmark == null) return;
		_offset = bookmark.Offset;
		_zoom   = bookmark.Zoom;
		Invalidate();
	}

	/// <summary>Gets the group box that is currently selected, or <c>null</c>.</summary>
	public NodeGroupBox SelectedGroupBox => _selectedBox;

	// ── Internal helpers ─────────────────────────────────────────────────────────

	/// <summary>Clears the group-box selection without triggering an additional repaint
	/// (the caller is expected to call Invalidate as part of a larger update).</summary>
	private void ClearBoxSelection() => _selectedBox = null;

	/// <summary>
	/// Re-computes the layout for <paramref name="node"/> and redraws the canvas.
	/// Call this after toggling <see cref="NodeSocket.IsPinned"/> on any of the node's sockets
	/// so that the canvas immediately reflects the change.
	/// </summary>
	public void InvalidateNode(NodeItem node)
	{
		if (node != null) LayoutNode(node);
		Invalidate();
	}

	// ── Construction ────────────────────────────────────────────────────────────

	/// <summary>Initializes a new <see cref="NodeGraphView"/>.</summary>
	public NodeGraphView()
	{
		CanFocus = true;
		base.BackgroundColor = s_canvasColor;
		_headerFont = new Font(SystemFont.Bold, 9f);
		_labelFont  = new Font(SystemFont.Default, 8f);
	}

	// ── Graph subscription ──────────────────────────────────────────────────────

	private void SubscribeGraph()
	{
		if (_graph == null) return;
		_graph.ConnectionAdded   += OnGraphChanged;
		_graph.ConnectionRemoved += OnGraphChanged;
		_graph.NodeAdded         += OnGraphChanged;
		_graph.NodeRemoved       += OnGraphChanged;
		_graph.BookmarkAdded     += OnGraphChanged;
		_graph.BookmarkRemoved   += OnGraphChanged;
		_graph.GroupBoxAdded     += OnGraphChanged;
		_graph.GroupBoxRemoved   += OnGraphChanged;
	}

	private void UnsubscribeGraph()
	{
		if (_graph == null) return;
		_graph.ConnectionAdded   -= OnGraphChanged;
		_graph.ConnectionRemoved -= OnGraphChanged;
		_graph.NodeAdded         -= OnGraphChanged;
		_graph.NodeRemoved       -= OnGraphChanged;
		_graph.BookmarkAdded     -= OnGraphChanged;
		_graph.BookmarkRemoved   -= OnGraphChanged;
		_graph.GroupBoxAdded     -= OnGraphChanged;
		_graph.GroupBoxRemoved   -= OnGraphChanged;
	}

	private void OnGraphChanged(object sender, EventArgs e)
	{
		LayoutAllNodes();
		Invalidate();
	}

	// ── Layout ──────────────────────────────────────────────────────────────────

	private void LayoutAllNodes()
	{
		if (_graph == null) return;
		foreach (var node in _graph.Nodes)
			LayoutNode(node);
	}

	private void LayoutNode(NodeItem node)
	{
		int pinnedIn  = node.Inputs.Count(s => s.IsPinned);
		int pinnedOut = node.Outputs.Count(s => s.IsPinned);
		int rows      = Math.Max(1, Math.Max(pinnedIn, pinnedOut));
		float hiddenExtra = node.HiddenSocketCount > 0 ? HiddenLabelRowHeight : 0f;
		node.ComputedHeight = HeaderHeight + rows * SocketRowHeight + NodePadding * 2 + hiddenExtra;
	}

	// ── Coordinate transforms ───────────────────────────────────────────────────

	private PointF GraphToView(PointF p) =>
		new PointF(p.X * _zoom + _offset.X, p.Y * _zoom + _offset.Y);

	/// <summary>
	/// Converts a point from view (pixel) coordinates to graph (logical) coordinates.
	/// </summary>
	/// <param name="viewPoint">A point in view space (e.g. a mouse position).</param>
	/// <returns>The equivalent point in graph space.</returns>
	public PointF ViewToGraph(PointF viewPoint) =>
		new PointF((viewPoint.X - _offset.X) / _zoom, (viewPoint.Y - _offset.Y) / _zoom);

	/// <summary>Returns the center position of <paramref name="socket"/> in graph space.</summary>
	private PointF GetSocketCenter(NodeSocket socket)
	{
		var node  = socket.Node;
		float bodyY = node.Position.Y + HeaderHeight + NodePadding;
		if (socket.Direction == NodeSocketDirection.Input)
		{
			var pinned = node.Inputs.Where(s => s.IsPinned).ToList();
			int idx = pinned.IndexOf(socket);
			if (idx < 0) idx = 0; // unpinned (e.g. pending connection drag) — place at top
			return new PointF(
				node.Position.X,
				bodyY + idx * SocketRowHeight + SocketRowHeight / 2f);
		}
		else
		{
			var pinned = node.Outputs.Where(s => s.IsPinned).ToList();
			int idx = pinned.IndexOf(socket);
			if (idx < 0) idx = 0;
			return new PointF(
				node.Position.X + node.ComputedWidth,
				bodyY + idx * SocketRowHeight + SocketRowHeight / 2f);
		}
	}

	// ── Hit testing (view space) ─────────────────────────────────────────────────

	private NodeItem HitTestNode(PointF viewPoint)
	{
		if (_graph == null) return null;
		var gp = ViewToGraph(viewPoint);
		// Reverse order so topmost (last in list) is tested first
		for (int i = _graph.Nodes.Count - 1; i >= 0; i--)
		{
			var n = _graph.Nodes[i];
			if (n.Bounds.Contains(gp)) return n;
		}
		return null;
	}

	private NodeSocket HitTestSocket(PointF viewPoint)
	{
		if (_graph == null) return null;
		var gp = ViewToGraph(viewPoint);
		float hitR2 = (SocketHitRadius / _zoom) * (SocketHitRadius / _zoom);
		foreach (var node in _graph.Nodes)
		{
			foreach (var s in node.Inputs.Concat<NodeSocket>(node.Outputs).Where(s => s.IsPinned))
			{
				var c = GetSocketCenter(s);
				float dx = c.X - gp.X, dy = c.Y - gp.Y;
				if (dx * dx + dy * dy <= hitR2) return s;
			}
		}
		return null;
	}

	private NodeConnection HitTestConnection(PointF viewPoint)
	{
		if (_graph == null) return null;
		var gp = ViewToGraph(viewPoint);
		float threshold = 5f / _zoom;
		foreach (var conn in _graph.Connections)
		{
			if (BezierContains(conn, gp, threshold))
				return conn;
		}
		return null;
	}

	/// <summary>
	/// Returns the group box whose title-header band contains <paramref name="viewPoint"/>,
	/// or <c>null</c>.  Checked in reverse order so boxes drawn later (on top) win.
	/// </summary>
	private NodeGroupBox HitTestGroupBoxHeader(PointF viewPoint)
	{
		if (_graph == null) return null;
		var gp = ViewToGraph(viewPoint);
		for (int i = _graph.GroupBoxes.Count - 1; i >= 0; i--)
		{
			var box     = _graph.GroupBoxes[i];
			var headerR = new RectangleF(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, BoxHeaderHeight);
			if (headerR.Contains(gp)) return box;
		}
		return null;
	}

	/// <summary>
	/// Returns the group box whose full bounds contain <paramref name="viewPoint"/>,
	/// or <c>null</c>.
	/// </summary>
	private NodeGroupBox HitTestGroupBox(PointF viewPoint)
	{
		if (_graph == null) return null;
		var gp = ViewToGraph(viewPoint);
		for (int i = _graph.GroupBoxes.Count - 1; i >= 0; i--)
		{
			var box = _graph.GroupBoxes[i];
			if (box.Bounds.Contains(gp)) return box;
		}
		return null;
	}

	/// <summary>
	/// Returns the connection and waypoint index whose handle is nearest to
	/// <paramref name="viewPoint"/> within hit-test radius, or <c>(null, -1)</c>.
	/// </summary>
	private (NodeConnection conn, int idx) HitTestWaypoint(PointF viewPoint)
	{
		if (_graph == null) return (null, -1);
		var gp = ViewToGraph(viewPoint);
		float hitR2 = (WaypointHitRadius / _zoom) * (WaypointHitRadius / _zoom);
		foreach (var conn in _graph.Connections)
		{
			for (int i = 0; i < conn.WayPoints.Count; i++)
			{
				var wp = conn.WayPoints[i];
				float dx = wp.X - gp.X, dy = wp.Y - gp.Y;
				if (dx * dx + dy * dy <= hitR2)
					return (conn, i);
			}
		}
		return (null, -1);
	}

	// ── Geometry helpers ─────────────────────────────────────────────────────────

	/// <summary>Returns <c>true</c> when <paramref name="inner"/> is fully enclosed by <paramref name="outer"/>.</summary>
	private static bool RectContainsRect(RectangleF outer, RectangleF inner) =>
		inner.Left   >= outer.Left  &&
		inner.Top    >= outer.Top   &&
		inner.Right  <= outer.Right &&
		inner.Bottom <= outer.Bottom;

	/// <summary>Returns <c>true</c> when the two rectangles overlap.</summary>
	private static bool RectIntersects(RectangleF a, RectangleF b) =>
		a.Left < b.Right  && a.Right  > b.Left &&
		a.Top  < b.Bottom && a.Bottom > b.Top;

	/// <summary>Returns the smallest rectangle that encloses both inputs.</summary>
	private static RectangleF RectUnion(RectangleF a, RectangleF b)
	{
		float minX = Math.Min(a.Left,   b.Left);
		float minY = Math.Min(a.Top,    b.Top);
		float maxX = Math.Max(a.Right,  b.Right);
		float maxY = Math.Max(a.Bottom, b.Bottom);
		return new RectangleF(minX, minY, maxX - minX, maxY - minY);
	}

	/// <summary>Returns all nodes whose full <see cref="NodeItem.Bounds"/> lie inside <paramref name="box"/>.</summary>
	private List<NodeItem> GetFullyContainedNodes(NodeGroupBox box)
	{
		var result = new List<NodeItem>();
		if (_graph == null) return result;
		foreach (var node in _graph.Nodes)
			if (RectContainsRect(box.Bounds, node.Bounds))
				result.Add(node);
		return result;
	}

	/// <summary>
	/// Returns the index at which a new waypoint should be inserted into
	/// <c>conn.WayPoints</c> so it is placed on the bezier segment nearest
	/// to <paramref name="gp"/>.
	/// </summary>
	private int FindWaypointInsertIndex(NodeConnection conn, PointF gp)
	{
		var src = GetSocketCenter(conn.Source);
		var tgt = GetSocketCenter(conn.Target);

		var pts = new List<PointF>(conn.WayPoints.Count + 2);
		pts.Add(src);
		pts.AddRange(conn.WayPoints);
		pts.Add(tgt);

		float minDist2 = float.MaxValue;
		int   bestSeg  = 0;

		for (int seg = 0; seg < pts.Count - 1; seg++)
		{
			var (c1, c2) = BezierControlPoints(pts[seg], pts[seg + 1], srcIsOutput: true);
			for (int i = 0; i <= 12; i++)
			{
				float t  = i / 12f;
				var   pt = CubicBezierPoint(pts[seg], c1, c2, pts[seg + 1], t);
				float dx = pt.X - gp.X, dy = pt.Y - gp.Y;
				float d2 = dx * dx + dy * dy;
				if (d2 < minDist2) { minDist2 = d2; bestSeg = seg; }
			}
		}

		// bestSeg 0 = insert before first waypoint (i.e. WayPoints.Insert(0, …))
		// bestSeg k = insert at WayPoints index k
		return bestSeg;
	}

	private bool BezierContains(NodeConnection conn, PointF gp, float threshold)
	{
		var src = GetSocketCenter(conn.Source);
		var tgt = GetSocketCenter(conn.Target);

		// Build full point list: [src, wp0, …, wn, tgt]
		var pts = new List<PointF>(conn.WayPoints.Count + 2);
		pts.Add(src);
		pts.AddRange(conn.WayPoints);
		pts.Add(tgt);

		float t2 = threshold * threshold;
		for (int seg = 0; seg < pts.Count - 1; seg++)
		{
			var (c1, c2) = BezierControlPoints(pts[seg], pts[seg + 1], srcIsOutput: true);
			for (int i = 0; i <= 24; i++)
			{
				float t  = i / 24f;
				var   pt = CubicBezierPoint(pts[seg], c1, c2, pts[seg + 1], t);
				float dx = pt.X - gp.X, dy = pt.Y - gp.Y;
				if (dx * dx + dy * dy <= t2) return true;
			}
		}
		return false;
	}

	// ── Bezier helpers ──────────────────────────────────────────────────────────

	/// <summary>
	/// Computes bezier control points for a connection.  Output-socket curves go right; input-socket curves go left.
	/// </summary>
	private static (PointF c1, PointF c2) BezierControlPoints(PointF src, PointF tgt, bool srcIsOutput)
	{
		float cp = Math.Abs(tgt.X - src.X) * 0.5f + 40f;
		if (srcIsOutput)
			return (new PointF(src.X + cp, src.Y), new PointF(tgt.X - cp, tgt.Y));
		else
			return (new PointF(src.X - cp, src.Y), new PointF(tgt.X + cp, tgt.Y));
	}

	private static PointF CubicBezierPoint(PointF p0, PointF p1, PointF p2, PointF p3, float t)
	{
		float u = 1f - t;
		return new PointF(
			u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
			u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y);
	}

	// ── Painting ─────────────────────────────────────────────────────────────────

	/// <inheritdoc/>
	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		var g = e.Graphics;
		g.AntiAlias = true;

		g.Clear(s_canvasColor);
		DrawGrid(g);

		g.SaveTransform();
		g.TranslateTransform(_offset.X, _offset.Y);
		g.ScaleTransform(_zoom, _zoom);

		if (_graph != null)
		{
			// Group boxes are drawn behind connections and nodes
			foreach (var box in _graph.GroupBoxes)
				DrawGroupBox(g, box, box == _selectedBox);

			foreach (var conn in _graph.Connections)
				DrawConnection(g, conn, conn == _hoveredConnection);

			foreach (var node in _graph.Nodes)
				DrawNode(g, node);
		}

		if (_connectingFrom != null)
			DrawPendingConnection(g);

		// Rubber-band preview while the user draws a new group box
		if (_drawingBox)
			DrawRubberBandBox(g);
		g.RestoreTransform();

		// The minimap is a device-space overlay, never affected by graph pan/zoom.
		DrawMinimap(g);
	}

	private void DrawGrid(Graphics g)
	{
		float scaledGrid = GridSize * _zoom;
		if (scaledGrid < 4f) return;

		float startX = _offset.X % scaledGrid;
		float startY = _offset.Y % scaledGrid;
		if (startX < 0) startX += scaledGrid;
		if (startY < 0) startY += scaledGrid;

		using (var pen = new Pen(s_gridColor, 1f))
		{
			for (float x = startX; x <= Width;  x += scaledGrid)
				g.DrawLine(pen, x, 0, x, Height);
			for (float y = startY; y <= Height; y += scaledGrid)
				g.DrawLine(pen, 0, y, Width, y);
		}
	}

	private void DrawNode(Graphics g, NodeItem node)
	{
		bool   selected = _selection.Contains(node);
		float  x = node.Position.X, y = node.Position.Y;
		float  w = node.ComputedWidth, h = node.ComputedHeight;

		// Drop shadow
		using (var shadow = new SolidBrush(Color.FromArgb(0, 0, 0, 70)))
			g.FillRectangle(shadow, x + 4, y + 4, w, h);

		// Node body
		g.FillRectangle(s_nodeBodyColor, x, y, w, h);

		// Header band
		var headerColor = (node.HeaderColor.A < 0.01f) ? s_defaultHeaderColor : node.HeaderColor;
		g.FillRectangle(headerColor, x, y, w, HeaderHeight);

		// Title text
		float titleY = y + (HeaderHeight - _headerFont.LineHeight) / 2f;
		g.DrawText(_headerFont, s_headerTextColor, x + NodePadding, titleY, node.Title);

		// Border
		using (var pen = new Pen(selected ? s_nodeSelColor : s_nodeBorderColor, selected ? 2f : 1f))
			g.DrawRectangle(pen, x, y, w, h);

		// Pinned sockets only
		foreach (var s in node.Inputs.Where(sock => sock.IsPinned))  DrawSocket(g, s);
		foreach (var s in node.Outputs.Where(sock => sock.IsPinned)) DrawSocket(g, s);

		// "N more…" indicator when some sockets are hidden
		int hidden = node.HiddenSocketCount;
		if (hidden > 0)
		{
			float indY = y + h - HiddenLabelRowHeight + (HiddenLabelRowHeight - _labelFont.LineHeight) / 2f;
			g.DrawText(_labelFont, s_hiddenLabelColor, x + NodePadding, indY,
				$"+ {hidden} more… (see Properties)");
		}
	}

	private void DrawSocket(Graphics g, NodeSocket socket)
	{
		var   center    = GetSocketCenter(socket);
		var   sockColor = socket.SocketType.Color;
		bool  isOutput  = socket.Direction == NodeSocketDirection.Output;
		bool  isControlFlow = IsControlFlowSocket(socket);
		bool  hovered   = socket == _hoveredSocket;

		float outerR; // effective radius/half-size for label offset

		if (isControlFlow)
		{
			outerR = ControlFlowHalfSize + (hovered ? 2f : 0f);
			DrawControlFlowTriangle(g, center, sockColor, socket.IsConnected, hovered);
		}
		else
		{
			float r = hovered ? SocketRadius + 2f : SocketRadius;
			outerR = r;
			var circleRect = new RectangleF(center.X - r, center.Y - r, r * 2f, r * 2f);
			if (socket.IsConnected)
			{
				g.FillEllipse(sockColor, circleRect);
			}
			else
			{
				g.FillEllipse(s_nodeBodyColor, circleRect);
				using (var pen = new Pen(sockColor, 2f))
					g.DrawEllipse(pen, circleRect);
			}
		}

		// Socket label
		float labelY = center.Y - _labelFont.LineHeight / 2f;
		if (isOutput)
		{
			float textW  = g.MeasureString(_labelFont, socket.Name).Width;
			float labelX = center.X - outerR - NodePadding / 2f - textW;
			g.DrawText(_labelFont, s_socketLabelColor, labelX, labelY, socket.Name);
		}
		else
		{
			float labelX = center.X + outerR + NodePadding / 2f;
			g.DrawText(_labelFont, s_socketLabelColor, labelX, labelY, socket.Name);

			// Default value badge for unconnected inputs
			if (!socket.IsConnected && socket.Value != null)
			{
				float nameW = g.MeasureString(_labelFont, socket.Name).Width;
				g.DrawText(_labelFont, s_valueColor, labelX + nameW + 4f, labelY, socket.Value);
			}
		}
	}

	/// <summary>Draws a right-pointing triangle for a ControlFlow socket.</summary>
	private void DrawControlFlowTriangle(Graphics g, PointF center, Color color, bool connected, bool hovered)
	{
		float hs = ControlFlowHalfSize + (hovered ? 2f : 0f);
		var tri = new[]
		{
			new PointF(center.X - hs, center.Y - hs),
			new PointF(center.X + hs, center.Y),
			new PointF(center.X - hs, center.Y + hs),
		};

		if (connected)
		{
			g.FillPolygon(color, tri);
		}
		else
		{
			g.FillPolygon(s_nodeBodyColor, tri);
			using (var pen = new Pen(color, 2f))
				g.DrawPolygon(pen, tri);
		}
	}

	private void DrawConnection(Graphics g, NodeConnection conn, bool hovered)
	{
		// Skip connections where either endpoint is unpinned (hidden from canvas)
		if (!conn.Source.IsPinned || !conn.Target.IsPinned) return;

		var src = GetSocketCenter(conn.Source);
		var tgt = GetSocketCenter(conn.Target);

		// Build full point list: [src, wp0, …, wn, tgt]
		var pts = new List<PointF>(conn.WayPoints.Count + 2);
		pts.Add(src);
		pts.AddRange(conn.WayPoints);
		pts.Add(tgt);

		bool isCtrlFlow = IsControlFlowSocket(conn.Source) || IsControlFlowSocket(conn.Target);

		if (isCtrlFlow)
		{
			using (var pen = new Pen(Color.FromRgb(0xDDDDDD), hovered ? 3f : 2.5f))
			{
				for (int seg = 0; seg < pts.Count - 1; seg++)
				{
					var (c1, c2) = BezierControlPoints(pts[seg], pts[seg + 1], srcIsOutput: true);
					using (var path = new GraphicsPath())
					{
						path.AddBezier(pts[seg], c1, c2, pts[seg + 1]);
						g.DrawPath(pen, path);
					}
				}
			}
		}
		else
		{
			var color = BlendColors(conn.Source.SocketType.Color, conn.Target.SocketType.Color);
			for (int seg = 0; seg < pts.Count - 1; seg++)
				DrawBezier(g, pts[seg], pts[seg + 1], srcIsOutput: true, color: color,
				           width: hovered ? 3f : 2f, dashed: false);
		}

		// Draw waypoint handles on top of the wire segments
		if (conn.WayPoints.Count > 0)
		{
			var handleColor = isCtrlFlow
				? Color.FromRgb(0xDDDDDD)
				: BlendColors(conn.Source.SocketType.Color, conn.Target.SocketType.Color);
			foreach (var wp in conn.WayPoints)
				DrawWaypointHandle(g, wp, handleColor);
		}
	}

	/// <summary>
	/// Draws a small circular handle for a wire-pin waypoint at <paramref name="wp"/>
	/// in graph space.
	/// </summary>
	private void DrawWaypointHandle(Graphics g, PointF wp, Color color)
	{
		float r = WaypointRadius;
		g.FillEllipse(Color.FromArgb(color.Rb, color.Gb, color.Bb, 200), wp.X - r, wp.Y - r, r * 2f, r * 2f);
		using (var pen = new Pen(Colors.White, 1f))
			g.DrawEllipse(pen, wp.X - r, wp.Y - r, r * 2f, r * 2f);
	}

	/// <summary>Draws a group box (in graph space, called inside the transform).</summary>
	private void DrawGroupBox(Graphics g, NodeGroupBox box, bool selected)
	{
		var  r   = box.Bounds;
		var  col = box.Color;

		// Semi-transparent body fill
		g.FillRectangle(Color.FromArgb(col.Rb, col.Gb, col.Bb, 25), r);

		// Header band
		g.FillRectangle(Color.FromArgb(col.Rb, col.Gb, col.Bb, 70),
		                r.X, r.Y, r.Width, BoxHeaderHeight);

		// Border – highlighted when selected
		using (var pen = new Pen(selected ? Colors.White : col, selected ? 2f : 1.5f))
			g.DrawRectangle(pen, r);

		// Title
		float titleY = r.Y + (BoxHeaderHeight - _labelFont.LineHeight) / 2f;
		g.DrawText(_labelFont, Colors.White, r.X + 6f, titleY, box.Title);
	}

	/// <summary>
	/// Returns the rubber-band rectangle in graph space from the two anchor points.
	/// </summary>
	private RectangleF GetDrawingBoxRect()
	{
		float x = Math.Min(_drawBoxAnchor.X, _drawBoxCurrent.X);
		float y = Math.Min(_drawBoxAnchor.Y, _drawBoxCurrent.Y);
		float w = Math.Abs(_drawBoxCurrent.X - _drawBoxAnchor.X);
		float h = Math.Abs(_drawBoxCurrent.Y - _drawBoxAnchor.Y);
		return new RectangleF(x, y, w, h);
	}

	/// <summary>Draws the rubber-band preview for a group box being drawn (in graph space).</summary>
	private void DrawRubberBandBox(Graphics g)
	{
		var r = GetDrawingBoxRect();
		g.FillRectangle(Color.FromArgb(200, 210, 255, 18), r);
		using (var pen = new Pen(Colors.White, 1.5f) { DashStyle = DashStyles.Dash })
			g.DrawRectangle(pen, r);
	}

	private void DrawPendingConnection(Graphics g)
	{
		var src = GetSocketCenter(_connectingFrom);
		var tgt = ViewToGraph(_connectingToView);
		DrawBezier(g, src, tgt,
			srcIsOutput: _connectingFrom.Direction == NodeSocketDirection.Output,
			color: _connectingFrom.SocketType.Color,
			width: 2f,
			dashed: true);
	}

	private void DrawBezier(Graphics g, PointF src, PointF tgt, bool srcIsOutput, Color color, float width, bool dashed)
	{
		var (c1, c2) = BezierControlPoints(src, tgt, srcIsOutput);

		using (var path = new GraphicsPath())
		{
			path.AddBezier(src, c1, c2, tgt);

			// Soft glow behind the line
			using (var glowPen = new Pen(Color.FromArgb(color.Rb, color.Gb, color.Bb, 45), width + 5f))
				g.DrawPath(glowPen, path);

			using (var pen = new Pen(color, width))
			{
				if (dashed) pen.DashStyle = DashStyles.Dash;
				g.DrawPath(pen, path);
			}
		}
	}

	private static Color BlendColors(Color a, Color b) =>
		new Color((a.R + b.R) / 2f, (a.G + b.G) / 2f, (a.B + b.B) / 2f);

	/// <summary>
	/// Returns <c>true</c> when <paramref name="socket"/> carries control-flow (execution order) data,
	/// identified by the socket type name "ControlFlow" (case-insensitive).
	/// </summary>
	private static bool IsControlFlowSocket(NodeSocket socket) =>
		string.Equals(socket.SocketType.Name, "ControlFlow", StringComparison.OrdinalIgnoreCase);

	// ── Minimap ───────────────────────────────────────────────────────────────────

	/// <summary>
	/// Returns the minimap panel rectangle in view (pixel) coordinates,
	/// anchored to the bottom-right corner of the canvas.
	/// </summary>
	private RectangleF GetMinimapRect() =>
		new RectangleF(
			Width  - MinimapWidth  - MinimapMargin,
			Height - MinimapHeight - MinimapMargin,
			MinimapWidth,
			MinimapHeight);

	/// <summary>
	/// Pans the main canvas so that the graph position that corresponds to
	/// <paramref name="mmPoint"/> in the minimap is centred in the viewport.
	/// </summary>
	private void PanFromMinimap(PointF mmPoint)
	{
		var mm = GetMinimapRect();

		var graphBounds = GetGraphBounds();
		float viewW  = Math.Max(1, Width);
		float viewH  = Math.Max(1, Height);
		float vpMinX = (_offset.X > 0 ? 0 : -_offset.X / _zoom);
		float vpMinY = (_offset.Y > 0 ? 0 : -_offset.Y / _zoom);
		float vpMaxX = vpMinX + viewW / _zoom;
		float vpMaxY = vpMinY + viewH / _zoom;

		float worldMinX = Math.Min(graphBounds.Left,  vpMinX);
		float worldMinY = Math.Min(graphBounds.Top,   vpMinY);
		float worldMaxX = Math.Max(graphBounds.Right, vpMaxX);
		float worldMaxY = Math.Max(graphBounds.Bottom, vpMaxY);
		float worldW = Math.Max(1f, worldMaxX - worldMinX);
		float worldH = Math.Max(1f, worldMaxY - worldMinY);

		float innerW = mm.Width  - MinimapPadding * 2f;
		float innerH = mm.Height - MinimapPadding * 2f;
		float scaleX = innerW / worldW;
		float scaleY = innerH / worldH;
		float scale  = Math.Min(scaleX, scaleY);

		float scaledW = worldW * scale;
		float scaledH = worldH * scale;
		float originX = mm.X + MinimapPadding + (innerW - scaledW) / 2f;
		float originY = mm.Y + MinimapPadding + (innerH - scaledH) / 2f;

		// Convert minimap pixel back to graph space
		float gx = (mmPoint.X - originX) / scale + worldMinX;
		float gy = (mmPoint.Y - originY) / scale + worldMinY;

		// Pan so that graph point (gx, gy) lands at the viewport centre
		_offset = new PointF(viewW / 2f - gx * _zoom, viewH / 2f - gy * _zoom);
		Invalidate();
	}

	/// <summary>Draws the minimap overlay into <paramref name="g"/> (view-space coordinates).</summary>
	private void DrawMinimap(Graphics g)
	{
		if (!ShowMinimap || _graph == null) return;

		var mm = GetMinimapRect();

		// ── Background ────────────────────────────────────────────────────────────
		g.FillRectangle(s_minimapBgColor, mm);
		using (var border = new Pen(s_minimapBorderColor, 1f))
			g.DrawRectangle(border, mm);

		// ── Compute the graph-space region that the minimap must fit ──────────────
		var graphBounds = GetGraphBounds();

		// Expand slightly so the viewport rect can extend beyond nodes
		float viewW  = Math.Max(1, Width);
		float viewH  = Math.Max(1, Height);
		float vpMinX = (_offset.X > 0 ? 0 : -_offset.X / _zoom);
		float vpMinY = (_offset.Y > 0 ? 0 : -_offset.Y / _zoom);
		float vpMaxX = vpMinX + viewW / _zoom;
		float vpMaxY = vpMinY + viewH / _zoom;

		float worldMinX = Math.Min(graphBounds.Left,  vpMinX);
		float worldMinY = Math.Min(graphBounds.Top,   vpMinY);
		float worldMaxX = Math.Max(graphBounds.Right, vpMaxX);
		float worldMaxY = Math.Max(graphBounds.Bottom, vpMaxY);

		float worldW = Math.Max(1f, worldMaxX - worldMinX);
		float worldH = Math.Max(1f, worldMaxY - worldMinY);

		// Inner drawable area (inside the frame + padding)
		float innerX = mm.X + MinimapPadding;
		float innerY = mm.Y + MinimapPadding;
		float innerW = mm.Width  - MinimapPadding * 2f;
		float innerH = mm.Height - MinimapPadding * 2f;

		// Uniform scale so the world fits inside the inner area
		float scaleX = innerW / worldW;
		float scaleY = innerH / worldH;
		float scale  = Math.Min(scaleX, scaleY);

		// Offset so the scaled world is centred inside the inner area
		float scaledW = worldW * scale;
		float scaledH = worldH * scale;
		float originX = innerX + (innerW - scaledW) / 2f;
		float originY = innerY + (innerH - scaledH) / 2f;

		// Helper: graph-space point → minimap pixel
		PointF ToMM(float gx, float gy) =>
			new PointF(originX + (gx - worldMinX) * scale,
			           originY + (gy - worldMinY) * scale);

		// ── Draw group boxes (behind connections and nodes) ────────────────────────
		foreach (var box in _graph.GroupBoxes)
		{
			var tl = ToMM(box.Bounds.Left, box.Bounds.Top);
			var br = ToMM(box.Bounds.Right, box.Bounds.Bottom);
			float bw = Math.Max(1f, br.X - tl.X);
			float bh = Math.Max(1f, br.Y - tl.Y);
			var col = box.Color;
			g.FillRectangle(Color.FromArgb(col.Rb, col.Gb, col.Bb, 50), tl.X, tl.Y, bw, bh);
			using (var boxPen = new Pen(Color.FromArgb(col.Rb, col.Gb, col.Bb, 180), 1f))
				g.DrawRectangle(boxPen, tl.X, tl.Y, bw, bh);
		}

		// ── Draw connections ──────────────────────────────────────────────────────
		using (var connPen = new Pen(s_minimapConnColor, 1f))
		{
			foreach (var conn in _graph.Connections)
			{
				if (!conn.Source.IsPinned || !conn.Target.IsPinned) continue;
				var src = GetSocketCenter(conn.Source);
				var tgt = GetSocketCenter(conn.Target);
				g.DrawLine(connPen, ToMM(src.X, src.Y), ToMM(tgt.X, tgt.Y));
			}
		}

		// ── Draw nodes ────────────────────────────────────────────────────────────
		foreach (var node in _graph.Nodes)
		{
			var tl = ToMM(node.Position.X, node.Position.Y);
			var br = ToMM(node.Position.X + node.ComputedWidth,
			              node.Position.Y + node.ComputedHeight);
			float nw = Math.Max(1f, br.X - tl.X);
			float nh = Math.Max(1f, br.Y - tl.Y);

			var headerColor = (node.HeaderColor.A < 0.01f) ? s_defaultHeaderColor : node.HeaderColor;
			g.FillRectangle(headerColor, tl.X, tl.Y, nw, nh);
		}

		// ── Draw viewport rectangle ───────────────────────────────────────────────
		var vpTL = ToMM(vpMinX, vpMinY);
		var vpBR = ToMM(vpMaxX, vpMaxY);
		float vpW = Math.Max(1f, vpBR.X - vpTL.X);
		float vpH = Math.Max(1f, vpBR.Y - vpTL.Y);

		g.FillRectangle(s_minimapViewportColor, vpTL.X, vpTL.Y, vpW, vpH);
		using (var vpPen = new Pen(s_minimapViewBorder, 1.5f))
			g.DrawRectangle(vpPen, vpTL.X, vpTL.Y, vpW, vpH);
	}

	// ── Mouse events ─────────────────────────────────────────────────────────────

	/// <inheritdoc/>
	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		Focus();

		// ── Minimap click: pan the main canvas ────────────────────────────────────
		if (e.Buttons == MouseButtons.Primary && ShowMinimap &&
		    _graph != null && _graph.Nodes.Count > 0)
		{
			var mm = GetMinimapRect();
			if (mm.Contains(e.Location))
			{
				PanFromMinimap(e.Location);
				_minimapDragging = true;
				e.Handled = true;
				return;
			}
		}

		// ── Panning ──────────────────────────────────────────────────────────────
		bool altDown = e.Modifiers.HasFlag(Keys.Alt);
		if (e.Buttons == MouseButtons.Middle ||
		    (e.Buttons == MouseButtons.Primary && (_spaceDown || altDown)))
		{
			_isPanning = true;
			_panStart  = e.Location;
			e.Handled  = true;
			return;
		}

		if (e.Buttons == MouseButtons.Primary)
		{
			bool ctrlDown = e.Modifiers.HasFlag(Keys.Control);

			// ── Socket interaction ────────────────────────────────────────────────
			var socket = HitTestSocket(e.Location);
			if (socket != null)
			{
				ClearBoxSelection();
				if (socket.Direction == NodeSocketDirection.Output)
				{
					_connectingFrom   = socket;
					_connectingToView = e.Location;
				}
				else
				{
					if (socket.IsConnected)
					{
						var existing = socket.Connections[0];
						_connectingFrom   = existing.Source;
						_connectingToView = e.Location;
						_graph.RemoveConnection(existing);
						ConnectionDeleted?.Invoke(this, new NodeConnectionEventArgs(existing));
					}
					else
					{
						_connectingFrom   = socket;
						_connectingToView = e.Location;
					}
				}
				Invalidate();
				e.Handled = true;
				return;
			}

			// ── Ctrl+click on a connection: insert a waypoint ─────────────────────
			if (ctrlDown)
			{
				var connForPin = HitTestConnection(e.Location);
				if (connForPin != null)
				{
					var gp = ViewToGraph(e.Location);
					int insertIdx = FindWaypointInsertIndex(connForPin, gp);
					connForPin.WayPoints.Insert(insertIdx, gp);
					Invalidate();
					e.Handled = true;
					return;
				}
			}

			// ── Wire-pin (waypoint) drag ──────────────────────────────────────────
			var (pinConn, pinIdx) = HitTestWaypoint(e.Location);
			if (pinConn != null)
			{
				_draggingPin      = true;
				_dragPinConn      = pinConn;
				_dragPinIdx       = pinIdx;
				_dragPinMouseStart = e.Location;
				_dragPinStart     = pinConn.WayPoints[pinIdx];
				Invalidate();
				e.Handled = true;
				return;
			}

			// ── Node interaction ──────────────────────────────────────────────────
			var node = HitTestNode(e.Location);
			if (node != null)
			{
				ClearBoxSelection();

				// Selection
				if (!e.Modifiers.HasFlag(Keys.Shift) && !_selection.Contains(node))
					_selection.Clear();
				if (!_selection.Contains(node))
					_selection.Add(node);
				SelectionChanged?.Invoke(this, new NodeItemEventArgs(node));

				// Begin drag
				_dragNode           = node;
				_dragMouseStart     = e.Location;
				_dragStartPositions = _selection.ToDictionary(n => n, n => n.Position);

				// Record which boxes fully contained each dragged node at drag start
				_dragNodeBoxMemberships = new Dictionary<NodeItem, List<NodeGroupBox>>();
				if (_graph != null)
				{
					foreach (var n in _selection)
					{
						_dragNodeBoxMemberships[n] = _graph.GroupBoxes
							.Where(b => RectContainsRect(b.Bounds, n.Bounds))
							.ToList();
					}
				}

				Invalidate();
				e.Handled = true;
				return;
			}

			// ── Group-box header drag ─────────────────────────────────────────────
			if (!ctrlDown)
			{
				var boxHeader = HitTestGroupBoxHeader(e.Location);
				if (boxHeader != null)
				{
					_selectedBox       = boxHeader;
					_dragBox           = boxHeader;
					_dragBoxMouseStart = e.Location;
					_dragBoxOrigBounds = boxHeader.Bounds;
					_dragBoxNodeStarts = GetFullyContainedNodes(boxHeader)
					                     .Select(n => (n, n.Position))
					                     .ToList();
					_selection.Clear();
					SelectionChanged?.Invoke(this, new NodeItemEventArgs(null));
					Invalidate();
					e.Handled = true;
					return;
				}
			}

			// ── Ctrl+drag on empty space: rubber-band new group box ───────────────
			if (ctrlDown)
			{
				_drawingBox     = true;
				_drawBoxAnchor  = ViewToGraph(e.Location);
				_drawBoxCurrent = _drawBoxAnchor;
				Invalidate();
				e.Handled = true;
				return;
			}

			// ── Deselect ─────────────────────────────────────────────────────────
			ClearBoxSelection();
			_selection.Clear();
			SelectionChanged?.Invoke(this, new NodeItemEventArgs(null));
			Invalidate();
		}

		// ── Right-click: delete waypoint or hovered connection ────────────────────
		if (e.Buttons == MouseButtons.Alternate && _graph != null)
		{
			// Waypoint removal takes priority over connection deletion
			var (pinConn, pinIdx) = HitTestWaypoint(e.Location);
			if (pinConn != null)
			{
				pinConn.WayPoints.RemoveAt(pinIdx);
				Invalidate();
				e.Handled = true;
				return;
			}

			var conn = _hoveredConnection ?? HitTestConnection(e.Location);
			if (conn != null)
			{
				_hoveredConnection = null;
				_graph.RemoveConnection(conn);
				ConnectionDeleted?.Invoke(this, new NodeConnectionEventArgs(conn));
				Invalidate();
				e.Handled = true;
			}
		}
	}

	/// <inheritdoc/>
	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);

		if (_minimapDragging)
		{
			PanFromMinimap(e.Location);
			return;
		}

		if (_isPanning)
		{
			var delta = e.Location - _panStart;
			_offset   = _offset + delta;
			_panStart = e.Location;
			Invalidate();
			return;
		}

		// ── Wire-pin drag ─────────────────────────────────────────────────────────
		if (_draggingPin)
		{
			var delta = e.Location - _dragPinMouseStart;
			float dx  = delta.X / _zoom;
			float dy  = delta.Y / _zoom;
			_dragPinConn.WayPoints[_dragPinIdx] = new PointF(_dragPinStart.X + dx, _dragPinStart.Y + dy);
			Invalidate();
			return;
		}

		// ── Rubber-band group-box drawing ─────────────────────────────────────────
		if (_drawingBox)
		{
			_drawBoxCurrent = ViewToGraph(e.Location);
			Invalidate();
			return;
		}

		if (_connectingFrom != null)
		{
			_connectingToView = e.Location;
			_hoveredSocket    = HitTestSocket(e.Location);
			Invalidate();
			return;
		}

		// ── Group-box drag ────────────────────────────────────────────────────────
		if (_dragBox != null)
		{
			var delta = e.Location - _dragBoxMouseStart;
			float dx  = delta.X / _zoom;
			float dy  = delta.Y / _zoom;
			var   ob  = _dragBoxOrigBounds;
			_dragBox.Bounds = new RectangleF(ob.X + dx, ob.Y + dy, ob.Width, ob.Height);

			// Move the nodes that were fully contained at drag start
			foreach (var (n, startPos) in _dragBoxNodeStarts)
				n.Position = new PointF(startPos.X + dx, startPos.Y + dy);

			Invalidate();
			return;
		}

		if (_dragNode != null)
		{
			var delta = e.Location - _dragMouseStart;
			float dx  = delta.X / _zoom;
			float dy  = delta.Y / _zoom;
			foreach (var kvp in _dragStartPositions)
				kvp.Key.Position = new PointF(kvp.Value.X + dx, kvp.Value.Y + dy);
			Invalidate();
			return;
		}

		// Update hover highlights
		var prevSocket = _hoveredSocket;
		var prevConn   = _hoveredConnection;
		_hoveredSocket     = HitTestSocket(e.Location);
		_hoveredConnection = (_hoveredSocket == null) ? HitTestConnection(e.Location) : null;
		if (_hoveredSocket != prevSocket || _hoveredConnection != prevConn)
			Invalidate();
	}

	/// <inheritdoc/>
	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);

		if (_minimapDragging)
		{
			_minimapDragging = false;
			e.Handled = true;
			return;
		}

		_isPanning = false;

		// ── Wire-pin drag end ─────────────────────────────────────────────────────
		if (_draggingPin)
		{
			_draggingPin = false;
			_dragPinConn = null;
			e.Handled    = true;
			return;
		}

		// ── Rubber-band group-box creation end ────────────────────────────────────
		if (_drawingBox)
		{
			_drawingBox = false;
			var r = GetDrawingBoxRect();
			if (r.Width >= MinBoxCreateSize && r.Height >= MinBoxCreateSize && _graph != null)
			{
				// Ensure the box is always tall enough to show its header plus some body space
				float minH = BoxHeaderHeight + MinBoxBodyHeight;
				if (r.Height < minH)
					r = new RectangleF(r.X, r.Y, r.Width, minH);
				var box = new NodeGroupBox { Bounds = r };
				_graph.AddGroupBox(box);
				_selectedBox = box;
			}
			Invalidate();
			e.Handled = true;
			return;
		}

		// ── Group-box drag end ────────────────────────────────────────────────────
		if (_dragBox != null)
		{
			_dragBox           = null;
			_dragBoxNodeStarts = null;
			e.Handled          = true;
			return;
		}

		// ── Node drag end: apply box-membership logic ─────────────────────────────
		bool wasNodeDrag    = _dragNode != null;
		_dragNode           = null;
		_dragStartPositions = null;

		if (wasNodeDrag && _dragNodeBoxMemberships != null && _graph != null)
		{
			foreach (var kvp in _dragNodeBoxMemberships)
			{
				var node  = kvp.Key;
				var boxes = kvp.Value;
				foreach (var box in boxes)
				{
					if (!_graph.GroupBoxes.Contains(box)) continue;

					bool fullyInside   = RectContainsRect(box.Bounds, node.Bounds);
					bool partialOverlap = !fullyInside && RectIntersects(box.Bounds, node.Bounds);

					if (partialOverlap)
					{
						// Node is trying to make more room – expand the box to contain it
						box.Bounds = RectUnion(box.Bounds, node.Bounds);
					}
					// If fully outside: node escapes the box – no action needed
				}
			}
			_dragNodeBoxMemberships = null;
			Invalidate();
		}

		// ── Connection creation ───────────────────────────────────────────────────
		if (_connectingFrom != null)
		{
			var targetSocket = HitTestSocket(e.Location);
			if (targetSocket != null &&
			    targetSocket != _connectingFrom &&
			    targetSocket.Node != _connectingFrom.Node)
			{
				// Normalise direction: src = output, tgt = input
				NodeSocket src = _connectingFrom, tgt = targetSocket;
				if (src.Direction == NodeSocketDirection.Input && tgt.Direction == NodeSocketDirection.Output)
					(src, tgt) = (tgt, src);

				if (src.Direction == NodeSocketDirection.Output &&
				    tgt.Direction == NodeSocketDirection.Input  &&
				    src.SocketType.IsCompatibleWith(tgt.SocketType))
				{
					var conn = _graph?.Connect(src, tgt);
					if (conn != null)
						ConnectionCreated?.Invoke(this, new NodeConnectionEventArgs(conn));
				}
			}
			_connectingFrom = null;
			Invalidate();
		}
	}

	/// <inheritdoc/>
	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);
		if (e.Delta.Height == 0)
			return;

		ZoomAt(e.Location, (float)Math.Exp(e.Delta.Height * 0.35f));
		e.Handled = true;
	}

	/// <summary>Zooms around a view-space point while keeping that point stationary.</summary>
	public void ZoomAt(PointF viewPoint, float factor)
	{
		if (factor <= 0 || float.IsNaN(factor) || float.IsInfinity(factor))
			return;

		var graphPos = ViewToGraph(viewPoint);
		_zoom = Math.Max(MinZoom, Math.Min(MaxZoom, _zoom * factor));
		_offset = new PointF(
			viewPoint.X - graphPos.X * _zoom,
			viewPoint.Y - graphPos.Y * _zoom);
		Invalidate();
	}

	// ── Keyboard events ───────────────────────────────────────────────────────────

	/// <inheritdoc/>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);

		switch (e.Key)
		{
			case Keys.Space:
				_spaceDown = true;
				e.Handled  = true;
				break;

			case Keys.Escape:
				_connectingFrom = null;
				_drawingBox     = false;
				_draggingPin    = false;
				_dragPinConn    = null;
				Invalidate();
				e.Handled = true;
				break;

			case Keys.Delete:
			case Keys.Backspace:
				if (_graph != null)
				{
					foreach (var node in _selection.ToList())
						_graph.RemoveNode(node);
					_selection.Clear();

					if (_selectedBox != null)
					{
						_graph.RemoveGroupBox(_selectedBox);
						_selectedBox = null;
					}

					if (_hoveredConnection != null)
					{
						var conn = _hoveredConnection;
						_hoveredConnection = null;
						_graph.RemoveConnection(conn);
						ConnectionDeleted?.Invoke(this, new NodeConnectionEventArgs(conn));
					}
					Invalidate();
				}
				e.Handled = true;
				break;

			case Keys.A:
				if (e.Control && _graph != null)
				{
					_selection.Clear();
					_selection.AddRange(_graph.Nodes);
					SelectionChanged?.Invoke(this, new NodeItemEventArgs(null));
					Invalidate();
					e.Handled = true;
				}
				break;

			case Keys.B:
				if (e.Control)
				{
					AddBookmark();
					e.Handled = true;
				}
				break;

			case Keys.F:
				FrameAll();
				e.Handled = true;
				break;
		}
	}

	/// <inheritdoc/>
	protected override void OnKeyUp(KeyEventArgs e)
	{
		base.OnKeyUp(e);
		if (e.Key == Keys.Space)
			_spaceDown = false;
	}

	// ── Public utilities ──────────────────────────────────────────────────────────

	/// <summary>
	/// Adjusts the zoom and offset so that all nodes are visible in the current viewport.
	/// </summary>
	public void FrameAll()
	{
		if (_graph == null || _graph.Nodes.Count == 0) return;

		var bounds = GetGraphBounds();
		float margin = 40f;
		float gw = bounds.Width  + margin * 2;
		float gh = bounds.Height + margin * 2;

		float viewW = Math.Max(1, Width);
		float viewH = Math.Max(1, Height);

		_zoom = Math.Max(MinZoom, Math.Min(MaxZoom, Math.Min(viewW / gw, viewH / gh)));
		_offset = new PointF(
			viewW / 2f - (bounds.Left + bounds.Width  / 2f) * _zoom,
			viewH / 2f - (bounds.Top  + bounds.Height / 2f) * _zoom);
		Invalidate();
	}

	/// <summary>
	/// Arranges the graph nodes in left-to-right dependency columns without overlap.
	/// </summary>
	/// <param name="nodes">Nodes to arrange; defaults to every node in the graph.</param>
	/// <param name="columnSpacing">Horizontal gap between columns.</param>
	/// <param name="rowSpacing">Vertical gap between nodes.</param>
	public void AutoLayout(IEnumerable<NodeItem> nodes = null, float columnSpacing = 300f, float rowSpacing = 32f)
	{
		if (_graph == null) return;

		LayoutAllNodes();
		var selected = (nodes ?? _graph.Nodes).Where(node => node != null && _graph.Nodes.Contains(node)).Distinct().ToList();
		if (selected.Count == 0) return;

		var selectedSet = new HashSet<NodeItem>(selected);
		var depths = new Dictionary<NodeItem, int>();
		int GetDepth(NodeItem node, HashSet<NodeItem> visiting)
		{
			if (depths.TryGetValue(node, out var cached)) return cached;
			if (!visiting.Add(node)) return 0;
			int depth = 0;
			foreach (var connection in _graph.Connections.Where(connection =>
				connection.Target.Node == node && selectedSet.Contains(connection.Source.Node)))
				depth = Math.Max(depth, GetDepth(connection.Source.Node, visiting) + 1);
			visiting.Remove(node);
			return depths[node] = depth;
		}

		foreach (var node in selected) GetDepth(node, new HashSet<NodeItem>());
		foreach (var column in selected.GroupBy(node => depths[node]).OrderBy(column => column.Key))
		{
			float y = 60f;
			foreach (var node in column.OrderBy(node => node.Title, StringComparer.OrdinalIgnoreCase))
			{
				node.Position = new PointF(60f + column.Key * columnSpacing, y);
				y += node.ComputedHeight + rowSpacing;
			}
		}
		Invalidate();
	}

	/// <summary>
	/// Returns the bounding rectangle (in graph space) that encloses all nodes,
	/// or <see cref="RectangleF.Empty"/> when the graph is empty.
	/// </summary>
	private RectangleF GetGraphBounds()
	{
		if (_graph == null || _graph.Nodes.Count == 0) return RectangleF.Empty;

		float minX = float.MaxValue, minY = float.MaxValue;
		float maxX = float.MinValue, maxY = float.MinValue;
		foreach (var node in _graph.Nodes)
		{
			minX = Math.Min(minX, node.Position.X);
			minY = Math.Min(minY, node.Position.Y);
			maxX = Math.Max(maxX, node.Position.X + node.ComputedWidth);
			maxY = Math.Max(maxY, node.Position.Y + node.ComputedHeight);
		}
		return new RectangleF(minX, minY, maxX - minX, maxY - minY);
	}

	/// <summary>
	/// Selects <paramref name="node"/> and pans the viewport so the node is centred in the
	/// visible area.  The zoom level is not changed.
	/// </summary>
	/// <remarks>
	/// If the node is not part of the current graph this method is a no-op.
	/// Raises <see cref="SelectionChanged"/> after updating the selection.
	/// </remarks>
	public void SelectAndCenter(NodeItem node)
	{
		if (node == null || _graph == null || !_graph.Nodes.Contains(node)) return;

		_selection.Clear();
		_selection.Add(node);
		SelectionChanged?.Invoke(this, new NodeItemEventArgs(node));

		// Pan so the node centre lands on the viewport centre
		float cx = node.Position.X + node.ComputedWidth  / 2f;
		float cy = node.Position.Y + node.ComputedHeight / 2f;
		_offset = new PointF(Width / 2f - cx * _zoom, Height / 2f - cy * _zoom);
		Invalidate();
	}

	/// <summary>Selects <paramref name="node"/> without changing the camera.</summary>
	public void SelectNode(NodeItem node)
	{
		if (node == null || _graph == null || !_graph.Nodes.Contains(node)) return;
		_selection.Clear();
		_selection.Add(node);
		Invalidate();
	}

	// ── Cleanup ───────────────────────────────────────────────────────────────────

	/// <inheritdoc/>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_headerFont?.Dispose();
			_labelFont?.Dispose();
			UnsubscribeGraph();
		}
		base.Dispose(disposing);
	}
}

// ─────────────────────────────────────────────────────────────────────────────────
//  NodeListPanel
// ─────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A side-panel that lists every node in a <see cref="NodeGraphView"/>'s graph.
/// Clicking a node in the list selects it on the canvas and centres the viewport on
/// it, making it easy to navigate large graphs where nodes may be out of view.
/// </summary>
/// <remarks>
/// <para>
/// Place this panel to the left of a <see cref="NodeGraphView"/> using a
/// <see cref="Splitter"/>.  The list updates automatically when nodes are added or
/// removed and when the <see cref="NodeGraphView.Graph"/> property is replaced.
/// </para>
/// <para>
/// The selection is kept in sync in both directions:
/// selecting a node on the canvas highlights it in the list, and clicking a row in
/// the list selects and centres the node on the canvas.
/// </para>
/// </remarks>
public class NodeListPanel : Panel
{
	// ── Colours (match the NodeGraphView dark theme) ─────────────────────────────
	private static readonly Color s_bg     = Color.FromRgb(0x161622);
	private static readonly Color s_header = Color.FromRgb(0x7777AA);

	// ── Inner list item ──────────────────────────────────────────────────────────

	/// <summary>Wraps a <see cref="NodeItem"/> so it can be stored directly in the ListBox.</summary>
	private sealed class NodeListItem : IListItem
	{
		public NodeItem Node { get; }
		public string   Text { get => Node.Title; set { } }
		public string   Key  => null;

		public NodeListItem(NodeItem node) => Node = node;
	}

	// ── State ────────────────────────────────────────────────────────────────────
	private readonly NodeGraphView _view;
	private readonly ListBox       _listBox;
	private NodeGraph              _subscribedGraph;
	private bool                   _suppressListSelection;

	/// <summary>
	/// Initializes a new <see cref="NodeListPanel"/> bound to <paramref name="view"/>.
	/// </summary>
	/// <param name="view">The <see cref="NodeGraphView"/> to observe and control.</param>
	public NodeListPanel(NodeGraphView view)
	{
		_view = view ?? throw new ArgumentNullException(nameof(view));
		BackgroundColor = s_bg;
		MinimumSize     = new Size(120, 0);

		_listBox = new ListBox { BackgroundColor = s_bg };
		_listBox.SelectedIndexChanged += OnListSelectionChanged;

		var headerLabel = new Label
		{
			Text              = "NODES",
			Font              = new Font(SystemFont.Default, 9f),
			TextColor         = s_header,
			VerticalAlignment = VerticalAlignment.Center,
		};

		var layout = new DynamicLayout
		{
			Padding         = new Padding(6, 6),
			DefaultSpacing  = new Size(2, 4),
			BackgroundColor = s_bg,
		};
		layout.Add(headerLabel);
		layout.Add(_listBox, yscale: true);

		Content = layout;

		_view.GraphChanged     += OnViewGraphChanged;
		_view.SelectionChanged += OnViewSelectionChanged;

		RefreshGraph();
	}

	// ── Private helpers ──────────────────────────────────────────────────────────

	private void RefreshGraph()
	{
		if (_subscribedGraph != null)
		{
			_subscribedGraph.NodeAdded   -= OnNodeAddedOrRemoved;
			_subscribedGraph.NodeRemoved -= OnNodeAddedOrRemoved;
		}

		_subscribedGraph = _view.Graph;

		if (_subscribedGraph != null)
		{
			_subscribedGraph.NodeAdded   += OnNodeAddedOrRemoved;
			_subscribedGraph.NodeRemoved += OnNodeAddedOrRemoved;
		}

		PopulateList();
	}

	private void PopulateList()
	{
		_suppressListSelection = true;
		try
		{
			_listBox.Items.Clear();
			if (_view.Graph != null)
				foreach (var node in _view.Graph.Nodes)
					_listBox.Items.Add(new NodeListItem(node));

			// Highlight the currently selected canvas node (if any)
			SyncSelectionCore();
		}
		finally { _suppressListSelection = false; }
	}

	/// <summary>
	/// Finds the list row whose wrapped <see cref="NodeItem"/> matches the canvas selection
	/// and sets the list selection accordingly.
	/// Uses reference equality so it is unaffected by changes in collection order.
	/// </summary>
	private void SyncSelectionCore()
	{
		var selected = _view.SelectedNodes.FirstOrDefault();
		int idx = -1;
		if (selected != null)
		{
			for (int i = 0; i < _listBox.Items.Count; i++)
			{
				if (_listBox.Items[i] is NodeListItem nli && ReferenceEquals(nli.Node, selected))
				{
					idx = i;
					break;
				}
			}
		}
		_listBox.SelectedIndex = idx;
	}

	private void SyncSelection()
	{
		if (_suppressListSelection) return;
		_suppressListSelection = true;
		try   { SyncSelectionCore(); }
		finally { _suppressListSelection = false; }
	}

	// ── Event handlers ───────────────────────────────────────────────────────────

	private void OnViewGraphChanged(object sender, EventArgs e)            => RefreshGraph();
	private void OnNodeAddedOrRemoved(object sender, NodeItemEventArgs e)  => PopulateList();
	private void OnViewSelectionChanged(object sender, NodeItemEventArgs e) => SyncSelection();

	private void OnListSelectionChanged(object sender, EventArgs e)
	{
		if (_suppressListSelection) return;

		int idx = _listBox.SelectedIndex;
		if (idx < 0 || idx >= _listBox.Items.Count) return;

		if (_listBox.Items[idx] is NodeListItem nli)
			_view.SelectAndCenter(nli.Node);
	}

	/// <inheritdoc/>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_view.GraphChanged     -= OnViewGraphChanged;
			_view.SelectionChanged -= OnViewSelectionChanged;

			if (_subscribedGraph != null)
			{
				_subscribedGraph.NodeAdded   -= OnNodeAddedOrRemoved;
				_subscribedGraph.NodeRemoved -= OnNodeAddedOrRemoved;
			}
		}
		base.Dispose(disposing);
	}
}

// ─────────────────────────────────────────────────────────────────────────────────
//  BookmarkPanel
// ─────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A side-panel that lists every <see cref="GraphBookmark"/> stored in the current
/// graph and lets the user navigate to, add, and delete bookmarks.
/// </summary>
/// <remarks>
/// <para>
/// Clicking a bookmark row instantly restores the camera position and zoom stored in
/// that bookmark (calls <see cref="NodeGraphView.JumpToBookmark"/>).
/// </para>
/// <para>
/// The <b>+</b> button creates a new bookmark at the current camera position.
/// Pressing <b>Delete / Backspace</b> while a row is focused removes that bookmark.
/// </para>
/// <para>
/// The list refreshes automatically whenever the graph's
/// <see cref="NodeGraph.BookmarkAdded"/> or <see cref="NodeGraph.BookmarkRemoved"/>
/// events fire, or when <see cref="NodeGraphView.Graph"/> is replaced.
/// </para>
/// </remarks>
public class BookmarkPanel : Panel
{
	// ── Colours (match the dark theme used by NodeGraphView) ─────────────────────
	private static readonly Color s_bg     = Color.FromRgb(0x161622);
	private static readonly Color s_header = Color.FromRgb(0x7777AA);

	// ── Inner list item ──────────────────────────────────────────────────────────
	private sealed class BookmarkListItem : IListItem
	{
		public GraphBookmark Bookmark { get; }
		public string Text { get => Bookmark.Name; set => Bookmark.Name = value; }
		public string Key  => null;
		public BookmarkListItem(GraphBookmark bm) => Bookmark = bm;
	}

	// ── State ────────────────────────────────────────────────────────────────────
	private readonly NodeGraphView _view;
	private readonly ListBox       _listBox;
	private NodeGraph              _subscribedGraph;

	/// <summary>
	/// Initializes a new <see cref="BookmarkPanel"/> bound to <paramref name="view"/>.
	/// </summary>
	/// <param name="view">The <see cref="NodeGraphView"/> to observe and control.</param>
	public BookmarkPanel(NodeGraphView view)
	{
		_view = view ?? throw new ArgumentNullException(nameof(view));
		BackgroundColor = s_bg;
		MinimumSize     = new Size(120, 0);

		_listBox = new ListBox { BackgroundColor = s_bg };
		_listBox.SelectedIndexChanged += OnListSelectionChanged;
		_listBox.KeyDown              += OnListKeyDown;

		var headerLabel = new Label
		{
			Text              = "BOOKMARKS",
			Font              = new Font(SystemFont.Default, 9f),
			TextColor         = s_header,
			VerticalAlignment = VerticalAlignment.Center,
		};

		var addBtn = new Button { Text = "+", ToolTip = "Add bookmark at current camera position (Ctrl+B)" };
		addBtn.Click += (_, _) => _view.AddBookmark();

		var headerRow = new TableLayout
		{
			Rows =
			{
				new TableRow(new TableCell(headerLabel, scaleWidth: true), new TableCell(addBtn)),
			},
		};

		var layout = new DynamicLayout
		{
			Padding         = new Padding(6, 6),
			DefaultSpacing  = new Size(2, 4),
			BackgroundColor = s_bg,
		};
		layout.Add(headerRow);
		layout.Add(_listBox, yscale: true);

		Content = layout;

		_view.GraphChanged += OnViewGraphChanged;
		RefreshGraph();
	}

	// ── Private helpers ──────────────────────────────────────────────────────────

	private void RefreshGraph()
	{
		if (_subscribedGraph != null)
		{
			_subscribedGraph.BookmarkAdded   -= OnBookmarkChanged;
			_subscribedGraph.BookmarkRemoved -= OnBookmarkChanged;
		}

		_subscribedGraph = _view.Graph;

		if (_subscribedGraph != null)
		{
			_subscribedGraph.BookmarkAdded   += OnBookmarkChanged;
			_subscribedGraph.BookmarkRemoved += OnBookmarkChanged;
		}

		PopulateList();
	}

	private void PopulateList()
	{
		_listBox.Items.Clear();
		if (_view.Graph != null)
			foreach (var bm in _view.Graph.Bookmarks)
				_listBox.Items.Add(new BookmarkListItem(bm));
	}

	// ── Event handlers ───────────────────────────────────────────────────────────

	private void OnViewGraphChanged(object sender, EventArgs e)                     => RefreshGraph();
	private void OnBookmarkChanged(object sender, GraphBookmarkEventArgs e)          => PopulateList();

	private void OnListSelectionChanged(object sender, EventArgs e)
	{
		int idx = _listBox.SelectedIndex;
		if (idx < 0 || idx >= _listBox.Items.Count) return;
		if (_listBox.Items[idx] is BookmarkListItem bli)
			_view.JumpToBookmark(bli.Bookmark);
	}

	private void OnListKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Keys.Delete && e.Key != Keys.Backspace) return;

		int idx = _listBox.SelectedIndex;
		if (idx < 0 || idx >= _listBox.Items.Count) return;
		if (_listBox.Items[idx] is BookmarkListItem bli && _view.Graph != null)
		{
			_view.Graph.RemoveBookmark(bli.Bookmark);
			e.Handled = true;
		}
	}

	/// <inheritdoc/>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_view.GraphChanged -= OnViewGraphChanged;
			if (_subscribedGraph != null)
			{
				_subscribedGraph.BookmarkAdded   -= OnBookmarkChanged;
				_subscribedGraph.BookmarkRemoved -= OnBookmarkChanged;
			}
		}
		base.Dispose(disposing);
	}
}
