using Clipper2Lib;

namespace NodeShapeBuilder;

/// <summary>
/// An Eto <see cref="Drawable"/> that renders the geometry produced by the currently
/// selected <see cref="NodeItem"/> in the <see cref="NodeGraphView"/>.
/// </summary>
/// <remarks>
/// The panel automatically re-evaluates and repaints whenever <see cref="Refresh"/> is called
/// by the <see cref="MainForm"/> after any graph change.
/// </remarks>
public class ShapePreviewPanel : Drawable
{
	// ── State ────────────────────────────────────────────────────────────────────

	private NodeItem  _selectedNode;
	private PathsD    _currentPaths = new PathsD();
	private string    _errorMessage = null;

	// ── Colors ───────────────────────────────────────────────────────────────────

	private static readonly Color s_bg            = Color.FromRgb(0x1A1A2A);
	private static readonly Color s_gridMajor     = Color.FromArgb(40, 40, 55, 255);
	private static readonly Color s_gridMinor     = Color.FromArgb(25, 25, 38, 255);
	private static readonly Color s_shapeFill     = Color.FromArgb(0x1D, 0x7A, 0xBA, 180);
	private static readonly Color s_shapeStroke   = Color.FromRgb(0x50C8FF);
	private static readonly Color s_originLine    = Color.FromArgb(80, 80, 100, 255);
	private static readonly Color s_textColor     = Color.FromRgb(0x888899);
	private static readonly Color s_errorColor    = Color.FromRgb(0xFF4444);
	private static readonly Color s_nodeTitleColor = Color.FromRgb(0xCCCCFF);

	// ── Public API ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Updates the displayed geometry for <paramref name="node"/>.
	/// Pass <c>null</c> to clear the preview.
	/// </summary>
	public void SetNode(NodeItem node)
	{
		_selectedNode = node;
		ReevaluateAndRepaint();
	}

	/// <summary>Re-evaluates the current node (e.g. after a connection change) and repaints.</summary>
	public void Refresh()
	{
		ReevaluateAndRepaint();
	}

	// ── Evaluation ───────────────────────────────────────────────────────────────

	private void ReevaluateAndRepaint()
	{
		_errorMessage = null;
		if (_selectedNode == null)
		{
			_currentPaths = new PathsD();
		}
		else
		{
			try
			{
				_currentPaths = ShapeGraphEvaluator.Evaluate(_selectedNode);
			}
			catch (Exception ex)
			{
				_currentPaths = new PathsD();
				_errorMessage = ex.Message;
			}
		}
		Invalidate();
	}

	// ── Painting ─────────────────────────────────────────────────────────────────

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);

		var g   = e.Graphics;
		int vw  = Width;
		int vh  = Height;

		// Background
		g.FillRectangle(s_bg, 0, 0, vw, vh);

		if (_selectedNode == null)
		{
			DrawCentreText(g, vw, vh, "Select a node to preview its geometry");
			return;
		}

		// Draw title
		using var titleFont = new Font(SystemFont.Bold, 9f);
		g.DrawText(titleFont, s_nodeTitleColor, 8, 6, _selectedNode.Title);

		if (_errorMessage != null)
		{
			DrawCentreText(g, vw, vh, $"Error: {_errorMessage}", s_errorColor);
			return;
		}

		if (_currentPaths.Count == 0)
		{
			DrawCentreText(g, vw, vh, "No geometry (check connections)");
			return;
		}

		// Compute bounding box of all polygons
		double minX = double.MaxValue, minY = double.MaxValue;
		double maxX = double.MinValue, maxY = double.MinValue;
		foreach (var poly in _currentPaths)
		{
			foreach (var pt in poly)
			{
				if (pt.x < minX) minX = pt.x;
				if (pt.y < minY) minY = pt.y;
				if (pt.x > maxX) maxX = pt.x;
				if (pt.y > maxY) maxY = pt.y;
			}
		}

		// Scale and center
		float margin  = 40f;
		float geomW   = (float)(maxX - minX);
		float geomH   = (float)(maxY - minY);
		float availW  = vw - margin * 2;
		float availH  = vh - margin * 2 - 20f; // leave room for title
		float scaleX  = geomW > 0 ? availW / geomW : 1;
		float scaleY  = geomH > 0 ? availH / geomH : 1;
		float scale   = Math.Min(scaleX, scaleY);
		float centreX = margin + availW / 2f;
		float centreY = margin + 20f + availH / 2f;
		float midGeomX = (float)((minX + maxX) / 2);
		float midGeomY = (float)((minY + maxY) / 2);

		// Draw grid (5-unit spacing scaled)
		DrawGrid(g, vw, vh, centreX, centreY, scale, midGeomX, midGeomY);

		// Draw origin crosshairs
		float ox = centreX - midGeomX * scale;
		float oy = centreY + midGeomY * scale; // Y flipped (screen Y increases downward)
		using (var pen = new Pen(s_originLine, 1f))
		{
			g.DrawLine(pen, ox, 0, ox, vh);
			g.DrawLine(pen, 0, oy, vw, oy);
		}

		// Draw each polygon
		foreach (var poly in _currentPaths)
		{
			if (poly.Count < 2) continue;

			var screenPoints = poly.Select(pt => ToScreen(pt, centreX, centreY, scale, midGeomX, midGeomY))
			                       .ToArray();

			// Fill
			g.FillPolygon(s_shapeFill, screenPoints);

			// Stroke
			using (var pen = new Pen(s_shapeStroke, 1.5f))
			{
				for (int i = 0; i < screenPoints.Length; i++)
				{
					var a = screenPoints[i];
					var b = screenPoints[(i + 1) % screenPoints.Length];
					g.DrawLine(pen, a, b);
				}
			}
		}

		// Stats
		using var statsFont = new Font(SystemFont.Default, 8f);
		int totalVerts = _currentPaths.Sum(p => p.Count);
		g.DrawText(statsFont, s_textColor, 8, vh - 20,
			$"{_currentPaths.Count} polygon(s), {totalVerts} vertices");
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────

	private static PointF ToScreen(PointD geo, float cx, float cy, float scale,
	                               float midGeoX, float midGeoY)
	{
		// Flip Y: geometry Y+ is up, screen Y+ is down
		return new PointF(
			cx + ((float)geo.x - midGeoX) * scale,
			cy - ((float)geo.y - midGeoY) * scale);
	}

	private void DrawGrid(Graphics g, int vw, int vh,
	                      float cx, float cy, float scale,
	                      float midGeoX, float midGeoY)
	{
		// Grid spacing in geometry units — pick something sensible
		float minorStep = PickGridStep(scale, 20f);
		float majorStep = minorStep * 5;

		float originX = cx - midGeoX * scale;
		float originY = cy + midGeoY * scale;

		// Vertical lines
		for (float gx = originX % majorStep; gx < vw; gx += minorStep)
		{
			bool isMajor = Math.Abs(gx % majorStep) < 0.5f;
			using var pen = new Pen(isMajor ? s_gridMajor : s_gridMinor, 1f);
			g.DrawLine(pen, gx, 0, gx, vh);
		}
		// Horizontal lines
		for (float gy = originY % majorStep; gy < vh; gy += minorStep)
		{
			bool isMajor = Math.Abs(gy % majorStep) < 0.5f;
			using var pen = new Pen(isMajor ? s_gridMajor : s_gridMinor, 1f);
			g.DrawLine(pen, 0, gy, vw, gy);
		}
	}

	private static float PickGridStep(float scale, float targetPixels)
	{
		// Try to find a grid spacing such that the pixel distance is ~targetPixels
		float raw = targetPixels / scale;
		float[] nice = { 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f, 20f, 50f, 100f };
		return nice.OrderBy(s => Math.Abs(s - raw)).FirstOrDefault(s => s > 0);
	}

	private void DrawCentreText(Graphics g, int vw, int vh, string text,
	                            Color? color = null)
	{
		using var font = new Font(SystemFont.Default, 9f);
		var c      = color ?? s_textColor;
		var size   = g.MeasureString(font, text);
		g.DrawText(font, c, (vw - size.Width) / 2f, (vh - size.Height) / 2f, text);
	}
}
