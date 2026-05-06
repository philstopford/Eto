namespace Eto.Test.Sections.Controls
{
	/// <summary>
	/// Interactive demonstration of the <see cref="NodeGraphView"/> control.
	/// </summary>
	/// <remarks>
	/// Mouse controls:
	///   Left-drag node header  – move node(s)
	///   Left-drag output socket – begin connection
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
		NodeGraphView _graphView;
		NodeGraph     _graph;
		bool          _framed;

		public NodeGraphViewSection()
		{
			_graphView = new NodeGraphView();
			_graph     = CreateExampleGraph();
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

			var layout = new DynamicLayout { DefaultSpacing = new Size(0, 0) };
			layout.Add(BuildToolbar());
			layout.Add(BuildHelpBar());
			layout.Add(_graphView, yscale: true);

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
				_graphView.FrameAll();
			};

			var clearAll = new Button { Text = "Clear Graph" };
			clearAll.Click += (s, e) =>
			{
				_graph            = new NodeGraph();
				_graphView.Graph  = _graph;
			};

			var separator = new Label { Text = " | ", VerticalAlignment = VerticalAlignment.Center };

			// "Add node" buttons for each node type
			var addFloatBtn  = AddNodeButton("+ Float",    () => MakeFloatNode());
			var addIntBtn    = AddNodeButton("+ Integer",  () => MakeIntNode());
			var addAddBtn    = AddNodeButton("+ Math Add", () => MakeMathNode("Add",  "+"));
			var addMulBtn    = AddNodeButton("+ Math Mul", () => MakeMathNode("Multiply", "×"));
			var addSphereBtn = AddNodeButton("+ Sphere",   () => MakeSphereNode());
			var addBoxBtn    = AddNodeButton("+ Box",      () => MakeBoxNode());
			var addUnionBtn  = AddNodeButton("+ Union",    () => MakeBooleanNode("Union"));
			var addDiffBtn   = AddNodeButton("+ Difference", () => MakeBooleanNode("Difference"));
			var addOutputBtn = AddNodeButton("+ Output",   () => MakeOutputNode());

			return new StackLayout
			{
				Orientation = Orientation.Horizontal,
				Padding     = new Padding(4, 2),
				Spacing     = 3,
				Items       =
				{
					addFloatBtn, addIntBtn, addAddBtn, addMulBtn,
					new StackLayoutItem(separator),
					addSphereBtn, addBoxBtn, addUnionBtn, addDiffBtn, addOutputBtn,
					new StackLayoutItem(separator),
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
				// Place new node in the centre of the current view
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
					Text      = "Drag sockets to connect • Right-click connection to delete • Middle-drag / Alt+drag to pan • Scroll to zoom • Delete key removes selected nodes",
					TextColor = Color.FromRgb(0x888888),
					Font      = new Font(SystemFont.Default, 8f),
				},
			};
		}

		// ── Event handlers ───────────────────────────────────────────────────────────

		void OnSelectionChanged(object sender, NodeItemEventArgs e)
		{
			if (e.Node != null)
				Log.Write(null, $"Selected: {e.Node.Title}");
			else if (_graphView.SelectedNodes.Count == 0)
				Log.Write(null, "Selection cleared");
		}

		// ── Node factory methods ─────────────────────────────────────────────────────

		static NodeItem MakeFloatNode()
		{
			var n = new NodeItem { Title = "Float", HeaderColor = Color.FromRgb(0x5C85D6) };
			n.AddInput("Value", NodeSocketType.Float, "0.0");
			n.AddOutput("Out",  NodeSocketType.Float);
			return n;
		}

		static NodeItem MakeIntNode()
		{
			var n = new NodeItem { Title = "Integer", HeaderColor = Color.FromRgb(0x2980B9) };
			n.AddInput("Value", NodeSocketType.Int, "0");
			n.AddOutput("Out",  NodeSocketType.Int);
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

		static NodeItem MakeSphereNode()
		{
			var n = new NodeItem { Title = "Sphere", HeaderColor = Color.FromRgb(0x10B981) };
			n.AddInput("Radius", NodeSocketType.Float, "1.0");
			n.AddOutput("Geometry", NodeSocketType.Geometry);
			return n;
		}

		static NodeItem MakeBoxNode()
		{
			var n = new NodeItem { Title = "Box", HeaderColor = Color.FromRgb(0x10B981) };
			n.AddInput("Width",  NodeSocketType.Float, "1.0");
			n.AddInput("Height", NodeSocketType.Float, "1.0");
			n.AddInput("Depth",  NodeSocketType.Float, "1.0");
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
		/// Creates a sample graph that demonstrates numeric and geometry node types.
		/// </summary>
		static NodeGraph CreateExampleGraph()
		{
			var g = new NodeGraph();

			// ── Numeric / math sub-graph ─────────────────────────────────────────────
			var numA = g.AddNode(new NodeItem { Title = "Float", Position = new PointF(40, 60), HeaderColor = Color.FromRgb(0x5C85D6) });
			numA.AddInput("Value",  NodeSocketType.Float, "1.5");
			numA.AddOutput("Out",   NodeSocketType.Float);

			var numB = g.AddNode(new NodeItem { Title = "Float", Position = new PointF(40, 180), HeaderColor = Color.FromRgb(0x5C85D6) });
			numB.AddInput("Value",  NodeSocketType.Float, "2.5");
			numB.AddOutput("Out",   NodeSocketType.Float);

			var addNode = g.AddNode(new NodeItem { Title = "Add", Position = new PointF(280, 110), HeaderColor = Color.FromRgb(0x8B5CF6) });
			addNode.AddInput("A",   NodeSocketType.Float);
			addNode.AddInput("B",   NodeSocketType.Float);
			addNode.AddOutput("A + B", NodeSocketType.Float);

			var numC = g.AddNode(new NodeItem { Title = "Float", Position = new PointF(40, 310), HeaderColor = Color.FromRgb(0x5C85D6) });
			numC.AddInput("Value",  NodeSocketType.Float, "3.0");
			numC.AddOutput("Out",   NodeSocketType.Float);

			var mulNode = g.AddNode(new NodeItem { Title = "Multiply", Position = new PointF(280, 240), HeaderColor = Color.FromRgb(0x8B5CF6) });
			mulNode.AddInput("A",   NodeSocketType.Float);
			mulNode.AddInput("B",   NodeSocketType.Float);
			mulNode.AddOutput("A × B", NodeSocketType.Float);

			// ── Geometry sub-graph ───────────────────────────────────────────────────
			var sphere = g.AddNode(new NodeItem { Title = "Sphere", Position = new PointF(40, 440), HeaderColor = Color.FromRgb(0x10B981) });
			sphere.AddInput("Radius",  NodeSocketType.Float, "1.0");
			sphere.AddOutput("Geometry", NodeSocketType.Geometry);

			var box = g.AddNode(new NodeItem { Title = "Box", Position = new PointF(40, 570), HeaderColor = Color.FromRgb(0x10B981) });
			box.AddInput("Width",  NodeSocketType.Float, "2.0");
			box.AddInput("Height", NodeSocketType.Float, "1.0");
			box.AddInput("Depth",  NodeSocketType.Float, "1.0");
			box.AddOutput("Geometry", NodeSocketType.Geometry);

			var unionNode = g.AddNode(new NodeItem { Title = "Union", Position = new PointF(290, 490), HeaderColor = Color.FromRgb(0xF59E0B) });
			unionNode.AddInput("A",  NodeSocketType.Geometry);
			unionNode.AddInput("B",  NodeSocketType.Geometry);
			unionNode.AddOutput("Result", NodeSocketType.Geometry);

			// ── Output node ──────────────────────────────────────────────────────────
			var output = g.AddNode(new NodeItem { Title = "Output", Position = new PointF(540, 280), HeaderColor = Color.FromRgb(0xEF4444) });
			output.AddInput("Value",    NodeSocketType.Float);
			output.AddInput("Geometry", NodeSocketType.Geometry);

			// ── Connections ──────────────────────────────────────────────────────────
			g.Connect(numA.Outputs[0],    addNode.Inputs[0]);
			g.Connect(numB.Outputs[0],    addNode.Inputs[1]);
			g.Connect(addNode.Outputs[0], mulNode.Inputs[0]);
			g.Connect(numC.Outputs[0],    mulNode.Inputs[1]);
			g.Connect(mulNode.Outputs[0], output.Inputs[0]);

			g.Connect(sphere.Outputs[0],   unionNode.Inputs[0]);
			g.Connect(box.Outputs[0],      unionNode.Inputs[1]);
			g.Connect(unionNode.Outputs[0], output.Inputs[1]);

			return g;
		}
	}
}
