using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using Veldrid;

namespace VeldridSurfaceTest;

/// <summary>
/// Main window for the Veldrid surface test.
///
/// Layout
/// ──────
///  ┌──────────────────────────────────────────────────────┐
///  │  Interactive Camera Input (Eto Drawable)             │
///  │  Software wireframe cube — same camera as Veldrid    │
///  │  • LMB drag → orbit  • Scroll → zoom                │
///  │  • RMB click → place marker                          │
///  ├──────────────────────────────────────────────────────┤
///  │  Veldrid GPU Rendering (VulkanSurface)               │
///  │  Solid-coloured cube, GPU-accelerated via Veldrid    │
///  │  Backend selected below; cube tracks top viewport    │
///  ├──────────────────────────────────────────────────────┤
///  │  Backend: [Vulkan ▼]   Status: …   FPS: 60           │
///  ├──────────────────────────────────────────────────────┤
///  │  Event log                                           │
///  ├──────────────────────────────────────────────────────┤
///  │  [Pause/Resume]  [Clear Markers]  [Clear Log]        │
///  └──────────────────────────────────────────────────────┘
///
/// The Drawable in the upper section provides all mouse input and renders a
/// lightweight Eto wireframe of the cube.  The same yaw / pitch / zoom values
/// are forwarded to <see cref="VeldridRenderer.RenderFrame"/> so the Veldrid
/// solid cube below always matches the wireframe exactly.
///
/// Backend notes
/// ─────────────
///  Vulkan (Linux/GTK)
///    Use <see cref="VulkanSurface"/> — its SurfaceCreated event provides the
///    wl_display + wl_surface (Wayland) or XDisplay + XWindow (X11) handles that
///    Veldrid needs via SwapchainSource.CreateWayland / CreateXlib.
///
///  OpenGL (Linux/GTK)
///    Requires a GLX or EGL context attached to an X11/Wayland window.
///    On X11: create a GLX context with glXCreateContext, expose the window handle
///    from an Eto Drawable's platform view, and supply an OpenGLPlatformInfo to
///    GraphicsDevice.CreateOpenGL.
///    On Wayland: use EGL (eglCreateContext / eglCreateWindowSurface) instead of GLX.
///
///  Direct3D 11 (Windows)
///    Retrieve the HWND from an Eto WinForms Drawable or Panel (Control.Handle),
///    create SwapchainSource.CreateWin32(hwnd, hinstance), and call
///    GraphicsDevice.CreateD3D11(options, swapchainDesc).
///
///  Metal (macOS)
///    Retrieve the NSView handle from an Eto.Mac DrawableHandler.NativeControl,
///    create SwapchainSource.CreateNSWindow(nsWindowHandle), and call
///    GraphicsDevice.CreateMetal(options, swapchainDesc).
/// </summary>
public class MainForm : Form
{
    // ── Veldrid objects ───────────────────────────────────────────────────────
    GraphicsDevice?   _gd;
    VeldridRenderer?  _renderer;

    // ── Shared camera state (Drawable + VeldridRenderer stay in sync) ─────────
    float _userYaw   =  0.4f;   // accumulated LMB-drag horizontal offset (radians)
    float _userPitch =  0.3f;   // accumulated LMB-drag vertical   offset (radians)
    float _zoom      =  3.5f;   // camera distance (scroll to change)
    float _angle;               // continuous auto-spin angle (radians)
    bool  _paused;

    // ── Input tracking ────────────────────────────────────────────────────────
    PointF _lastDrag;
    bool   _dragging;
    PointF _mousePos;
    readonly List<PointF> _markers = new();

    // ── Animation + FPS ──────────────────────────────────────────────────────
    UITimer  _animTimer = null!;
    int      _fps;
    int      _fpsCount;
    DateTime _fpsEpoch  = DateTime.Now;
    long     _totalFrames;
    DateTime _lastRender = DateTime.Now;

    // ── Status labels ─────────────────────────────────────────────────────────
    readonly Label _lblStatus  = new Label { Text = "Initialising…" };
    readonly Label _lblFps     = new Label { Text = "–" };
    readonly Label _lblFrames  = new Label { Text = "0" };
    readonly Label _lblBackend = new Label { Text = "–" };

    // ── Event log ─────────────────────────────────────────────────────────────
    readonly TextArea _log = new TextArea
    {
        ReadOnly        = true,
        Wrap            = false,
        BackgroundColor = Colors.Black,
        TextColor       = Colors.LimeGreen,
        Font            = Fonts.Monospace(10),
    };

    public MainForm()
    {
        Title       = "Veldrid Surface Test — Eto.Forms GTK3";
        ClientSize  = new Size(800, 760);
        Resizable   = true;
        Maximizable = true;

        // ── Interactive Drawable viewport ────────────────────────────────────
        // Renders the Eto software wireframe cube and handles all mouse input.
        // The resulting camera angles are also sent to the Veldrid renderer below.
        var viewport = new Drawable { BackgroundColor = new Color(0.05f, 0.05f, 0.10f) };
        viewport.Paint      += ViewportPaint;
        viewport.MouseMove  += ViewportMouseMove;
        viewport.MouseDown  += ViewportMouseDown;
        viewport.MouseUp    += ViewportMouseUp;
        viewport.MouseWheel += ViewportMouseWheel;

        // ── VulkanSurface — Veldrid renders into this ────────────────────────
        var surface = new VulkanSurface();
        surface.SurfaceCreated   += (s, e) => OnSurfaceCreated(surface);
        surface.SurfaceDestroyed += (s, e) => OnSurfaceDestroyed();
        surface.Render           += (s, e) => OnVulkanRender(surface);

        // ── Backend selector ─────────────────────────────────────────────────
        // On GTK / Linux the Vulkan backend is fully operational.
        // The other entries document how each backend would be initialised on its
        // respective platform — see the class-level XML comments above.
        var backendDrop = new DropDown();
        backendDrop.Items.Add("Vulkan  (Linux / GTK  — wl_surface or XWindow handle)");
        backendDrop.Items.Add("OpenGL  (Linux / GTK  — GLX or EGL context required)");
        backendDrop.Items.Add("Direct3D 11  (Windows — HWND from WinForms control)");
        backendDrop.Items.Add("Metal  (macOS — NSView handle from Eto.Mac)");
        backendDrop.SelectedIndex = 0;
        backendDrop.SelectedIndexChanged += (_, _) =>
        {
            var idx = backendDrop.SelectedIndex;
            if (idx != 0)
                Log($"Backend #{idx} is shown for reference only on this platform. "
                  + "Vulkan is the active backend for GTK/Linux.");
        };

        // ── Animation timer ──────────────────────────────────────────────────
        _animTimer = new UITimer { Interval = 1.0 / 60.0 };
        _animTimer.Elapsed += (_, _) =>
        {
            if (!_paused)
            {
                _angle += 0.02f;
                if (_angle >= MathF.Tau) _angle -= MathF.Tau;
            }

            _fpsCount++;
            var elapsed = (DateTime.Now - _fpsEpoch).TotalSeconds;
            if (elapsed >= 1.0)
            {
                _fps    = (int)(_fpsCount / elapsed);
                _fpsCount = 0;
                _fpsEpoch = DateTime.Now;
                _lblFps.Text = _fps.ToString();
            }

            viewport.Invalidate();   // repaint Eto wireframe
            surface.Invalidate();    // trigger VulkanSurface.Render → Veldrid frame
        };

        Load   += (_, _) => _animTimer.Start();
        UnLoad += (_, _) =>
        {
            _animTimer.Stop();
            _renderer?.Dispose();
            _gd?.Dispose();
        };

        // ── Button row ───────────────────────────────────────────────────────
        var btnToggleAnim = new Button { Text = "Pause Animation" };
        btnToggleAnim.Click += (_, _) =>
        {
            _paused = !_paused;
            btnToggleAnim.Text = _paused ? "Resume Animation" : "Pause Animation";
        };

        var btnClearMarkers = new Button { Text = "Clear Markers" };
        btnClearMarkers.Click += (_, _) => { _markers.Clear(); viewport.Invalidate(); };

        var btnClearLog = new Button { Text = "Clear Log" };
        btnClearLog.Click += (_, _) => _log.Text = string.Empty;

        // ── Layout ───────────────────────────────────────────────────────────
        Content = new TableLayout
        {
            Padding = new Padding(6),
            Spacing = new Size(0, 4),
            Rows =
            {
                // Interactive Drawable — fills available space.
                new TableRow(new GroupBox
                {
                    Text    = "Camera Input (Eto Drawable — software wireframe)",
                    Content = viewport,
                }) { ScaleHeight = true },

                // VulkanSurface — Veldrid renders a GPU solid cube here.
                new TableRow(new GroupBox
                {
                    Text    = "Veldrid GPU Rendering (solid cube — same camera as above)",
                    Content = surface,
                    Height  = 220,
                }),

                // Status / backend row
                BuildStatusRow(backendDrop),

                // Event log
                new TableRow(new TableLayout
                {
                    Rows =
                    {
                        new TableRow(new Label { Text = "Event log:", Font = Fonts.Sans(9, FontStyle.Bold) }),
                        new TableRow(new Scrollable { Content = _log, Height = 110 }),
                    }
                }),

                // Button row
                new TableRow(new TableLayout(
                    new TableRow(btnToggleAnim, btnClearMarkers, btnClearLog, null)
                ) { Spacing = new Size(6, 0) }),
            }
        };

        Log("Waiting for VulkanSurface.SurfaceCreated …");
        Log("LMB drag: orbit  |  Scroll: zoom  |  RMB click: place marker");
    }

    // ── VulkanSurface lifecycle ───────────────────────────────────────────────

    void OnSurfaceCreated(VulkanSurface surface)
    {
        var info = surface.GetSurfaceInfo();
        if (info == null)
        {
            Log("SurfaceCreated — GetSurfaceInfo() returned null");
            _lblStatus.Text = "Error: no surface info";
            return;
        }

        Log($"SurfaceCreated  type={info.SurfaceType}  size={surface.Size}");

        try
        {
            // ── Build the SwapchainSource from the display-server handles ────────
            //
            // Wayland: SwapchainSource.CreateWayland(wl_display, wl_surface)
            // X11:     SwapchainSource.CreateXlib(XDisplay*, XWindow)
            //
            // Windows equivalent (D3D11 / OpenGL / Vulkan):
            //   SwapchainSource.CreateWin32(hwnd, hinstance)
            //
            // macOS equivalent (Metal / MoltenVK):
            //   SwapchainSource.CreateNSWindow(nsWindowHandle)
            //   SwapchainSource.CreateNSView(nsViewHandle)
            SwapchainSource source;
            if (info.SurfaceType == VulkanSurfaceType.Wayland)
            {
                source = SwapchainSource.CreateWayland(info.WlDisplay, info.WlSurface);
                Log($"  wl_display = {FormatPtr(info.WlDisplay)}");
                Log($"  wl_surface = {FormatPtr(info.WlSurface)}");
                if (info.PreferredPhysicalDeviceDrmNode is string drm)
                    Log($"  drm_node   = {drm}");
            }
            else
            {
                source = SwapchainSource.CreateXlib(info.XDisplay, (nint)info.XWindow);
                Log($"  XDisplay   = {FormatPtr(info.XDisplay)}");
                Log($"  XWindow    = 0x{info.XWindow:X}");
            }

            var swapchainDesc = new SwapchainDescription(
                source,
                (uint)Math.Max(1, surface.Width),
                (uint)Math.Max(1, surface.Height),
                Veldrid.PixelFormat.R32_Float,   // depth buffer (D32 float)
                syncToVerticalBlank: false);

            // ── Create the Veldrid GraphicsDevice ────────────────────────────
            //
            // GraphicsDevice.CreateVulkan  — Vulkan (Linux, Windows, macOS via MoltenVK)
            // GraphicsDevice.CreateD3D11   — Direct3D 11 (Windows only)
            // GraphicsDevice.CreateMetal   — Metal (macOS only)
            // GraphicsDevice.CreateOpenGL  — OpenGL (all platforms, needs a GL context)
            //
            // PreferStandardClipSpaceYDirection lets us write shaders in OpenGL
            // convention (Y-up) and have Veldrid handle the backend-specific flip.
            // PreferDepthRangeZeroToOne gives [0,1] depth range on all backends.
            var options = new GraphicsDeviceOptions
            {
                Debug                           = false,
                SwapchainDepthFormat            = Veldrid.PixelFormat.R32_Float,
                SyncToVerticalBlank             = false,
                ResourceBindingModel            = ResourceBindingModel.Improved,
                PreferDepthRangeZeroToOne       = true,
                PreferStandardClipSpaceYDirection = true,
            };

            _gd = GraphicsDevice.CreateVulkan(options, swapchainDesc);
            Log($"GraphicsDevice created  backend={_gd.BackendType}  device={_gd.DeviceName}");

            _renderer    = new VeldridRenderer(_gd,
                (uint)Math.Max(1, surface.Width),
                (uint)Math.Max(1, surface.Height));
            _lastRender  = DateTime.Now;

            _lblBackend.Text = _gd.BackendType.ToString();
            _lblStatus.Text  = $"Rendering ({_gd.DeviceName})";
        }
        catch (Exception ex)
        {
            Log($"Veldrid initialisation failed: {ex.Message}");
            _lblStatus.Text  = $"Error: {ex.Message}";
            _lblBackend.Text = "–";
        }
    }

    void OnVulkanRender(VulkanSurface surface)
    {
        if (_renderer == null || _gd == null) return;

        // Handle swapchain resize (VeldridRenderer.Resize is a no-op if unchanged).
        _renderer.Resize(
            (uint)Math.Max(1, surface.Width),
            (uint)Math.Max(1, surface.Height));

        // Compute the combined camera angles — same formula as the Eto wireframe
        // so both cubes always display exactly the same orientation.
        float yaw   = _userYaw   + _angle * 0.6f;
        float pitch = _userPitch + _angle * 0.35f;

        _totalFrames++;
        _lblFrames.Text = _totalFrames.ToString();

        try
        {
            _renderer.RenderFrame(yaw, pitch, _zoom);
        }
        catch (Exception ex)
        {
            Log($"RenderFrame error: {ex.Message}");
        }
    }

    void OnSurfaceDestroyed()
    {
        _renderer?.Dispose();
        _renderer = null;
        _gd?.Dispose();
        _gd = null;

        Log("SurfaceDestroyed — Veldrid resources disposed.");
        _lblStatus.Text  = "Surface destroyed";
        _lblBackend.Text = "–";
    }

    // ── Drawable: painting ────────────────────────────────────────────────────

    void ViewportPaint(object? sender, PaintEventArgs e)
    {
        var g   = e.Graphics;
        var ctl = (Drawable)sender!;
        int w   = ctl.Width;
        int h   = ctl.Height;

        // Background
        g.FillRectangle(new Color(0.05f, 0.05f, 0.10f), 0, 0, w, h);

        // Subtle grid
        using var gridPen = new Pen(new Color(1f, 1f, 1f, 0.07f));
        for (int x = 0; x <= w; x += 40) g.DrawLine(gridPen, x, 0, x, h);
        for (int y = 0; y <= h; y += 40) g.DrawLine(gridPen, 0, y, w, y);

        // Wireframe cube (same camera angles as Veldrid below)
        float yaw   = _userYaw   + _angle * 0.6f;
        float pitch = _userPitch + _angle * 0.35f;
        DrawWireframeCube(g, w / 2f, h / 2f, w, h, yaw, pitch);

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
            using var crossPen = new Pen(new Color(1f, 1f, 1f, 0.35f));
            g.DrawLine(crossPen, _mousePos.X, 0, _mousePos.X, h);
            g.DrawLine(crossPen, 0, _mousePos.Y, w, _mousePos.Y);
        }

        // HUD
        var font = Fonts.Monospace(10);
        DrawHud(g, font, 8,  8, $"Eto software renderer  —  drives the Veldrid cube below");
        DrawHud(g, font, 8, 26, "LMB drag: orbit  |  Scroll: zoom  |  RMB click: marker");
        DrawHud(g, font, 8, 44, $"Yaw: {_userYaw + _angle * 0.6f:F2}  Pitch: {_userPitch + _angle * 0.35f:F2}  Zoom: {_zoom:F2}");
        DrawHud(g, font, 8, 62, $"Mouse: ({_mousePos.X:F0}, {_mousePos.Y:F0})  Markers: {_markers.Count}");
    }

    static void DrawHud(Graphics g, Font font, float x, float y, string text)
    {
        var sz = g.MeasureString(font, text);
        g.FillRectangle(new Color(0f, 0f, 0f, 0.60f), x - 2, y - 1, sz.Width + 4, sz.Height + 2);
        g.DrawText(font, Colors.White, x, y, text);
    }

    // ── Drawable: wireframe cube ──────────────────────────────────────────────

    static void DrawWireframeCube(
        Graphics g, float cx, float cy, int w, int h,
        float yaw, float pitch)
    {
        // 8 unit-cube corners
        float[][] verts =
        {
            new[] {-1f,-1f,-1f}, new[] {+1f,-1f,-1f},
            new[] {+1f,+1f,-1f}, new[] {-1f,+1f,-1f},
            new[] {-1f,-1f,+1f}, new[] {+1f,-1f,+1f},
            new[] {+1f,+1f,+1f}, new[] {-1f,+1f,+1f},
        };

        // 12 edges
        (int a, int b)[] edges =
        {
            (0,1),(1,2),(2,3),(3,0),
            (4,5),(5,6),(6,7),(7,4),
            (0,4),(1,5),(2,6),(3,7),
        };

        float cosY = MathF.Cos(yaw),   sinY = MathF.Sin(yaw);
        float cosP = MathF.Cos(pitch), sinP = MathF.Sin(pitch);
        float fov  = Math.Min(w, h) / 3.5f;   // fixed FOV scale (zoom handled in Veldrid)

        var proj = new PointF[8];
        for (int i = 0; i < 8; i++)
        {
            float x = verts[i][0], y = verts[i][1], z = verts[i][2];
            float xR = x * cosY - z * sinY; float zR = x * sinY + z * cosY; x = xR; z = zR;
            float yR = y * cosP - z * sinP; float zR2 = y * sinP + z * cosP; y = yR; z = zR2;
            float pz = z + 4f;
            proj[i] = new PointF(cx + x * fov / pz, cy + y * fov / pz);
        }

        float hue0 = (yaw * 40f) % 360f;
        if (hue0 < 0) hue0 += 360f;
        for (int i = 0; i < edges.Length; i++)
        {
            var (a, b) = edges[i];
            using var pen = new Pen(HsvToColor((hue0 + i * 22.5f) % 360f, 0.85f, 1f), 2f);
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

    // ── Drawable: mouse handlers ──────────────────────────────────────────────

    void ViewportMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            float dx = e.Location.X - _lastDrag.X;
            float dy = e.Location.Y - _lastDrag.Y;
            _userYaw   += dx * 0.01f;
            _userPitch  = Math.Clamp(_userPitch + dy * 0.01f, -MathF.PI / 2f, MathF.PI / 2f);
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
        _zoom = Math.Clamp(_zoom - e.Delta.Height * 0.3f, 0.5f, 12f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    Control BuildStatusRow(DropDown backendDrop) =>
        new TableLayout(
            new TableRow(
                new TableCell(new Label { Text = "Backend:", VerticalAlignment = VerticalAlignment.Center }),
                new TableCell(backendDrop),
                new TableCell(new Label { Text = "  Status:", VerticalAlignment = VerticalAlignment.Center }),
                new TableCell(_lblStatus, true),
                new TableCell(new Label { Text = "  FPS:", VerticalAlignment = VerticalAlignment.Center }),
                new TableCell(_lblFps),
                new TableCell(new Label { Text = "  Frames:", VerticalAlignment = VerticalAlignment.Center }),
                new TableCell(_lblFrames),
                new TableCell(new Label { Text = "  Backend:", VerticalAlignment = VerticalAlignment.Center }),
                new TableCell(_lblBackend)
            )
        ) { Spacing = new Size(4, 0) };

    void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Application.Instance.AsyncInvoke(() => _log.Text += line + "\n");
    }

    static string FormatPtr(IntPtr ptr) =>
        ptr == IntPtr.Zero ? "(zero)" : $"0x{ptr.ToInt64():X}";
}
