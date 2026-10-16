namespace Eto.Test.Sections.Controls
{
	/// <summary>
	/// Interactive demonstration of the <see cref="NodeGraphView"/> control including a
	/// properties panel that lists all sockets for the selected node and lets the user
	/// pin or unpin them from the canvas view.
	/// </summary>
	/// <remarks>
	/// Mouse controls:
	///   Left-drag node header  – move node(s)
	///   Left-drag output socket – begin connection (data or control-flow)
	///   Left-drag connected input – re-route connection
	///   Right-click connection – delete it
	///   Middle-drag / Alt+Left-drag / Space+Left-drag – pan canvas
	///   Mouse-wheel – zoom in / out (centred on cursor)
	///   Shift+click – add node to selection
	///   Delete / Backspace – delete selected nodes (or hovered connection)
	///   Ctrl+A – select all nodes
	///   F – frame all nodes
	/// </remarks>
	[Section("Controls", "Node Graph View")]
	public class NodeGraphViewSection : Panel
	{
		NodeGraphView      _graphView;
		NodeGraph          _graph;
		bool               _framed;
		NodePropertyPanel  _propertyPanel;
		NodeListPanel      _nodeListPanel;

		public NodeGraphViewSection()
		{
			_graphView     = new NodeGraphView();
			_graph         = CreateExampleGraph();
			_propertyPanel = new NodePropertyPanel(_graphView);
			_nodeListPanel = new NodeListPanel(_graphView);

			_graphView.Graph = _graph;

			// Auto-frame once the control has been sized
			_graphView.SizeChanged += (s, e) =>
			{
				if (!_framed && _graphView.Width > 0 && _graphView.Height > 0)
				{
					_framed = true;
					_graphView.FrameAll();
				}
			};

			_graphView.SelectionChanged  += OnSelectionChanged;
			_graphView.ConnectionCreated += (s, e) => Log.Write(null, $"Connection created: {e.Connection.Source.Name} → {e.Connection.Target.Name}");
			_graphView.ConnectionDeleted += (s, e) => Log.Write(null, $"Connection deleted: {e.Connection.Source.Name} → {e.Connection.Target.Name}");

			// [ NodeList | [ PropertyPanel | GraphView ] ]
			var innerSplitter = new Splitter
			{
				Orientation = Orientation.Horizontal,
				Panel1      = _propertyPanel,
				Panel2      = _graphView,
				Position    = 240,
			};

			var outerSplitter = new Splitter
			{
				Orientation = Orientation.Horizontal,
				Panel1      = _nodeListPanel,
				Panel2      = innerSplitter,
				Position    = 150,
			};

			var layout = new DynamicLayout { DefaultSpacing = new Size(0, 0) };
			layout.Add(BuildToolbar());
			layout.Add(BuildHelpBar());
			layout.Add(outerSplitter, yscale: true);

			Content = layout;
		}

		// ── Toolbar ─────────────────────────────────────────────────────────────────

		Control BuildToolbar()
		{
			var frameAll = new Button { Text = "Frame All (F)" };
			frameAll.Click += (s, e) => _graphView.FrameAll();

			var resetExample = new Button { Text = "Reset Example" };
			resetExample.Click += (s, e) =>
			{
				_graph            = CreateExampleGraph();
				_graphView.Graph  = _graph;
				_framed           = false;
				_propertyPanel.Clear();
				_graphView.FrameAll();
			};

			var clearAll = new Button { Text = "Clear Graph" };
			clearAll.Click += (s, e) =>
			{
				_graph            = new NodeGraph();
				_graphView.Graph  = _graph;
				_propertyPanel.Clear();
			};

			var separator = new Label { Text = " | ", VerticalAlignment = VerticalAlignment.Center };
			var sep2      = new Label { Text = " | ", VerticalAlignment = VerticalAlignment.Center };

			// "Add node" buttons for each node type
			var addShapeBtn  = AddNodeButton("+ Shape Layer", () => MakeShapeLayerNode("Shape Layer"));
			var addSimBtn    = AddNodeButton("+ Simulation",  () => MakeSimulationNode());
			var addPatternBtn = AddNodeButton("+ Pattern",    () => MakePatternNode());
			var addMathBtn   = AddNodeButton("+ Math Add",   () => MakeMathNode("Add", "+"));
			var addBoxBtn    = AddNodeButton("+ Box",        () => MakeBoxNode());
			var addUnionBtn  = AddNodeButton("+ Union",      () => MakeBooleanNode("Union"));
			var addDiffBtn   = AddNodeButton("+ Difference", () => MakeBooleanNode("Difference"));
			var addOutputBtn = AddNodeButton("+ Output",     () => MakeOutputNode());

			return new StackLayout
			{
				Orientation = Orientation.Horizontal,
				Padding     = new Padding(4, 2),
				Spacing     = 3,
				Items       =
				{
					addShapeBtn, addSimBtn, addPatternBtn,
					new StackLayoutItem(separator),
					addMathBtn, addBoxBtn, addUnionBtn, addDiffBtn, addOutputBtn,
					new StackLayoutItem(sep2),
					frameAll, resetExample, clearAll,
				},
			};
		}

		Button AddNodeButton(string text, Func<NodeItem> factory)
		{
			var btn = new Button { Text = text };
			btn.Click += (s, e) =>
			{
				var node = factory();
				var centre = _graphView.ViewToGraph(
					new PointF(_graphView.Width / 2f, _graphView.Height / 2f));
				node.Position = new PointF(
					centre.X - NodeGraphView.DefaultNodeWidth / 2f,
					centre.Y - 40f);
				_graph.AddNode(node);
			};
			return btn;
		}

		Control BuildHelpBar()
		{
			return new Panel
			{
				Padding = new Padding(6, 2),
				Content = new Label
				{
					Text      = "Drag sockets to connect • Right-click connection to delete • Middle-drag / Alt+drag to pan • Scroll to zoom • Delete key removes selected nodes • ▷ = control-flow socket",
					TextColor = Color.FromRgb(0x888888),
					Font      = new Font(SystemFont.Default, 8f),
				},
			};
		}

		// ── Event handlers ───────────────────────────────────────────────────────────

		void OnSelectionChanged(object sender, NodeItemEventArgs e)
		{
			var node = e.Node ?? _graphView.SelectedNodes.FirstOrDefault();
			_propertyPanel.Populate(node);
			if (node != null)
				Log.Write(null, $"Selected: {node.Title}");
			else if (_graphView.SelectedNodes.Count == 0)
				Log.Write(null, "Selection cleared");
		}

		// ── Node factory methods ─────────────────────────────────────────────────────

		/// <summary>
		/// Creates a "Shape Layer" node that resembles a Variance entropy layer: many parameters
		/// but only the most important ones pinned to the canvas by default.
		/// </summary>
		static NodeItem MakeShapeLayerNode(string title)
		{
			var n = new NodeItem { Title = title, HeaderColor = Color.FromRgb(0x1D7ABA) };

			// Control-flow exec pin (always pinned)
			n.AddInput("▶ Exec",         NodeSocketType.ControlFlow);
			n.AddOutput("▷ Then",        NodeSocketType.ControlFlow);

			// Key geometry parameters — pinned by default
			n.AddInput("Width",          NodeSocketType.Float, "1.0");
			n.AddInput("CDU",            NodeSocketType.Float, "0.05");

			// Secondary parameters — hidden by default (accessible via Properties panel)
			var height   = n.AddInput("Height",       NodeSocketType.Float, "0.5");
			var lwr      = n.AddInput("LWR",          NodeSocketType.Float, "0.02");
			var overlayX = n.AddInput("Overlay X",    NodeSocketType.Float, "0.0");
			var overlayY = n.AddInput("Overlay Y",    NodeSocketType.Float, "0.0");
			var wobble   = n.AddInput("Wobble",       NodeSocketType.Float, "0.0");
			var innerCV  = n.AddInput("Inner CV",     NodeSocketType.Float, "0.0");
			height.IsPinned   = false;
			lwr.IsPinned      = false;
			overlayX.IsPinned = false;
			overlayY.IsPinned = false;
			wobble.IsPinned   = false;
			innerCV.IsPinned  = false;

			n.AddOutput("Geometry",      NodeSocketType.Geometry);
			return n;
		}

		/// <summary>
		/// Creates a "Simulation" node that accepts geometry from multiple shape layers
		/// and exposes a control-flow exec socket to enforce evaluation order.
		/// </summary>
		static NodeItem MakeSimulationNode()
		{
			var n = new NodeItem { Title = "Simulation", HeaderColor = Color.FromRgb(0xC0392B) };

			n.AddInput("▶ Exec",         NodeSocketType.ControlFlow);
			n.AddInput("Shape A",        NodeSocketType.Geometry);
			n.AddInput("Shape B",        NodeSocketType.Geometry);
			n.AddInput("N Runs",         NodeSocketType.Int, "1000");

			var seed = n.AddInput("Seed", NodeSocketType.Int, "0");
			seed.IsPinned = false;

			n.AddOutput("▷ Done",        NodeSocketType.ControlFlow);
			n.AddOutput("Results",       NodeSocketType.String);
			return n;
		}

		/// <summary>
		/// Creates a "Pattern" node that accepts pattern elements and a control-flow exec socket.
		/// Resembles a Quilt pattern root node.
		/// </summary>
		static NodeItem MakePatternNode()
		{
			var n = new NodeItem { Title = "Pattern", HeaderColor = Color.FromRgb(0x8E44AD) };

			n.AddInput("▶ Exec",         NodeSocketType.ControlFlow);
			n.AddInput("Element A",      NodeSocketType.Geometry);
			n.AddInput("Element B",      NodeSocketType.Geometry);

			var pitch  = n.AddInput("Pitch X",   NodeSocketType.Float, "10.0");
			var pitchY = n.AddInput("Pitch Y",   NodeSocketType.Float, "10.0");
			pitch.IsPinned  = false;
			pitchY.IsPinned = false;

			n.AddOutput("▷ Done",        NodeSocketType.ControlFlow);
			n.AddOutput("Layout",        NodeSocketType.Geometry);
			return n;
		}

		static NodeItem MakeMathNode(string title, string symbol)
		{
			var n = new NodeItem { Title = title, HeaderColor = Color.FromRgb(0x8B5CF6) };
			n.AddInput("A", NodeSocketType.Float);
			n.AddInput("B", NodeSocketType.Float);
			n.AddOutput($"A {symbol} B", NodeSocketType.Float);
			return n;
		}

		static NodeItem MakeBoxNode()
		{
			var n = new NodeItem { Title = "Box", HeaderColor = Color.FromRgb(0x10B981) };
			n.AddInput("Width",  NodeSocketType.Float, "2.0");
			n.AddInput("Height", NodeSocketType.Float, "1.0");
			var depth = n.AddInput("Depth",    NodeSocketType.Float, "1.0");
			var bevel = n.AddInput("Bevel",    NodeSocketType.Float, "0.0");
			var segs  = n.AddInput("Segments", NodeSocketType.Int,   "1");
			depth.IsPinned = false;
			bevel.IsPinned = false;
			segs.IsPinned  = false;
			n.AddOutput("Geometry", NodeSocketType.Geometry);
			return n;
		}

		static NodeItem MakeBooleanNode(string operation)
		{
			var n = new NodeItem { Title = operation, HeaderColor = Color.FromRgb(0xF59E0B) };
			n.AddInput("A", NodeSocketType.Geometry);
			n.AddInput("B", NodeSocketType.Geometry);
			n.AddOutput("Result", NodeSocketType.Geometry);
			return n;
		}

		static NodeItem MakeOutputNode()
		{
			var n = new NodeItem { Title = "Output", HeaderColor = Color.FromRgb(0xEF4444) };
			n.AddInput("Value",    NodeSocketType.Float);
			n.AddInput("Geometry", NodeSocketType.Geometry);
			return n;
		}

		// ── Example graph ────────────────────────────────────────────────────────────

		/// <summary>
		/// Creates an example graph modelling a two-layer Variance simulation scenario:
		/// two shape layers feed into a simulation node via both data (geometry) and
		/// control-flow (exec) connections.  Several parameters are intentionally hidden
		/// on the canvas to demonstrate the Properties panel.
		/// </summary>
		static NodeGraph CreateExampleGraph()
		{
			var g = new NodeGraph();

			// ── Shape Layer A ────────────────────────────────────────────────────────
			var layerA = g.AddNode(new NodeItem { Title = "Shape Layer A", Position = new PointF(40, 60),  HeaderColor = Color.FromRgb(0x1D7ABA) });
			layerA.AddInput("▶ Exec",      NodeSocketType.ControlFlow);
			layerA.AddOutput("▷ Then",     NodeSocketType.ControlFlow);
			layerA.AddInput("Width",       NodeSocketType.Float, "1.0");
			layerA.AddInput("CDU",         NodeSocketType.Float, "0.05");
			var laHeight = layerA.AddInput("Height",    NodeSocketType.Float, "0.5");
			var laLWR    = layerA.AddInput("LWR",       NodeSocketType.Float, "0.02");
			var laOvX    = layerA.AddInput("Overlay X", NodeSocketType.Float, "0.0");
			var laOvY    = layerA.AddInput("Overlay Y", NodeSocketType.Float, "0.0");
			laHeight.IsPinned = false;
			laLWR.IsPinned    = false;
			laOvX.IsPinned    = false;
			laOvY.IsPinned    = false;
			layerA.AddOutput("Geometry",   NodeSocketType.Geometry);

			// ── Shape Layer B ────────────────────────────────────────────────────────
			var layerB = g.AddNode(new NodeItem { Title = "Shape Layer B", Position = new PointF(40, 320), HeaderColor = Color.FromRgb(0x1D7ABA) });
			layerB.AddInput("▶ Exec",      NodeSocketType.ControlFlow);
			layerB.AddOutput("▷ Then",     NodeSocketType.ControlFlow);
			layerB.AddInput("Width",       NodeSocketType.Float, "2.0");
			layerB.AddInput("CDU",         NodeSocketType.Float, "0.03");
			var lbHeight = layerB.AddInput("Height",    NodeSocketType.Float, "0.5");
			var lbLWR    = layerB.AddInput("LWR",       NodeSocketType.Float, "0.01");
			var lbOvX    = layerB.AddInput("Overlay X", NodeSocketType.Float, "0.0");
			lbHeight.IsPinned = false;
			lbLWR.IsPinned    = false;
			lbOvX.IsPinned    = false;
			layerB.AddOutput("Geometry",   NodeSocketType.Geometry);

			// ── Simulation ───────────────────────────────────────────────────────────
			var sim = g.AddNode(new NodeItem { Title = "Simulation", Position = new PointF(380, 170), HeaderColor = Color.FromRgb(0xC0392B) });
			sim.AddInput("▶ Exec",         NodeSocketType.ControlFlow);
			sim.AddInput("Shape A",        NodeSocketType.Geometry);
			sim.AddInput("Shape B",        NodeSocketType.Geometry);
			sim.AddInput("N Runs",         NodeSocketType.Int, "1000");
			var simSeed = sim.AddInput("Seed", NodeSocketType.Int, "0");
			simSeed.IsPinned = false;
			sim.AddOutput("▷ Done",        NodeSocketType.ControlFlow);
			sim.AddOutput("Results",       NodeSocketType.String);

			// ── Pattern node ─────────────────────────────────────────────────────────
			var pattern = g.AddNode(new NodeItem { Title = "Pattern", Position = new PointF(660, 170), HeaderColor = Color.FromRgb(0x8E44AD) });
			pattern.AddInput("▶ Exec",     NodeSocketType.ControlFlow);
			pattern.AddInput("Layout",     NodeSocketType.Geometry);
			pattern.AddOutput("▷ Done",    NodeSocketType.ControlFlow);
			pattern.AddOutput("Output",    NodeSocketType.Geometry);

			// ── Data connections ─────────────────────────────────────────────────────
			g.Connect(layerA.Outputs[1], sim.Inputs[1]);   // Layer A Geometry → Shape A
			g.Connect(layerB.Outputs[1], sim.Inputs[2]);   // Layer B Geometry → Shape B
			g.Connect(sim.Outputs[1],    pattern.Inputs[1]); // Results → Layout

			// ── Control-flow connections ─────────────────────────────────────────────
			// Layer A exec → Simulation exec (enforce Layer A is processed first)
			g.Connect(layerA.Outputs[0], sim.Inputs[0]);   // Layer A ▷ Then → Simulation ▶ Exec
			// Simulation done → Pattern exec
			g.Connect(sim.Outputs[0],    pattern.Inputs[0]); // Simulation ▷ Done → Pattern ▶ Exec

			return g;
		}
	}

	// ── NodePropertyPanel ────────────────────────────────────────────────────────

	/// <summary>
	/// A collapsible side panel that lists all sockets belonging to the currently selected
	/// <see cref="NodeItem"/> and lets the user toggle their canvas visibility via pin checkboxes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each socket row shows:
	/// <list type="bullet">
	///   <item>A <see cref="CheckBox"/> — when checked the socket is pinned (visible on the canvas).</item>
	///   <item>A colored swatch matching the socket type's color.</item>
	///   <item>The socket name and, for unconnected inputs, its default value.</item>
	/// </list>
	/// </para>
	/// <para>
	/// Toggling the checkbox calls <see cref="NodeGraphView.InvalidateNode"/> so the canvas
	/// immediately reflects the new visibility state.
	/// </para>
	/// </remarks>
	internal class NodePropertyPanel : Panel
	{
		private readonly NodeGraphView _view;
		private static readonly Color  s_panelBg     = Color.FromRgb(0x1A1A2A);
		private static readonly Color  s_sectionColor = Color.FromRgb(0x555570);
		private static readonly Color  s_mutedColor   = Color.FromRgb(0x888899);
		private static readonly Color  s_valueColor   = Color.FromRgb(0xF5A623);

		public NodePropertyPanel(NodeGraphView view)
		{
			_view           = view;
			BackgroundColor = s_panelBg;
			ShowPlaceholder();
		}

		// ── Public API ───────────────────────────────────────────────────────────────

		/// <summary>Populates the panel for <paramref name="node"/>, or clears it when <c>null</c>.</summary>
		public void Populate(NodeItem node)
		{
			if (node == null)
			{
				ShowPlaceholder();
				return;
			}

			var scroll = new Scrollable
			{
				BackgroundColor = s_panelBg,
				Border          = BorderType.None,
			};

			var layout = new DynamicLayout
			{
				Padding        = new Padding(8, 6),
				DefaultSpacing = new Size(4, 2),
				BackgroundColor = s_panelBg,
			};

			// ── Header ───────────────────────────────────────────────────────────────
			layout.Add(new Label
			{
				Text     = node.Title,
				Font     = new Font(SystemFont.Bold, 10f),
				TextColor = Colors.White,
			});
			layout.Add(new Panel { Height = 4, BackgroundColor = s_panelBg }); // spacer

			// ── Input sockets ────────────────────────────────────────────────────────
			if (node.Inputs.Count > 0)
			{
				layout.Add(MakeSectionLabel("INPUTS"));
				foreach (var s in node.Inputs)
					layout.Add(MakeSocketRow(s, node));
				layout.Add(new Panel { Height = 6, BackgroundColor = s_panelBg });
			}

			// ── Output sockets ───────────────────────────────────────────────────────
			if (node.Outputs.Count > 0)
			{
				layout.Add(MakeSectionLabel("OUTPUTS"));
				foreach (var s in node.Outputs)
					layout.Add(MakeSocketRow(s, node));
			}

			layout.Add(null); // fill remaining vertical space
			scroll.Content = layout;
			Content        = scroll;
		}

		/// <summary>Resets the panel to its empty/placeholder state.</summary>
		public void Clear() => ShowPlaceholder();

		// ── Private helpers ──────────────────────────────────────────────────────────

		private void ShowPlaceholder()
		{
			Content = new Label
			{
				Text              = "Select a node\nto see its properties",
				TextColor         = s_mutedColor,
				VerticalAlignment = VerticalAlignment.Center,
				TextAlignment     = TextAlignment.Center,
				Wrap              = WrapMode.Word,
			};
		}

		private Label MakeSectionLabel(string text) =>
			new Label
			{
				Text      = text,
				Font      = new Font(SystemFont.Default, 7.5f),
				TextColor = s_sectionColor,
			};

		private Control MakeSocketRow(NodeSocket socket, NodeItem node)
		{
			// Pin toggle
			var pin = new CheckBox
			{
				Checked     = socket.IsPinned,
				ToolTip     = socket.IsPinned
					? "Unpin: hide this socket from the canvas"
					: "Pin: show this socket as a connector on the canvas",
			};
			pin.CheckedChanged += (_, _) =>
			{
				socket.IsPinned = pin.Checked == true;
				pin.ToolTip = socket.IsPinned
					? "Unpin: hide this socket from the canvas"
					: "Pin: show this socket as a connector on the canvas";
				_view.InvalidateNode(node);
			};

			// Type color swatch
			var swatch = new Panel
			{
				BackgroundColor = socket.SocketType.Color,
				Width           = 10,
				Height          = 10,
			};

			// Socket name label
			var nameLabel = new Label
			{
				Text      = socket.Name,
				TextColor = Colors.White,
			};

			// Optional value badge for unconnected inputs
			Label valueLabel = null;
			if (socket.Direction == NodeSocketDirection.Input &&
			    !socket.IsConnected &&
			    socket.Value != null)
			{
				valueLabel = new Label
				{
					Text      = socket.Value,
					TextColor = s_valueColor,
				};
			}

			var row = new StackLayout
			{
				Orientation              = Orientation.Horizontal,
				Spacing                  = 5,
				VerticalContentAlignment = VerticalAlignment.Center,
				Padding                  = new Padding(0, 1),
				Items                    = { pin, swatch, nameLabel },
			};

			if (valueLabel != null)
				row.Items.Add(valueLabel);

			return row;
		}
	}
}
