using Clipper2Lib;

namespace NodeShapeBuilder;

/// <summary>
/// Evaluates a <see cref="NodeGraph"/> of shape-definition and boolean-operation nodes,
/// producing a <see cref="PathsD"/> (list of polygons) for any given node.
/// </summary>
/// <remarks>
/// <para>
/// Shape nodes produce primitive geometry:
///   • Rectangle  — Width × Height, centred at the origin
///   • Circle     — polygon approximation of a circle (n-gon)
///   • L-shape    — L-shaped polygon (outer rect minus inner notch)
///   • T-shape    — T-shaped polygon
/// </para>
/// <para>
/// Boolean nodes combine two geometry inputs:
///   • Union        — A ∪ B
///   • Intersection — A ∩ B
///   • Difference   — A − B (subtract B from A)
/// </para>
/// <para>
/// The Output node simply passes its input geometry through.
/// </para>
/// </remarks>
public static class ShapeGraphEvaluator
{
	// Scaling factor: Clipper2 works best with integers / large fixed-point coords.
	// We scale by 1000 so that 1 unit = 1000 Clipper units, giving sub-millimetre precision.
	private const double Scale = 1000.0;

	// ── Public entry point ───────────────────────────────────────────────────────

	/// <summary>
	/// Evaluates the geometry for <paramref name="node"/> by walking the graph.
	/// Returns an empty <see cref="PathsD"/> if the node cannot be evaluated.
	/// </summary>
	public static PathsD Evaluate(NodeItem node)
	{
		if (node == null) return new PathsD();
		var visited = new HashSet<NodeItem>();
		return EvaluateNode(node, visited);
	}

	// ── Node dispatch ────────────────────────────────────────────────────────────

	private static PathsD EvaluateNode(NodeItem node, HashSet<NodeItem> visited)
	{
		if (!visited.Add(node)) return new PathsD(); // cycle guard

		return node.Title switch
		{
			"Rectangle"    => MakeRectangle(node),
			"Circle"       => MakeCircle(node),
			"L-Shape"      => MakeLShape(node),
			"T-Shape"      => MakeTShape(node),
			"Union"        => BooleanOp(node, ClipType.Union,        visited),
			"Intersection" => BooleanOp(node, ClipType.Intersection, visited),
			"Difference"   => BooleanOp(node, ClipType.Difference,   visited),
			"Output"       => PassThrough(node, visited),
			_              => new PathsD(),
		};
	}

	// ── Shape generators ─────────────────────────────────────────────────────────

	private static PathsD MakeRectangle(NodeItem node)
	{
		double w = GetParam(node, "Width",  4.0);
		double h = GetParam(node, "Height", 2.0);
		double hw = w / 2, hh = h / 2;

		var path = new PathD
		{
			new PointD(-hw, -hh),
			new PointD( hw, -hh),
			new PointD( hw,  hh),
			new PointD(-hw,  hh),
		};
		return new PathsD { path };
	}

	private static PathsD MakeCircle(NodeItem node)
	{
		double r       = GetParam(node, "Radius",   1.5);
		int    segs    = (int)Math.Max(6, GetParam(node, "Segments", 32));
		double cx      = GetParam(node, "Center X", 0.0);
		double cy      = GetParam(node, "Center Y", 0.0);

		var path = new PathD();
		for (int i = 0; i < segs; i++)
		{
			double angle = 2 * Math.PI * i / segs;
			path.Add(new PointD(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle)));
		}
		return new PathsD { path };
	}

	/// <summary>
	/// Generates an L-shaped polygon.
	/// The outer bounding box is (Width × Height); the inner notch cuts the top-right
	/// corner by (Width × ArmWidth) × ArmHeight.
	/// </summary>
	private static PathsD MakeLShape(NodeItem node)
	{
		double w  = GetParam(node, "Width",     4.0);
		double h  = GetParam(node, "Height",    4.0);
		double aw = GetParam(node, "Arm Width", 2.0); // horizontal arm width
		double ah = GetParam(node, "Arm Height",2.0); // vertical arm height
		// Clamp arm dimensions
		aw = Math.Min(aw, w);
		ah = Math.Min(ah, h);

		double hw = w / 2, hh = h / 2;

		// Counter-clockwise polygon from bottom-left
		var path = new PathD
		{
			new PointD(-hw, -hh),      // bottom-left
			new PointD( hw, -hh),      // bottom-right
			new PointD( hw, -hh + ah), // up to the step
			new PointD(-hw + aw, -hh + ah), // step inward
			new PointD(-hw + aw,  hh), // up to top
			new PointD(-hw,       hh), // top-left
		};
		return new PathsD { path };
	}

	/// <summary>
	/// Generates a T-shaped polygon.
	/// Bar dimensions: BarWidth × BarHeight (the top of the T).
	/// Stem dimensions: StemWidth × StemHeight (the vertical part).
	/// </summary>
	private static PathsD MakeTShape(NodeItem node)
	{
		double bw  = GetParam(node, "Bar Width",   4.0);
		double bh  = GetParam(node, "Bar Height",  1.0);
		double sw  = GetParam(node, "Stem Width",  1.0);
		double sh  = GetParam(node, "Stem Height", 3.0);
		sw  = Math.Min(sw, bw);

		double totalH = bh + sh;
		double hbw    = bw / 2;
		double hsw    = sw / 2;
		double top    = totalH / 2;
		double mid    = top - bh;
		double bot    = -sh / 2 - bh / 2;

		var path = new PathD
		{
			new PointD(-hbw, top),   // top-left of bar
			new PointD( hbw, top),   // top-right of bar
			new PointD( hbw, mid),   // right shoulder
			new PointD( hsw, mid),   // inner right
			new PointD( hsw, bot),   // bottom-right of stem
			new PointD(-hsw, bot),   // bottom-left of stem
			new PointD(-hsw, mid),   // inner left
			new PointD(-hbw, mid),   // left shoulder
		};
		return new PathsD { path };
	}

	// ── Boolean operations ───────────────────────────────────────────────────────

	private static PathsD BooleanOp(NodeItem node, ClipType op, HashSet<NodeItem> visited)
	{
		var aGeom = GetInputGeometry(node, 0, visited);
		var bGeom = GetInputGeometry(node, 1, visited);

		if (aGeom.Count == 0) return bGeom;
		if (bGeom.Count == 0) return op == ClipType.Difference ? aGeom : new PathsD();

		// Clipper.BooleanOp(ClipType, Paths64 subject, Paths64 clip, FillRule) → Paths64
		var result = Clipper.BooleanOp(op, ToInt64Paths(aGeom), ToInt64Paths(bGeom), FillRule.NonZero);
		return FromInt64Paths(result);
	}

	private static PathsD PassThrough(NodeItem node, HashSet<NodeItem> visited) =>
		GetInputGeometry(node, 0, visited);

	// ── Socket helpers ───────────────────────────────────────────────────────────

	private static PathsD GetInputGeometry(NodeItem node, int inputIdx, HashSet<NodeItem> visited)
	{
		if (inputIdx >= node.Inputs.Count) return new PathsD();
		var socket = node.Inputs[inputIdx];
		if (!socket.IsConnected) return new PathsD();

		var conn   = socket.Connections[0];
		var srcNode = conn.Source.Node;

		// Create fresh visited set branch so sibling inputs don't block each other
		var branchVisited = new HashSet<NodeItem>(visited);
		return EvaluateNode(srcNode, branchVisited);
	}

	private static double GetParam(NodeItem node, string name, double defaultValue)
	{
		var socket = node.Inputs.FirstOrDefault(s => s.Name == name);
		if (socket == null) return defaultValue;
		if (socket.IsConnected) return defaultValue; // connected numeric value not supported yet
		return double.TryParse(socket.Value,
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out var v) ? v : defaultValue;
	}

	// ── Clipper2 unit conversion ─────────────────────────────────────────────────

	private static Paths64 ToInt64Paths(PathsD src)
	{
		var dst = new Paths64(src.Count);
		foreach (var poly in src)
		{
			var p64 = new Path64(poly.Count);
			foreach (var pt in poly)
				p64.Add(new Point64((long)(pt.x * Scale), (long)(pt.y * Scale)));
			dst.Add(p64);
		}
		return dst;
	}

	private static PathsD FromInt64Paths(Paths64 src)
	{
		var dst = new PathsD(src.Count);
		foreach (var poly in src)
		{
			var pd = new PathD(poly.Count);
			foreach (var pt in poly)
				pd.Add(new PointD(pt.X / Scale, pt.Y / Scale));
			dst.Add(pd);
		}
		return dst;
	}
}
