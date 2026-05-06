namespace Eto.Forms;

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

	/// <summary>Raised after a connection is added.</summary>
	public event EventHandler<NodeConnectionEventArgs> ConnectionAdded;

	/// <summary>Raised after a connection is removed.</summary>
	public event EventHandler<NodeConnectionEventArgs> ConnectionRemoved;

	/// <summary>Raised after a node is added.</summary>
	public event EventHandler<NodeItemEventArgs> NodeAdded;

	/// <summary>Raised after a node is removed.</summary>
	public event EventHandler<NodeItemEventArgs> NodeRemoved;

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
///   <item>Right-click a connection to delete it; Delete/Backspace removes selected nodes.</item>
///   <item>Mouse-wheel to zoom; middle-mouse-drag (or Alt+left-drag) to pan.</item>
///   <item>Press F to frame all nodes; Ctrl+A to select all nodes.</item>
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
		}
	}

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

	/// <summary>Raised when the node selection changes.</summary>
	public event EventHandler<NodeItemEventArgs> SelectionChanged;

	/// <summary>Raised after a connection is created via user interaction.</summary>
	public event EventHandler<NodeConnectionEventArgs> ConnectionCreated;

	/// <summary>Raised after a connection is deleted via user interaction.</summary>
	public event EventHandler<NodeConnectionEventArgs> ConnectionDeleted;

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

		// NodeGraphView lives in the Eto core assembly, so EventLookup.HookupEvents
		// never fires for it (it returns early for types in the Eto assembly).
		// We must explicitly request the platform event plumbing for every On… method
		// we override; otherwise the Gtk handler never adds the required event masks
		// (PointerMotionMask, ButtonReleaseMask, ScrollMask, KeyPressMask, …) and the
		// control silently ignores all mouse and keyboard input on Linux/GTK.
		HandleEvent(Control.MouseDownEvent);
		HandleEvent(Control.MouseUpEvent);
		HandleEvent(Control.MouseMoveEvent);
		HandleEvent(Control.MouseWheelEvent);
		HandleEvent(Control.KeyDownEvent);
		HandleEvent(Control.KeyUpEvent);
	}

	// ── Graph subscription ──────────────────────────────────────────────────────

	private void SubscribeGraph()
	{
		if (_graph == null) return;
		_graph.ConnectionAdded   += OnGraphChanged;
		_graph.ConnectionRemoved += OnGraphChanged;
		_graph.NodeAdded         += OnGraphChanged;
		_graph.NodeRemoved       += OnGraphChanged;
	}

	private void UnsubscribeGraph()
	{
		if (_graph == null) return;
		_graph.ConnectionAdded   -= OnGraphChanged;
		_graph.ConnectionRemoved -= OnGraphChanged;
		_graph.NodeAdded         -= OnGraphChanged;
		_graph.NodeRemoved       -= OnGraphChanged;
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

	internal void LayoutNode(NodeItem node)
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

	private bool BezierContains(NodeConnection conn, PointF gp, float threshold)
	{
		var (c1, c2) = BezierControlPoints(
			GetSocketCenter(conn.Source),
			GetSocketCenter(conn.Target),
			srcIsOutput: true);
		var src = GetSocketCenter(conn.Source);
		var tgt = GetSocketCenter(conn.Target);
		float t2 = threshold * threshold;
		for (int i = 0; i <= 24; i++)
		{
			float t = i / 24f;
			var  pt = CubicBezierPoint(src, c1, c2, tgt, t);
			float dx = pt.X - gp.X, dy = pt.Y - gp.Y;
			if (dx * dx + dy * dy <= t2) return true;
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

		using (g.SaveTransformState())
		{
			g.TranslateTransform(_offset.X, _offset.Y);
			g.ScaleTransform(_zoom, _zoom);

			if (_graph != null)
			{
				foreach (var conn in _graph.Connections)
					DrawConnection(g, conn, conn == _hoveredConnection);

				foreach (var node in _graph.Nodes)
					DrawNode(g, node);
			}

			if (_connectingFrom != null)
				DrawPendingConnection(g);
		}
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

		if (IsControlFlowSocket(conn.Source) || IsControlFlowSocket(conn.Target))
		{
			// ControlFlow: light-grey, slightly wider, no glow
			using (var pen = new Pen(Color.FromRgb(0xDDDDDD), hovered ? 3f : 2.5f))
			{
				var (c1, c2) = BezierControlPoints(src, tgt, srcIsOutput: true);
				using (var path = new GraphicsPath())
				{
					path.AddBezier(src, c1, c2, tgt);
					g.DrawPath(pen, path);
				}
			}
		}
		else
		{
			var color = BlendColors(conn.Source.SocketType.Color, conn.Target.SocketType.Color);
			DrawBezier(g, src, tgt, srcIsOutput: true, color: color, width: hovered ? 3f : 2f, dashed: false);
		}
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

	// ── Mouse events ─────────────────────────────────────────────────────────────

	/// <inheritdoc/>
	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		Focus();

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
			// ── Socket interaction ────────────────────────────────────────────────
			var socket = HitTestSocket(e.Location);
			if (socket != null)
			{
				if (socket.Direction == NodeSocketDirection.Output)
				{
					// Start drawing a connection from the output
					_connectingFrom   = socket;
					_connectingToView = e.Location;
				}
				else
				{
					if (socket.IsConnected)
					{
						// Re-route: detach existing connection, keep dragging from its source
						var existing = socket.Connections[0];
						_connectingFrom   = existing.Source;
						_connectingToView = e.Location;
						_graph.RemoveConnection(existing);
						ConnectionDeleted?.Invoke(this, new NodeConnectionEventArgs(existing));
					}
					else
					{
						// Start from an unconnected input (reversed bezier)
						_connectingFrom   = socket;
						_connectingToView = e.Location;
					}
				}
				Invalidate();
				e.Handled = true;
				return;
			}

			// ── Node interaction ──────────────────────────────────────────────────
			var node = HitTestNode(e.Location);
			if (node != null)
			{
				// Selection
				if (!e.Modifiers.HasFlag(Keys.Shift) && !_selection.Contains(node))
					_selection.Clear();
				if (!_selection.Contains(node))
					_selection.Add(node);
				SelectionChanged?.Invoke(this, new NodeItemEventArgs(node));

				// Bring clicked node to front visually
				_graph.Nodes.Remove(node);
				_graph.Nodes.Add(node);

				// Begin drag
				_dragNode           = node;
				_dragMouseStart     = e.Location;
				_dragStartPositions = _selection.ToDictionary(n => n, n => n.Position);

				Invalidate();
				e.Handled = true;
				return;
			}

			// ── Deselect ─────────────────────────────────────────────────────────
			_selection.Clear();
			SelectionChanged?.Invoke(this, new NodeItemEventArgs(null));
			Invalidate();
		}

		// ── Right-click: delete hovered connection ────────────────────────────────
		if (e.Buttons == MouseButtons.Alternate && _graph != null)
		{
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

		if (_isPanning)
		{
			var delta = e.Location - _panStart;
			_offset   = _offset + delta;
			_panStart = e.Location;
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

		_isPanning          = false;
		_dragNode           = null;
		_dragStartPositions = null;

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
		// Zoom centered on the cursor position
		var graphPos = ViewToGraph(e.Location);
		float factor = 1f + e.Delta.Height * 0.1f;
		_zoom = Math.Max(MinZoom, Math.Min(MaxZoom, _zoom * factor));
		// Adjust offset so that graphPos stays under the cursor
		_offset = new PointF(
			e.Location.X - graphPos.X * _zoom,
			e.Location.Y - graphPos.Y * _zoom);
		Invalidate();
		e.Handled = true;
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

		float minX = float.MaxValue, minY = float.MaxValue;
		float maxX = float.MinValue, maxY = float.MinValue;
		foreach (var node in _graph.Nodes)
		{
			minX = Math.Min(minX, node.Position.X);
			minY = Math.Min(minY, node.Position.Y);
			maxX = Math.Max(maxX, node.Position.X + node.ComputedWidth);
			maxY = Math.Max(maxY, node.Position.Y + node.ComputedHeight);
		}

		float margin = 40f;
		float gw = maxX - minX + margin * 2;
		float gh = maxY - minY + margin * 2;

		float viewW = Math.Max(1, Width);
		float viewH = Math.Max(1, Height);

		_zoom = Math.Max(MinZoom, Math.Min(MaxZoom, Math.Min(viewW / gw, viewH / gh)));
		_offset = new PointF(
			viewW / 2f - ((minX + maxX) / 2f) * _zoom,
			viewH / 2f - ((minY + maxY) / 2f) * _zoom);
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
