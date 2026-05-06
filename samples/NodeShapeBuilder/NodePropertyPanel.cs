namespace NodeShapeBuilder;

/// <summary>
/// Side panel listing all sockets of the selected node grouped as INPUTS / OUTPUTS.
/// Each socket row shows a pin checkbox (toggles <see cref="NodeSocket.IsPinned"/>),
/// a type-colour swatch, the socket name, and—for unconnected inputs—the default value.
/// Editing a parameter value calls <paramref name="onValueChanged"/> so the caller
/// can re-evaluate and refresh the geometry preview.
/// </summary>
internal class NodePropertyPanel : Panel
{
	// ── Colours ──────────────────────────────────────────────────────────────────

	private static readonly Color s_bg      = Color.FromRgb(0x1A1A2A);
	private static readonly Color s_muted   = Color.FromRgb(0x888899);
	private static readonly Color s_section = Color.FromRgb(0x555570);

	// ── State ────────────────────────────────────────────────────────────────────

	private readonly NodeGraphView _view;
	private readonly Action        _onValueChanged;

	public NodePropertyPanel(NodeGraphView view, Action onValueChanged = null)
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

		// Node title
		layout.Add(new Label
		{
			Text      = node.Title,
			Font      = new Font(SystemFont.Bold, 10f),
			TextColor = Colors.White,
		});
		layout.Add(new Panel { Height = 4 });

		// Inputs
		if (node.Inputs.Count > 0)
		{
			layout.Add(MakeSectionLabel("INPUTS"));
			foreach (var s in node.Inputs)
				layout.Add(MakeSocketRow(s, node));
			layout.Add(new Panel { Height = 4 });
		}

		// Outputs
		if (node.Outputs.Count > 0)
		{
			layout.Add(MakeSectionLabel("OUTPUTS"));
			foreach (var s in node.Outputs)
				layout.Add(MakeSocketRow(s, node));
		}

		layout.Add(null); // spacer
		scroll.Content = layout;
		Content        = scroll;
	}

	public void Clear() => ShowPlaceholder();

	// ── Helpers ──────────────────────────────────────────────────────────────────

	private void ShowPlaceholder()
	{
		Content = new Label
		{
			Text              = "Select a node\nto see its properties",
			TextColor         = s_muted,
			VerticalAlignment = VerticalAlignment.Center,
			TextAlignment     = TextAlignment.Center,
			Wrap              = WrapMode.Word,
		};
	}

	private static Label MakeSectionLabel(string text) =>
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
			BackgroundColor = socket.SocketType?.Color ?? Colors.Gray,
			Width = 10,
			Height = 10,
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

		// For unconnected inputs: show editable default value
		if (socket.Direction == NodeSocketDirection.Input && !socket.IsConnected && socket.Value != null)
		{
			var tb = new TextBox
			{
				Text      = socket.Value,
				Width     = 60,
				TextColor = Color.FromRgb(0xF5A623),
			};
			tb.TextChanged += (_, _) =>
			{
				socket.Value = tb.Text;
				_onValueChanged?.Invoke();
			};
			row.Items.Add(new StackLayoutItem(tb, VerticalAlignment.Center));
		}

		return row;
	}
}
