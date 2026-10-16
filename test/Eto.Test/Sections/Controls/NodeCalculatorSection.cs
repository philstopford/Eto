namespace Eto.Test.Sections.Controls
{
	/// <summary>
	/// Demonstrates a live-evaluating calculator built on top of <see cref="NodeGraphView"/>.
	/// Each connection change or value edit immediately re-computes and displays the result.
	/// </summary>
	/// <remarks>
	/// Node types:
	///   Number   – A constant float value (editable via the Properties panel).
	///   Add      – A + B
	///   Subtract – A − B
	///   Multiply – A × B
	///   Divide   – A ÷ B  (division by zero → ∞)
	///   Abs      – |A|
	///   Negate   – −A
	///   Display  – Accepts one float and shows the computed value.  Only the
	///              Display node's result is shown in the result bar; there can
	///              be multiple Display nodes in the graph.
	/// <para/>
	/// Controls are the same as NodeGraphView (drag sockets to connect, right-click to delete, etc.).
	/// </remarks>
	[Section("Controls", "Node Calculator")]
	public class NodeCalculatorSection : Panel
	{
		// ── Types shared across the section ─────────────────────────────────────────

		static readonly NodeSocketType s_numType  = new NodeSocketType("Number", Color.FromRgb(0xF5A623));

		// ── UI ──────────────────────────────────────────────────────────────────────

		NodeGraphView      _graphView;
		NodeGraph          _graph;
		bool               _framed;
		CalcPropertyPanel  _propPanel;
		NodeListPanel      _nodeListPanel;
		Label              _resultLabel;

		public NodeCalculatorSection()
		{
			_graphView     = new NodeGraphView();
			_propPanel     = new CalcPropertyPanel(_graphView, OnValueChanged);
			_nodeListPanel = new NodeListPanel(_graphView);
			_resultLabel = new Label
			{
				Text      = "No Display node",
				TextColor = Colors.White,
				Font      = new Font(SystemFont.Bold, 12f),
			};

			_graph            = CreateExampleGraph();
			_graphView.Graph  = _graph;

			_graphView.SizeChanged += (s, e) =>
			{
				if (!_framed && _graphView.Width > 0 && _graphView.Height > 0)
				{
					_framed = true;
					_graphView.FrameAll();
				}
			};

			_graphView.SelectionChanged  += OnSelectionChanged;
			_graphView.ConnectionCreated += (_, _) => ReEvaluate();
			_graphView.ConnectionDeleted += (_, _) => ReEvaluate();

			var toolbar   = BuildToolbar();
			var resultBar = BuildResultBar();

			// [ NodeList | [ PropertyPanel | GraphView ] ]
			var innerSplitter = new Splitter
			{
				Orientation = Orientation.Horizontal,
				Panel1      = _propPanel,
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
			layout.Add(toolbar);
			layout.Add(outerSplitter, yscale: true);
			layout.Add(resultBar);
			Content = layout;

			ReEvaluate();
		}

		// ── Toolbar ─────────────────────────────────────────────────────────────────

		Control BuildToolbar()
		{
			return new StackLayout
			{
				Orientation = Orientation.Horizontal,
				Padding     = new Padding(4, 2),
				Spacing     = 3,
				Items       =
				{
					NodeButton("+ Number",   () => MakeNumber()),
					NodeButton("+ Add",      () => MakeBinary("Add",      "A + B",  "+",  "0",  "0")),
					NodeButton("+ Subtract", () => MakeBinary("Subtract", "A − B",  "−",  "0",  "0")),
					NodeButton("+ Multiply", () => MakeBinary("Multiply", "A × B",  "×",  "1",  "1")),
					NodeButton("+ Divide",   () => MakeBinary("Divide",   "A ÷ B",  "÷",  "0",  "1")),
					NodeButton("+ Abs",      () => MakeUnary("Abs",      "|A|")),
					NodeButton("+ Negate",   () => MakeUnary("Negate",   "−A")),
					NodeButton("+ Display",  () => MakeDisplay()),
					new StackLayoutItem(new Panel(), expand: true),
					QuickButton("Frame (F)",  () => _graphView.FrameAll()),
					QuickButton("Reset",      ResetExample),
					QuickButton("Clear",      () => { _graph = new NodeGraph(); _graphView.Graph = _graph; _propPanel.Clear(); ReEvaluate(); }),
				},
			};
		}

		Button NodeButton(string text, Func<NodeItem> factory)
		{
			var btn = new Button { Text = text };
			btn.Click += (_, _) =>
			{
				var node = factory();
				var c    = _graphView.ViewToGraph(new PointF(_graphView.Width / 2f, _graphView.Height / 2f));
				node.Position = new PointF(c.X - NodeGraphView.DefaultNodeWidth / 2f, c.Y - 40f);
				_graph.AddNode(node);
			};
			return btn;
		}

		Button QuickButton(string text, Action action)
		{
			var btn = new Button { Text = text };
			btn.Click += (_, _) => action();
			return btn;
		}

		// ── Result bar ───────────────────────────────────────────────────────────────

		Control BuildResultBar()
		{
			return new Panel
			{
				Padding         = new Padding(8, 4),
				BackgroundColor = Color.FromRgb(0x12121F),
				Content         = new StackLayout
				{
					Orientation = Orientation.Horizontal,
					Spacing     = 8,
					Items       =
					{
						new Label
						{
							Text      = "Result:",
							TextColor = Color.FromRgb(0x888888),
							VerticalAlignment = VerticalAlignment.Center,
						},
						new StackLayoutItem(_resultLabel, VerticalAlignment.Center),
					},
				},
			};
		}

		// ── Event handlers ───────────────────────────────────────────────────────────

		void OnSelectionChanged(object sender, NodeItemEventArgs e)
		{
			var node = e.Node ?? _graphView.SelectedNodes.FirstOrDefault();
			_propPanel.Populate(node);
		}

		void OnValueChanged()
		{
			// Called when a Number node's value is edited in the properties panel
			ReEvaluate();
		}

		void ResetExample()
		{
			_graph           = CreateExampleGraph();
			_graphView.Graph = _graph;
			_propPanel.Clear();
			_framed = false;
			_graphView.FrameAll();
			ReEvaluate();
		}

		// ── Evaluator ────────────────────────────────────────────────────────────────

		/// <summary>
		/// Evaluates all Display nodes in the graph and updates the result label.
		/// </summary>
		void ReEvaluate()
		{
			if (_graph == null) { _resultLabel.Text = "—"; return; }

			var displayNodes = _graph.Nodes
				.Where(n => n.Title == "Display")
				.ToList();

			if (displayNodes.Count == 0)
			{
				_resultLabel.Text = "No Display node";
				return;
			}

			var results = new List<string>();
			var visited = new HashSet<NodeItem>();

			foreach (var dn in displayNodes)
			{
				double v = EvaluateInput(dn, 0, visited);
				visited.Clear();
				string formatted = double.IsInfinity(v) ? "∞"
					: double.IsNaN(v)      ? "NaN"
					: v.ToString("G6");
				results.Add(dn.Tag is string label ? $"{label} = {formatted}" : formatted);
			}

			_resultLabel.Text = string.Join("   |   ", results);
		}

		/// <summary>Recursively evaluates the value produced by a node.</summary>
		double EvaluateNode(NodeItem node, HashSet<NodeItem> visited)
		{
			// Cycle guard
			if (!visited.Add(node)) return 0;

			switch (node.Title)
			{
				case "Number":   return ParseValue(node.Outputs[0].Value);
				case "Add":      return EvaluateInput(node, 0, visited) + EvaluateInput(node, 1, visited);
				case "Subtract": return EvaluateInput(node, 0, visited) - EvaluateInput(node, 1, visited);
				case "Multiply": return EvaluateInput(node, 0, visited) * EvaluateInput(node, 1, visited);
				case "Divide":
				{
					double b = EvaluateInput(node, 1, visited);
					return b == 0 ? double.PositiveInfinity : EvaluateInput(node, 0, visited) / b;
				}
				case "Abs":      return Math.Abs(EvaluateInput(node, 0, visited));
				case "Negate":   return -EvaluateInput(node, 0, visited);
				case "Display":  return EvaluateInput(node, 0, visited);
				default:         return 0;
			}
		}

		/// <summary>Gets the value at input socket index <paramref name="idx"/>.</summary>
		double EvaluateInput(NodeItem node, int idx, HashSet<NodeItem> visited)
		{
			if (idx >= node.Inputs.Count) return 0;
			var socket = node.Inputs[idx];
			if (socket.IsConnected)
			{
				var conn = socket.Connections[0];
				return EvaluateNode(conn.Source.Node, visited);
			}
			return ParseValue(socket.Value);
		}

		static double ParseValue(string s) =>
			double.TryParse(s, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

		// ── Node factories ───────────────────────────────────────────────────────────

		NodeItem MakeNumber(double value = 0)
		{
			var n = new NodeItem { Title = "Number", HeaderColor = Color.FromRgb(0x2563EB) };
			var out0 = n.AddOutput("Value", s_numType);
			out0.Value = value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
			return n;
		}

		NodeItem MakeBinary(string title, string outLabel, string symbol, string defaultA, string defaultB)
		{
			var n = new NodeItem { Title = title, HeaderColor = Color.FromRgb(0x7C3AED) };
			n.AddInput("A", s_numType, defaultA);
			n.AddInput("B", s_numType, defaultB);
			n.AddOutput(outLabel, s_numType);
			return n;
		}

		NodeItem MakeUnary(string title, string outLabel)
		{
			var n = new NodeItem { Title = title, HeaderColor = Color.FromRgb(0x059669) };
			n.AddInput("A",    s_numType, "0");
			n.AddOutput(outLabel, s_numType);
			return n;
		}

		NodeItem MakeDisplay()
		{
			var n = new NodeItem { Title = "Display", HeaderColor = Color.FromRgb(0xDC2626) };
			n.AddInput("Value", s_numType, "0");
			return n;
		}

		// ── Example graph ────────────────────────────────────────────────────────────

		NodeGraph CreateExampleGraph()
		{
			var g = new NodeGraph();

			var n3   = g.AddNode(MakeNumber(3));  n3.Position  = new PointF(40,  40);
			var n4   = g.AddNode(MakeNumber(4));  n4.Position  = new PointF(40,  160);
			var n10  = g.AddNode(MakeNumber(10)); n10.Position = new PointF(40,  280);
			var n2   = g.AddNode(MakeNumber(2));  n2.Position  = new PointF(40,  400);

			var add = g.AddNode(MakeBinary("Add", "A + B", "+", "0", "0"));
			add.Position = new PointF(270, 80);

			var mul = g.AddNode(MakeBinary("Multiply", "A × B", "×", "1", "1"));
			mul.Position = new PointF(270, 320);

			var sub = g.AddNode(MakeBinary("Subtract", "A − B", "−", "0", "0"));
			sub.Position = new PointF(510, 160);

			var disp = g.AddNode(MakeDisplay()); disp.Position = new PointF(740, 160);

			// (3 + 4) − (10 × 2) = 7 − 20 = −13
			g.Connect(n3.Outputs[0],   add.Inputs[0]);
			g.Connect(n4.Outputs[0],   add.Inputs[1]);
			g.Connect(n10.Outputs[0],  mul.Inputs[0]);
			g.Connect(n2.Outputs[0],   mul.Inputs[1]);
			g.Connect(add.Outputs[0],  sub.Inputs[0]);
			g.Connect(mul.Outputs[0],  sub.Inputs[1]);
			g.Connect(sub.Outputs[0],  disp.Inputs[0]);

			return g;
		}
	}

	// ── CalcPropertyPanel ────────────────────────────────────────────────────────

	/// <summary>
	/// Property panel for the calculator that adds an inline numeric editor for
	/// <c>Number</c> nodes, allowing the user to change the constant without rebuilding
	/// the graph.
	/// </summary>
	internal class CalcPropertyPanel : Panel
	{
		private static readonly Color s_bg      = Color.FromRgb(0x1A1A2A);
		private static readonly Color s_muted   = Color.FromRgb(0x888899);
		private static readonly Color s_section = Color.FromRgb(0x555570);

		private readonly NodeGraphView _view;
		private readonly Action        _onValueChanged;

		public CalcPropertyPanel(NodeGraphView view, Action onValueChanged)
		{
			_view           = view;
			_onValueChanged = onValueChanged;
			BackgroundColor = s_bg;
			ShowPlaceholder();
		}

		// ── Public API ───────────────────────────────────────────────────────────────

		public void Populate(NodeItem node)
		{
			if (node == null) { ShowPlaceholder(); return; }

			var scroll = new Scrollable
			{
				BackgroundColor = s_bg,
				Border          = BorderType.None,
			};

			var layout = new DynamicLayout
			{
				Padding         = new Padding(8, 6),
				DefaultSpacing  = new Size(4, 4),
				BackgroundColor = s_bg,
			};

			layout.Add(new Label
			{
				Text      = node.Title,
				Font      = new Font(SystemFont.Bold, 10f),
				TextColor = Colors.White,
			});

			// ── Editable numeric value for Number nodes ───────────────────────────────
			if (node.Title == "Number" && node.Outputs.Count > 0)
			{
				layout.Add(MakeSectionLabel("VALUE"));
				var valueOut = node.Outputs[0]; // stores the constant

				var tb = new TextBox
				{
					Text      = valueOut.Value ?? "0",
					Font      = new Font(SystemFont.Default, 11f),
					TextColor = Color.FromRgb(0xF5A623),
				};
				tb.TextChanged += (_, _) =>
				{
					valueOut.Value = tb.Text;
					_onValueChanged();
				};

				layout.Add(tb);
				layout.Add(new Panel { Height = 6 });
			}

			// ── Sockets ───────────────────────────────────────────────────────────────
			if (node.Inputs.Count > 0)
			{
				layout.Add(MakeSectionLabel("INPUTS"));
				foreach (var s in node.Inputs)
					layout.Add(MakeSocketRow(s, node));
				layout.Add(new Panel { Height = 4 });
			}
			if (node.Outputs.Count > 0)
			{
				layout.Add(MakeSectionLabel("OUTPUTS"));
				foreach (var s in node.Outputs)
					layout.Add(MakeSocketRow(s, node));
			}

			layout.Add(null);
			scroll.Content = layout;
			Content        = scroll;
		}

		public void Clear() => ShowPlaceholder();

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private void ShowPlaceholder()
		{
			Content = new Label
			{
				Text              = "Select a node\nto edit its value",
				TextColor         = s_muted,
				VerticalAlignment = VerticalAlignment.Center,
				TextAlignment     = TextAlignment.Center,
				Wrap              = WrapMode.Word,
			};
		}

		private Label MakeSectionLabel(string text) =>
			new Label { Text = text, Font = new Font(SystemFont.Default, 7.5f), TextColor = s_section };

		private Control MakeSocketRow(NodeSocket socket, NodeItem node)
		{
			var pin = new CheckBox
			{
				Checked = socket.IsPinned,
				ToolTip = socket.IsPinned ? "Unpin from canvas" : "Pin to canvas",
			};
			pin.CheckedChanged += (_, _) =>
			{
				socket.IsPinned = pin.Checked == true;
				pin.ToolTip     = socket.IsPinned ? "Unpin from canvas" : "Pin to canvas";
				_view.InvalidateNode(node);
			};

			var swatch = new Panel
			{
				BackgroundColor = socket.SocketType.Color,
				Width = 10, Height = 10,
			};

			var nameLabel = new Label { Text = socket.Name, TextColor = Colors.White };

			var row = new StackLayout
			{
				Orientation              = Orientation.Horizontal,
				Spacing                  = 5,
				VerticalContentAlignment = VerticalAlignment.Center,
				Padding                  = new Padding(0, 1),
				Items                    = { pin, swatch, nameLabel },
			};

			if (socket.Direction == NodeSocketDirection.Input && !socket.IsConnected && socket.Value != null)
			{
				row.Items.Add(new Label
				{
					Text      = socket.Value,
					TextColor = Color.FromRgb(0xF5A623),
				});
			}

			return row;
		}
	}
}
