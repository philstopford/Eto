using System;
using System.Text;
using Eto.Drawing;
using Eto.Forms;

namespace VulkanSurfaceTest;

/// <summary>
/// Main window for the VulkanSurface test application.
///
/// Layout
/// ──────
///  ┌──────────────────────────────────────────────┐
///  │  VulkanSurface  (fills available space)      │
///  ├──────────────────────────────────────────────┤
///  │  [Status grid — surface type, handles, etc.] │
///  ├──────────────────────────────────────────────┤
///  │  Event log (scrollable read-only text area)  │
///  ├──────────────────────────────────────────────┤
///  │  [Request Render]  [Clear Log]               │
///  └──────────────────────────────────────────────┘
///
/// The VulkanSurface itself renders nothing — its purpose here is only to prove
/// that the lifecycle events fire, that the platform returns valid (non-zero)
/// surface handles, and that the Wayland subsurface or X11 window is created
/// correctly.  Hook up an actual Vulkan renderer (e.g. via Veldrid or Silk.NET)
/// to draw into the surface.
/// </summary>
public class MainForm : Form
{
    // ── status labels ────────────────────────────────────────────────────────
    readonly Label _lblSurfaceType    = new Label { Text = "–" };
    readonly Label _lblWlDisplay      = new Label { Text = "–" };
    readonly Label _lblWlSurface      = new Label { Text = "–" };
    readonly Label _lblDrmNode        = new Label { Text = "–" };
    readonly Label _lblXDisplay       = new Label { Text = "–" };
    readonly Label _lblXWindow        = new Label { Text = "–" };
    readonly Label _lblScaleFactor    = new Label { Text = "–" };
    readonly Label _lblFrameCount     = new Label { Text = "0" };

    // ── event log ────────────────────────────────────────────────────────────
    readonly TextArea _log = new TextArea
    {
        ReadOnly  = true,
        Wrap      = false,
        BackgroundColor = Colors.Black,
        TextColor       = Colors.LimeGreen,
        Font            = Fonts.Monospace(10),
    };

    int _frameCount;

    public MainForm()
    {
        Title        = "VulkanSurface Test — Eto.Forms GTK3";
        ClientSize   = new Size(800, 640);
        Resizable    = true;
        Maximizable  = true;

        // ── VulkanSurface ────────────────────────────────────────────────────
        var surface = new VulkanSurface();

        surface.SurfaceCreated   += OnSurfaceCreated;
        surface.SurfaceDestroyed += OnSurfaceDestroyed;
        surface.Render           += OnRender;

        // ── button row ───────────────────────────────────────────────────────
        var btnRequestRender = new Button { Text = "Request Render" };
        btnRequestRender.Click += (_, _) =>
        {
            // The surface fires Render when the GTK widget is resized or when
            // SizeAllocated fires.  We can't call it directly from outside the
            // handler, but we can force a re-layout to trigger it.
            surface.Invalidate();
            Log("Button: Invalidate() called");
        };

        var btnClearLog = new Button { Text = "Clear Log" };
        btnClearLog.Click += (_, _) => _log.Text = string.Empty;

        // ── layout ───────────────────────────────────────────────────────────
        Content = new TableLayout
        {
            Padding = new Padding(6),
            Spacing = new Size(0, 4),
            Rows =
            {
                // The VulkanSurface stretches to fill available space.
                new TableRow(surface) { ScaleHeight = true },

                // Status grid
                BuildStatusPanel(),

                // Event log
                new TableRow(new TableLayout
                {
                    Rows =
                    {
                        new TableRow(new Label { Text = "Event log:", Font = Fonts.Sans(9, FontStyle.Bold) }),
                        new TableRow(new Scrollable { Content = _log, Height = 140 }),
                    }
                }),

                // Buttons
                new TableRow(new TableLayout(
                    new TableRow(
                        btnRequestRender,
                        btnClearLog,
                        null   // spacer
                    )
                ) { Spacing = new Size(6, 0) }),
            }
        };

        Log("Form constructed — waiting for SurfaceCreated ...");
    }

    // ── event handlers ───────────────────────────────────────────────────────

    void OnSurfaceCreated(object? sender, EventArgs e)
    {
        var surface = (VulkanSurface)sender!;
        var info    = surface.GetSurfaceInfo();

        _lblScaleFactor.Text = surface.BackingScaleFactor.ToString("F2");

        if (info == null)
        {
            Log("SurfaceCreated — but GetSurfaceInfo() returned null (handler issue?)");
            _lblSurfaceType.Text = "null";
            return;
        }

        _lblSurfaceType.Text = info.SurfaceType.ToString();

        if (info.SurfaceType == VulkanSurfaceType.Wayland)
        {
            _lblWlDisplay.Text  = FormatPtr(info.WlDisplay);
            _lblWlSurface.Text  = FormatPtr(info.WlSurface);
            _lblDrmNode.Text    = info.PreferredPhysicalDeviceDrmNode ?? "(none)";
            _lblXDisplay.Text   = "N/A";
            _lblXWindow.Text    = "N/A";
        }
        else
        {
            _lblXDisplay.Text  = FormatPtr(info.XDisplay);
            _lblXWindow.Text   = $"0x{info.XWindow:X}";
            _lblWlDisplay.Text = "N/A";
            _lblWlSurface.Text = "N/A";
            _lblDrmNode.Text   = "N/A";
        }

        Log($"SurfaceCreated  type={info.SurfaceType}  size={surface.Size}  scale={surface.BackingScaleFactor:F2}");

        if (info.SurfaceType == VulkanSurfaceType.Wayland)
        {
            Log($"  wl_display  = {FormatPtr(info.WlDisplay)}");
            Log($"  wl_surface  = {FormatPtr(info.WlSurface)}");
            Log($"  drm_node    = {info.PreferredPhysicalDeviceDrmNode ?? "(not available)"}");
        }
        else
        {
            Log($"  XDisplay    = {FormatPtr(info.XDisplay)}");
            Log($"  XWindow     = 0x{info.XWindow:X}");
        }
    }

    void OnRender(object? sender, VulkanRenderEventArgs e)
    {
        _frameCount++;
        _lblFrameCount.Text = _frameCount.ToString();
        // Only log the first few renders and every 100th, to keep the log readable.
        if (_frameCount <= 3 || _frameCount % 100 == 0)
        {
            var surface = (VulkanSurface)sender!;
            Log($"Render #{_frameCount}  size={surface.Size}");
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

        var grid = new TableLayout
        {
            Spacing = new Size(8, 2),
            Rows =
            {
                Row("Surface type:",       _lblSurfaceType),
                Row("wl_display:",         _lblWlDisplay),
                Row("wl_surface:",         _lblWlSurface),
                Row("DRM render node:",    _lblDrmNode),
                Row("XDisplay:",           _lblXDisplay),
                Row("XWindow:",            _lblXWindow),
                Row("Backing scale factor:", _lblScaleFactor),
                Row("Render frame count:", _lblFrameCount),
            }
        };

        return new GroupBox { Text = "Surface info", Content = grid, Padding = new Padding(4) };
    }

    void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Application.Instance.AsyncInvoke(() =>
        {
            _log.Text += line + "\n";
        });
    }

    static string FormatPtr(IntPtr ptr) =>
        ptr == IntPtr.Zero ? "(zero — not created)" : $"0x{ptr.ToInt64():X}";
}
