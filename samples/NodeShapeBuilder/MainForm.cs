namespace NodeShapeBuilder;

/// <summary>
/// The main application window.
///
/// Layout:
/// ┌──────────────────────────────────────────────────────────┐
/// │  Toolbar                                                 │
/// ├────────────┬────────────────────────────┬────────────────┤
/// │  Properties│      NodeGraphView          │   Shape Preview│
/// │  Panel     │  (drag sockets to connect) │   (Drawable)   │
/// │  (200 px)  │                            │   (240 px)     │
/// └────────────┴────────────────────────────┴────────────────┘
///
/// Shape nodes available from toolbar: Rectangle, Circle, L-Shape, T-Shape.
/// Boolean nodes: Union, Intersection, Difference.
/// An Output node is the final result shown in the preview.
/// </summary>
public class MainForm : Form
{
	// ── Socket types ─────────────────────────────────────────────────────────────
	static readonly NodeSocketType s_geomType = new NodeSocketType("Geometry", Color.FromRgb(0x50E3C2));
	static readonly NodeSocketType s_numType  = new NodeSocketType("Number",   Color.FromRgb(0xF5A623));

	// ── UI controls ──────────────────────────────────────────────────────────────
	NodeGraphView    _graphView;
	NodeGraph        _graph;
	bool             _framed;
	ShapePreviewPanel _previewPanel;
	NodePropertyPanel _propPanel;
	Label            _statusLabel;

	public MainForm()
	{
		Title        = "NodeShapeBuilder – Shape & Boolean Node Graph Demo";
		ClientSize   = new Size(1200, 700);
		Resizable    = true;

		_graphView    = new NodeGraphView();
		_previewPanel = new ShapePreviewPanel
		{
			BackgroundColor = Color.FromRgb(0x1A1A2A),
		};
		_propPanel  = new NodePropertyPanel(_graphView);
		_statusLabel = new Label
		{
			Text      = "Ready",
			TextColor = Color.FromRgb(0x888899),
		};

		// Build initial example
		_graph           = CreateExampleGraph();
		_graphView.Graph = _graph;

		// Wire events
		_graphView.SizeChanged += (_, _) =>
		{
			if (!_framed && _graphView.Width > 0 && _graphView.Height > 0)
			{
				_framed = true;
				_graphView.FrameAll();
			}
		};
		_graphView.SelectionChanged  += OnSelectionChanged;
		_graphView.ConnectionCreated += (_, e) =>
		{
			SetStatus($"Connected: {e.Connection.Source.Name} → {e.Connection.Target.Name}");
			RefreshPreview();
		};
		_graphView.ConnectionDeleted += (_, e) =>
		{
			SetStatus($"Disconnected: {e.Connection.Source.Name} → {e.Connection.Target.Name}");
			RefreshPreview();
		};

		// ── Layout ───────────────────────────────────────────────────────────────

		var innerSplitter = new Splitter
		{
			Orientation = Orientation.Horizontal,
			Panel1      = _graphView,
			Panel2      = _previewPanel,
			Position    = -1,        // will be set after shown
			Panel2MinimumSize = 240,
		};

		var outerSplitter = new Splitter
		{
			Orientation = Orientation.Horizontal,
			Panel1      = _propPanel,
			Panel2      = innerSplitter,
			Position    = 200,
		};

		var mainLayout = new DynamicLayout { DefaultSpacing = new Size(0, 0) };
		mainLayout.Add(BuildToolbar());
		mainLayout.Add(outerSplitter, yscale: true);
		mainLayout.Add(BuildStatusBar());

		Content = mainLayout;

		// Set the right-panel initial size once form is shown
		Shown += (_, _) =>
		{
			innerSplitter.Position = Math.Max(100, (innerSplitter.Width - 240));
		};

		RefreshPreview();
	}

	// ── Toolbar ──────────────────────────────────────────────────────────────────

	Control BuildToolbar()
	{
		return new StackLayout
		{
			Orientation = Orientation.Horizontal,
			Padding     = new Padding(4, 3),
			Spacing     = 3,
			Items       =
			{
				// Shape primitives
				NodeBtn("Rectangle",   MakeRectangle),
				NodeBtn("Circle",      MakeCircle),
				NodeBtn("L-Shape",     MakeLShape),
				NodeBtn("T-Shape",     MakeTShape),
				new Panel { Width = 8 },
				// Boolean operations
				NodeBtn("Union",        MakeUnion),
				NodeBtn("Intersection", MakeIntersection),
				NodeBtn("Difference",   MakeDifference),
				new Panel { Width = 8 },
				// Output
				NodeBtn("Output",       MakeOutput),
				new StackLayoutItem(new Panel(), expand: true),
				// Utility
				QuickBtn("Frame (F)",  () => _graphView.FrameAll()),
				QuickBtn("Reset Demo", ResetDemo),
				QuickBtn("Clear",      () =>
				{
					_graph = new NodeGraph();
					_graphView.Graph = _graph;
					_propPanel.Clear();
					RefreshPreview();
				}),
			},
		};
	}

	Button NodeBtn(string label, Func<NodeItem> factory)
	{
		var btn = new Button { Text = label };
		btn.Click += (_, _) =>
		{
			var node = factory();
			var centre = _graphView.ViewToGraph(
				new PointF(_graphView.Width / 2f, _graphView.Height / 2f));
			node.Position = new PointF(centre.X - NodeGraphView.DefaultNodeWidth / 2f,
			                           centre.Y - 40f);
			_graph.AddNode(node);
			SetStatus($"Added '{node.Title}' node");
		};
		return btn;
	}

	static Button QuickBtn(string label, Action action)
	{
		var btn = new Button { Text = label };
		btn.Click += (_, _) => action();
		return btn;
	}

	// ── Status bar ───────────────────────────────────────────────────────────────

	Control BuildStatusBar()
	{
		return new Panel
		{
			BackgroundColor = Color.FromRgb(0x12121F),
			Padding         = new Padding(8, 3),
			Content         = _statusLabel,
		};
	}

	void SetStatus(string msg) => _statusLabel.Text = msg;

	// ── Event handlers ───────────────────────────────────────────────────────────

	void OnSelectionChanged(object sender, NodeItemEventArgs e)
	{
		var node = e.Node ?? _graphView.SelectedNodes.FirstOrDefault();
		_propPanel.Populate(node);
		_previewPanel.SetNode(node);
		if (node != null)
			SetStatus($"Selected: {node.Title}");
	}

	void RefreshPreview()
	{
		_previewPanel.Refresh();
	}

	// ── Node factories ───────────────────────────────────────────────────────────

	private NodeItem MakeRectangle()
	{
		var n = new NodeItem { Title = "Rectangle", HeaderColor = Color.FromRgb(0x1565C0) };
		AddParamInput(n, "Width",  "4");
		AddParamInput(n, "Height", "2");
		n.AddOutput("Shape", s_geomType);
		return n;
	}

	private NodeItem MakeCircle()
	{
		var n = new NodeItem { Title = "Circle", HeaderColor = Color.FromRgb(0x006064) };
		AddParamInput(n, "Radius",   "1.5");
		AddParamInput(n, "Segments", "32").IsPinned = false;
		AddParamInput(n, "Center X", "0").IsPinned  = false;
		AddParamInput(n, "Center Y", "0").IsPinned  = false;
		n.AddOutput("Shape", s_geomType);
		return n;
	}

	private NodeItem MakeLShape()
	{
		var n = new NodeItem { Title = "L-Shape", HeaderColor = Color.FromRgb(0x4A148C) };
		AddParamInput(n, "Width",      "4");
		AddParamInput(n, "Height",     "4");
		AddParamInput(n, "Arm Width",  "2").IsPinned  = false;
		AddParamInput(n, "Arm Height", "2").IsPinned  = false;
		n.AddOutput("Shape", s_geomType);
		return n;
	}

	private NodeItem MakeTShape()
	{
		var n = new NodeItem { Title = "T-Shape", HeaderColor = Color.FromRgb(0x827717) };
		AddParamInput(n, "Bar Width",   "4");
		AddParamInput(n, "Bar Height",  "1");
		AddParamInput(n, "Stem Width",  "1").IsPinned  = false;
		AddParamInput(n, "Stem Height", "3").IsPinned  = false;
		n.AddOutput("Shape", s_geomType);
		return n;
	}

	private NodeItem MakeUnion()
	{
		var n = new NodeItem { Title = "Union", HeaderColor = Color.FromRgb(0x1B5E20) };
		n.AddInput("A", s_geomType);
		n.AddInput("B", s_geomType);
		n.AddOutput("Result", s_geomType);
		return n;
	}

	private NodeItem MakeIntersection()
	{
		var n = new NodeItem { Title = "Intersection", HeaderColor = Color.FromRgb(0x33691E) };
		n.AddInput("A", s_geomType);
		n.AddInput("B", s_geomType);
		n.AddOutput("Result", s_geomType);
		return n;
	}

	private NodeItem MakeDifference()
	{
		var n = new NodeItem { Title = "Difference", HeaderColor = Color.FromRgb(0x880E4F) };
		n.AddInput("A", s_geomType);
		n.AddInput("B", s_geomType);
		n.AddOutput("Result", s_geomType);
		return n;
	}

	private NodeItem MakeOutput()
	{
		var n = new NodeItem { Title = "Output", HeaderColor = Color.FromRgb(0xB71C1C) };
		n.AddInput("Shape", s_geomType);
		return n;
	}

	private static NodeSocket AddParamInput(NodeItem node, string name, string defaultVal)
	{
		var s = node.AddInput(name, NodeSocketType.Float, defaultVal);
		return s;
	}

	// ── Example graph ─────────────────────────────────────────────────────────────

	NodeGraph CreateExampleGraph()
	{
		var g = new NodeGraph();

		// Two rectangles…
		var rect1 = g.AddNode(MakeRectangle()); rect1.Position = new PointF(30,  40);
		// Tweak rect1 to be taller
		rect1.Inputs[1].Value = "3";

		var rect2 = g.AddNode(MakeRectangle()); rect2.Position = new PointF(30, 200);
		// Offset rect2 dimensions
		rect2.Inputs[0].Value = "2";
		rect2.Inputs[1].Value = "5";

		// …united…
		var union = g.AddNode(MakeUnion()); union.Position = new PointF(270, 100);

		// …then a circle punched out via Difference
		var circle = g.AddNode(MakeCircle()); circle.Position = new PointF(270, 280);

		var diff = g.AddNode(MakeDifference()); diff.Position = new PointF(510, 180);

		// Output node
		var output = g.AddNode(MakeOutput()); output.Position = new PointF(750, 180);

		// Connect
		g.Connect(rect1.Outputs[0],  union.Inputs[0]);
		g.Connect(rect2.Outputs[0],  union.Inputs[1]);
		g.Connect(union.Outputs[0],  diff.Inputs[0]);
		g.Connect(circle.Outputs[0], diff.Inputs[1]);
		g.Connect(diff.Outputs[0],   output.Inputs[0]);

		return g;
	}

	void ResetDemo()
	{
		_graph           = CreateExampleGraph();
		_graphView.Graph = _graph;
		_propPanel.Clear();
		_previewPanel.SetNode(null);
		_framed = false;
		_graphView.FrameAll();
		SetStatus("Example graph loaded");
	}
}
