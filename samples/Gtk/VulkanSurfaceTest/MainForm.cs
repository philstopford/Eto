using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;

namespace VulkanSurfaceTest;

/// <summary>
/// Main window for the VulkanSurface test application.
///
/// Layout
/// ──────
///  ┌──────────────────────────────────────────────┐
///  │  Interactive Viewport (Drawable)             │
///  │  Rotating wireframe cube driven by UITimer   │
///  │  • LMB drag  → orbit  • Scroll → zoom       │
///  │  • RMB click → place marker                  │
///  ├──────────────────────────────────────────────┤
///  │  VulkanSurface (native handles — fixed h)    │
///  │  Gray until a Vulkan renderer is attached    │
///  ├──────────────────────────────────────────────┤
///  │  Surface info status grid                    │
///  ├──────────────────────────────────────────────┤
///  │  Event log (scrollable read-only text area)  │
///  ├──────────────────────────────────────────────┤
///  │  [Pause/Resume]  [Clear Markers]  [Clear Log]│
///  └──────────────────────────────────────────────┘
/// </summary>
public class MainForm : Form
{
    // ── VulkanSurface status labels ──────────────────────────────────────────
    readonly Label _lblSurfaceType = new Label { Text = "–" };
    readonly Label _lblWlDisplay   = new Label { Text = "–" };
    readonly Label _lblWlSurface   = new Label { Text = "–" };
    readonly Label _lblDrmNode     = new Label { Text = "–" };
    readonly Label _lblXDisplay    = new Label { Text = "–" };
    readonly Label _lblXWindow     = new Label { Text = "–" };
    readonly Label _lblScaleFactor = new Label { Text = "–" };
    readonly Label _lblRenderCount = new Label { Text = "0" };

    // ── event log ────────────────────────────────────────────────────────────
    readonly TextArea _log = new TextArea
    {
        ReadOnly        = true,
        Wrap            = false,
        BackgroundColor = Colors.Black,
        TextColor       = Colors.LimeGreen,
        Font            = Fonts.Monospace(10),
    };

    // ── viewport: orbit / zoom state ─────────────────────────────────────────
    float  _yaw      = 0.4f;   // user-controlled horizontal angle (radians)
    float  _pitch    = 0.3f;   // user-controlled vertical   angle (radians)
    float  _zoom     = 2.5f;   // perspective distance — smaller = more zoomed in
    PointF _lastDrag;
    bool   _dragging;
    PointF _mousePos;
    readonly List<PointF> _markers = new List<PointF>();

    // ── animation state ──────────────────────────────────────────────────────
    float    _angle;                       // continuously-increasing spin angle
    int      _fps;
    int      _framesSinceLastFps;
    DateTime _fpsEpoch = DateTime.Now;
    long     _totalFrames;
    UITimer  _animTimer = null!;

    // ── VulkanSurface render counter ─────────────────────────────────────────
    int _vulkanRenderCount;

    public MainForm()
    {
        Title       = "VulkanSurface Test — Eto.Forms GTK3";
        ClientSize  = new Size(800, 720);
        Resizable   = true;
        Maximizable = true;

        // ── Interactive Drawable viewport ────────────────────────────────────
        var viewport = new Drawable { BackgroundColor = new Color(0.05f, 0.05f, 0.1f) };
        viewport.Paint      += ViewportPaint;
        viewport.MouseMove  += ViewportMouseMove;
        viewport.MouseDown  += ViewportMouseDown;
        viewport.MouseUp    += ViewportMouseUp;
        viewport.MouseWheel += ViewportMouseWheel;

        // Animation timer fires ~60 times per second, advances the spin and
        // asks the Drawable to repaint.
        _animTimer = new UITimer { Interval = 1.0 / 60.0 };
        _animTimer.Elapsed += (_, _) =>
        {
            _angle += 0.02f;
            if (_angle >= MathF.Tau) _angle -= MathF.Tau;
            _totalFrames++;
            _framesSinceLastFps++;
            var elapsed = (DateTime.Now - _fpsEpoch).TotalSeconds;
            if (elapsed >= 1.0)
            {
                _fps = (int)(_framesSinceLastFps / elapsed);
                _framesSinceLastFps = 0;
                _fpsEpoch = DateTime.Now;
            }
            viewport.Invalidate();
        };

        Load   += (_, _) => _animTimer.Start();
        UnLoad += (_, _) => _animTimer.Stop();

        // ── VulkanSurface (kept for native-handle lifecycle plumbing) ─────────
        var surface = new VulkanSurface();
        surface.SurfaceCreated   += OnSurfaceCreated;
        surface.SurfaceDestroyed += OnSurfaceDestroyed;
        surface.Render           += OnVulkanRender;

        // ── button row ───────────────────────────────────────────────────────
        var btnToggleAnim = new Button { Text = "Pause Animation" };
        btnToggleAnim.Click += (_, _) =>
        {
            if (_animTimer.Started) { _animTimer.Stop();  btnToggleAnim.Text = "Resume Animation"; }
            else                    { _animTimer.Start(); btnToggleAnim.Text = "Pause Animation";  }
        };

        var btnClearMarkers = new Button { Text = "Clear Markers" };
        btnClearMarkers.Click += (_, _) => { _markers.Clear(); viewport.Invalidate(); };

        var btnClearLog = new Button { Text = "Clear Log" };
        btnClearLog.Click += (_, _) => _log.Text = string.Empty;

        // ── layout ───────────────────────────────────────────────────────────
        Content = new TableLayout
        {
            Padding = new Padding(6),
            Spacing = new Size(0, 4),
            Rows =
            {
                // Interactive viewport — fills available space.
                new TableRow(viewport) { ScaleHeight = true },

                // VulkanSurface at fixed height — gray until a Vulkan renderer
                // is wired up; it still fires SurfaceCreated/Render/SurfaceDestroyed.
                new TableRow(new GroupBox
                {
                    Text    = "VulkanSurface (native handles — gray without a Vulkan renderer)",
                    Content = surface,
                    Height  = 80,
                }),

                // Status grid
                BuildStatusPanel(),

                // Event log
                new TableRow(new TableLayout
                {
                    Rows =
                    {
                        new TableRow(new Label { Text = "Event log:", Font = Fonts.Sans(9, FontStyle.Bold) }),
                        new TableRow(new Scrollable { Content = _log, Height = 110 }),
                    }
                }),

                // Buttons
                new TableRow(new TableLayout(
                    new TableRow(btnToggleAnim, btnClearMarkers, btnClearLog, null)
                ) { Spacing = new Size(6, 0) }),
            }
        };

        Log("Viewport ready.  LMB drag: orbit  |  Scroll: zoom  |  RMB click: place marker");
    }

    // ── Viewport: painting ────────────────────────────────────────────────────

    void ViewportPaint(object? sender, PaintEventArgs e)
    {
        var g   = e.Graphics;
        var ctl = (Drawable)sender!;
        int w   = ctl.Width;
        int h   = ctl.Height;

        // Background
        g.FillRectangle(new Color(0.05f, 0.05f, 0.1f), 0, 0, w, h);

        // Subtle grid
        using var gridPen = new Pen(new Color(1f, 1f, 1f, 0.07f));
        for (int x = 0; x <= w; x += 40) g.DrawLine(gridPen, x, 0, x, h);
        for (int y = 0; y <= h; y += 40) g.DrawLine(gridPen, 0, y, w, y);

        // Rotating wireframe cube
        DrawCube(g, w / 2f, h / 2f, w, h);

        // Right-click markers
        using var markerPen = new Pen(Colors.Yellow, 1.5f);
        foreach (var m in _markers)
        {
            g.DrawEllipse(markerPen, m.X - 6, m.Y - 6, 12, 12);
            g.DrawLine(markerPen, m.X - 12, m.Y, m.X + 12, m.Y);
            g.DrawLine(markerPen, m.X, m.Y - 12, m.X, m.Y + 12);
        }

        // Mouse crosshair
        if (w > 0 && h > 0)
        {
            using var crossPen = new Pen(new Color(1f, 1f, 1f, 0.4f));
            g.DrawLine(crossPen, _mousePos.X, 0, _mousePos.X, h);
            g.DrawLine(crossPen, 0, _mousePos.Y, w, _mousePos.Y);
        }

        // HUD overlay
        var font = Fonts.Monospace(10);
        DrawHud(g, font, 8,  8, $"FPS: {_fps}  |  Total frames: {_totalFrames}");
        DrawHud(g, font, 8, 26, "LMB drag: orbit  |  Scroll: zoom  |  RMB click: marker");
        DrawHud(g, font, 8, 44, $"Yaw: {_yaw:F2}  Pitch: {_pitch:F2}  Zoom: {_zoom:F2}");
        DrawHud(g, font, 8, 62, $"Mouse: ({_mousePos.X:F0}, {_mousePos.Y:F0})  Markers: {_markers.Count}");
    }

    static void DrawHud(Graphics g, Font font, float x, float y, string text)
    {
        var sz = g.MeasureString(font, text);
        g.FillRectangle(new Color(0f, 0f, 0f, 0.6f), x - 2, y - 1, sz.Width + 4, sz.Height + 2);
        g.DrawText(font, Colors.White, x, y, text);
    }

    // ── Viewport: 3D wireframe cube ───────────────────────────────────────────

    void DrawCube(Graphics g, float cx, float cy, int w, int h)
    {
        // 8 unit-cube vertices
        float[][] verts =
        {
            new float[] { -1, -1, -1 }, new float[] {  1, -1, -1 },
            new float[] {  1,  1, -1 }, new float[] { -1,  1, -1 },
            new float[] { -1, -1,  1 }, new float[] {  1, -1,  1 },
            new float[] {  1,  1,  1 }, new float[] { -1,  1,  1 },
        };

        // 12 edges (back face, front face, connecting)
        (int a, int b)[] edges =
        {
            (0,1),(1,2),(2,3),(3,0),
            (4,5),(5,6),(6,7),(7,4),
            (0,4),(1,5),(2,6),(3,7),
        };

        // Combined rotation = user-controlled offset + continuous auto-spin
        float yaw   = _yaw   + _angle * 0.6f;
        float pitch = _pitch + _angle * 0.35f;
        float cosY  = MathF.Cos(yaw);
        float sinY  = MathF.Sin(yaw);
        float cosP  = MathF.Cos(pitch);
        float sinP  = MathF.Sin(pitch);
        float fov   = Math.Min(w, h) / _zoom;

        // Project all 8 vertices
        var proj = new PointF[8];
        for (int i = 0; i < 8; i++)
        {
            float x = verts[i][0], y = verts[i][1], z = verts[i][2];

            // Rotate around Y axis
            float xR = x * cosY - z * sinY;
            float zR = x * sinY + z * cosY;
            x = xR; z = zR;

            // Rotate around X axis
            float yR  = y * cosP - z * sinP;
            float zR2 = y * sinP + z * cosP;
            y = yR; z = zR2;

            // Perspective divide (camera sits at z = −4)
            float pz = z + 4f;
            proj[i] = new PointF(cx + x * fov / pz, cy + y * fov / pz);
        }

        // Draw each edge with a hue derived from the current spin angle
        float hue0 = (_angle * 40f) % 360f;
        for (int i = 0; i < edges.Length; i++)
        {
            var (a, b) = edges[i];
            var col    = HsvToColor((hue0 + i * 22.5f) % 360f, 0.85f, 1f);
            using var pen = new Pen(col, 2.5f);
            g.DrawLine(pen, proj[a], proj[b]);
        }
    }

    static Color HsvToColor(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = v - c;
        float r, g, b;
        if      (h <  60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else              { r = c; g = 0; b = x; }
        return new Color(r + m, g + m, b + m);
    }

    // ── Viewport: mouse handlers ──────────────────────────────────────────────

    void ViewportMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            float dx = e.Location.X - _lastDrag.X;
            float dy = e.Location.Y - _lastDrag.Y;
            _yaw   += dx * 0.01f;
            _pitch  = Math.Clamp(_pitch + dy * 0.01f, -MathF.PI / 2f, MathF.PI / 2f);
        }
        _mousePos = e.Location;
        _lastDrag = e.Location;
    }

    void ViewportMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Buttons == MouseButtons.Primary)
        {
            _dragging = true;
            _lastDrag = e.Location;
        }
        else if (e.Buttons == MouseButtons.Alternate)
        {
            _markers.Add(e.Location);
            Log($"Marker #{_markers.Count} placed at ({e.Location.X:F0}, {e.Location.Y:F0})");
        }
    }

    void ViewportMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Buttons == MouseButtons.Primary)
            _dragging = false;
    }

    void ViewportMouseWheel(object? sender, MouseEventArgs e)
    {
        // Positive Delta.Height = scroll up = zoom in = decrease zoom distance.
        _zoom = Math.Clamp(_zoom - e.Delta.Height * 0.3f, 0.5f, 10f);
    }

    // ── VulkanSurface event handlers ──────────────────────────────────────────

    void OnSurfaceCreated(object? sender, EventArgs e)
    {
        var vs   = (VulkanSurface)sender!;
        var info = vs.GetSurfaceInfo();

        _lblScaleFactor.Text = vs.BackingScaleFactor.ToString("F2");

        if (info == null)
        {
            Log("SurfaceCreated — GetSurfaceInfo() returned null");
            _lblSurfaceType.Text = "null";
            return;
        }

        _lblSurfaceType.Text = info.SurfaceType.ToString();

        if (info.SurfaceType == VulkanSurfaceType.Wayland)
        {
            _lblWlDisplay.Text = FormatPtr(info.WlDisplay);
            _lblWlSurface.Text = FormatPtr(info.WlSurface);
            _lblDrmNode.Text   = info.PreferredPhysicalDeviceDrmNode ?? "(none)";
            _lblXDisplay.Text  = "N/A";
            _lblXWindow.Text   = "N/A";
        }
        else
        {
            _lblXDisplay.Text  = FormatPtr(info.XDisplay);
            _lblXWindow.Text   = $"0x{info.XWindow:X}";
            _lblWlDisplay.Text = "N/A";
            _lblWlSurface.Text = "N/A";
            _lblDrmNode.Text   = "N/A";
        }

        Log($"SurfaceCreated  type={info.SurfaceType}  size={vs.Size}  scale={vs.BackingScaleFactor:F2}");

        if (info.SurfaceType == VulkanSurfaceType.Wayland)
        {
            Log($"  wl_display = {FormatPtr(info.WlDisplay)}");
            Log($"  wl_surface = {FormatPtr(info.WlSurface)}");
            Log($"  drm_node   = {info.PreferredPhysicalDeviceDrmNode ?? "(not available)"}");
        }
        else
        {
            Log($"  XDisplay   = {FormatPtr(info.XDisplay)}");
            Log($"  XWindow    = 0x{info.XWindow:X}");
        }
    }

    void OnVulkanRender(object? sender, VulkanRenderEventArgs e)
    {
        _vulkanRenderCount++;
        _lblRenderCount.Text = _vulkanRenderCount.ToString();
        // Log only the first few and every 100th render to keep the log readable.
        if (_vulkanRenderCount <= 3 || _vulkanRenderCount % 100 == 0)
        {
            var vs = (VulkanSurface)sender!;
            Log($"VulkanSurface Render #{_vulkanRenderCount}  size={vs.Size}");
        }
    }

    void OnSurfaceDestroyed(object? sender, EventArgs e)
    {
        Log("SurfaceDestroyed — Vulkan resources should be released here.");
        _lblSurfaceType.Text = "destroyed";
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    Control BuildStatusPanel()
    {
        static TableRow Row(string label, Label value) =>
            new TableRow(
                new Label { Text = label, VerticalAlignment = VerticalAlignment.Center },
                new TableCell(value, true)
            );

        return new GroupBox
        {
            Text    = "VulkanSurface info",
            Padding = new Padding(4),
            Content = new TableLayout
            {
                Spacing = new Size(8, 2),
                Rows =
                {
                    Row("Surface type:",         _lblSurfaceType),
                    Row("wl_display:",           _lblWlDisplay),
                    Row("wl_surface:",           _lblWlSurface),
                    Row("DRM render node:",      _lblDrmNode),
                    Row("XDisplay:",             _lblXDisplay),
                    Row("XWindow:",              _lblXWindow),
                    Row("Backing scale factor:", _lblScaleFactor),
                    Row("Render frame count:",   _lblRenderCount),
                }
            }
        };
    }

    void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Application.Instance.AsyncInvoke(() => _log.Text += line + "\n");
    }

    static string FormatPtr(IntPtr ptr) =>
        ptr == IntPtr.Zero ? "(zero — not created)" : $"0x{ptr.ToInt64():X}";
}
