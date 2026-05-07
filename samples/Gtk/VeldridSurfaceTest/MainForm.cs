using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using Veldrid;
using Veldrid.OpenGL;

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
/// Backend selection
/// ─────────────────
///  The dropdown lets you switch between the Veldrid-supported backends at
///  runtime.  On GTK/Linux the following are available:
///
///  Vulkan  — Preferred.  Uses the wl_surface (Wayland) or XWindow (X11)
///    handle that VulkanSurface exposes.  Requires libvulkan.so.1 and a
///    compatible GPU driver (Mesa radv/anv or NVIDIA 500+).
///
///  OpenGL  — Fallback.  Also uses the native window handle from VulkanSurface:
///    • X11: GLX context (libGL.so.1) bound to the XWindow.
///    • Wayland: EGL context (libEGL.so.1 + libwayland-egl.so.1) bound to
///      a wl_egl_window wrapping the wl_surface.
///    Requires OpenGL 3.3 Core or later (Mesa or proprietary driver).
///
///  Direct3D 11 (Windows only)
///    Retrieve the HWND from an Eto WinForms Drawable, create
///    SwapchainSource.CreateWin32(hwnd, hinstance), and call
///    GraphicsDevice.CreateD3D11(options, swapchainDesc).
///
///  Metal (macOS only)
///    Retrieve the NSView handle from an Eto.Mac DrawableHandler.NativeControl,
///    create SwapchainSource.CreateNSWindow(nsWindowHandle), and call
///    GraphicsDevice.CreateMetal(options, swapchainDesc).
///
/// Automatic fallback
/// ──────────────────
///  If the selected backend fails (e.g. Vulkan not installed), the code
///  automatically tries OpenGL.  A message is logged explaining the situation.
/// </summary>
public class MainForm : Form
{
    const int DiagnosticsHeartbeatIntervalFrames = 180;
    const int MinSurfaceInitDim = 4;
    const int SurfaceViewportHeight = 220;

    // ── Veldrid objects ───────────────────────────────────────────────────────
    GraphicsDevice?   _gd;
    VeldridRenderer?  _renderer;

    // ── Saved surface state for backend hot-switching ─────────────────────────
    IVulkanSurfaceInfo? _surfaceInfo;    // last SurfaceCreated info
    VulkanSurface?      _vulkanSurface;  // the VulkanSurface widget

    // ── Backend selection ─────────────────────────────────────────────────────
    // Dropdown index 0 = Vulkan, 1 = OpenGL, 2+ = reference-only on Linux.
    GraphicsBackend _desiredBackend = GraphicsBackend.Vulkan;
    DropDown        _backendDrop    = null!;

    // ── OpenGL context handles (for cleanup on teardown) ──────────────────────
    // GLX path (X11):
    IntPtr _glxCtx;              // GLX context pointer
    // EGL path (Wayland or X11-EGL):
    IntPtr _eglDpy;              // EGL display
    IntPtr _eglCtx;              // EGL context
    IntPtr _eglSurf;             // EGL surface
    IntPtr _wlEglWindow;         // wl_egl_window* (Wayland only; zero on X11)

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
    uint     _swapchainW;
    uint     _swapchainH;
    bool     _swapchainCanResize;
    readonly bool _diagnosticsEnabled = DiagnosticsEnabled();
    UITimer? _diagnosticsTimer;
    string? _lastSpatialSnapshot;
    string? _lastInitDeferralSnapshot;

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
        // The VulkanSurface exposes native display/window handles (wl_surface
        // or XWindow) which we use for BOTH the Vulkan and OpenGL backends.
        var surface = new VulkanSurface
        {
            Size = new Size(320, SurfaceViewportHeight),
        };
        _vulkanSurface = surface;
        surface.SurfaceCreated   += (_, _) => OnSurfaceCreated(surface);
        surface.SurfaceDestroyed += (_, _) => OnSurfaceDestroyed();
        surface.Render           += (_, _) => OnVulkanRender(surface);
        surface.SizeChanged      += (_, _) =>
        {
            LogSurfaceSpatialSnapshot("Surface.SizeChanged");
            TryInitializeVeldridWhenReady("Surface.SizeChanged");
        };

        // ── Backend selector ─────────────────────────────────────────────────
        _backendDrop = new DropDown();
        _backendDrop.Items.Add("Vulkan   (Linux — wl_surface or XWindow → Vulkan swapchain)");
        _backendDrop.Items.Add("OpenGL   (Linux — GLX on X11  |  EGL on Wayland)");
        _backendDrop.Items.Add("Direct3D 11   (Windows only — HWND from WinForms control)");
        _backendDrop.Items.Add("Metal   (macOS only — NSView handle from Eto.Mac)");
        _backendDrop.SelectedIndex = 0;
        _backendDrop.SelectedIndexChanged += OnBackendDropChanged;

        // ── Animation timer ──────────────────────────────────────────────────
        _animTimer = new UITimer { Interval = 1.0 / 60.0 };
        _animTimer.Elapsed += (_, _) =>
        {
            if (_gd == null)
                TryInitializeVeldridWhenReady("Timer");

            if (!_paused)
            {
                _angle += 0.02f;
                if (_angle >= MathF.Tau) _angle -= MathF.Tau;
            }

            _fpsCount++;
            var elapsed = (DateTime.Now - _fpsEpoch).TotalSeconds;
            if (elapsed >= 1.0)
            {
                _fps      = (int)(_fpsCount / elapsed);
                _fpsCount = 0;
                _fpsEpoch = DateTime.Now;
                _lblFps.Text = _fps.ToString();
            }

            viewport.Invalidate();   // repaint Eto wireframe
            surface.Invalidate();    // trigger VulkanSurface.Render → Veldrid frame
        };
        _animTimer.Start();

        Closing += (_, _) => { _animTimer.Stop(); TeardownVeldrid(); };
        Closing += (_, _) => _diagnosticsTimer?.Stop();
        Load += (_, _) => Application.Instance.AsyncInvoke(() =>
        {
            if (_diagnosticsEnabled)
                LogSurfaceSpatialSnapshot("Load");
            TryInitializeVeldridWhenReady("Load");
        });

        // ── Buttons ──────────────────────────────────────────────────────────
        var btnToggleAnim  = new Button { Text = "Pause" };
        var btnClearMarkers = new Button { Text = "Clear Markers" };
        var btnClearLog    = new Button { Text = "Clear Log" };

        btnToggleAnim.Click += (_, _) =>
        {
            _paused = !_paused;
            btnToggleAnim.Text = _paused ? "Resume" : "Pause";
        };
        btnClearMarkers.Click += (_, _) => { _markers.Clear(); };
        btnClearLog.Click     += (_, _) => { _log.Text = ""; };

        // ── Layout ───────────────────────────────────────────────────────────
        Content = new TableLayout
        {
            Padding = new Padding(8),
            Spacing = new Size(0, 6),
            Rows =
            {
                // Top: Eto wireframe viewport (interactive camera input)
                new TableRow(new TableLayout
                {
                    Rows =
                    {
                        new TableRow(new Label
                        {
                            Text = "Camera Input  (Eto software renderer)",
                            Font = Fonts.Sans(9, FontStyle.Bold),
                        }),
                        new TableRow(viewport) { ScaleHeight = true },
                    }
                }) { ScaleHeight = true },

                // Bottom: Veldrid GPU rendering surface
                new TableRow(new TableLayout
                {
                    Rows =
                    {
                        new TableRow(new Label
                        {
                            Text = "Veldrid GPU Rendering  (VulkanSurface)",
                            Font = Fonts.Sans(9, FontStyle.Bold),
                        }),
                        new TableRow(new GroupBox
                        {
                            Text = "Veldrid surface",
                            Content = surface,
                            Height = SurfaceViewportHeight,
                        }) { ScaleHeight = true },
                    }
                }) { ScaleHeight = true },

                // Status / backend row
                BuildStatusRow(_backendDrop),

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
        Log("Use the Backend dropdown to switch between Vulkan and OpenGL.");
        if (_diagnosticsEnabled)
        {
            Log("Diagnostics enabled (ETO_VELDRID_DIAGNOSTICS=1) — logging spatial snapshots.");
            StartDiagnosticsTimer();
            LogSurfaceSpatialSnapshot("Startup");
        }
    }

    // ── Backend hot-switching ─────────────────────────────────────────────────

    void OnBackendDropChanged(object? sender, EventArgs e)
    {
        var idx = _backendDrop.SelectedIndex;

        // Items 2+ (D3D11, Metal) are reference-only on Linux.
        if (idx >= 2)
        {
            Log($"Backend #{idx} is platform-specific — see MainForm.cs comments for details.");
            _backendDrop.SelectedIndex = _desiredBackend == GraphicsBackend.Vulkan ? 0 : 1;
            return;
        }

        var newBackend = idx == 0 ? GraphicsBackend.Vulkan : GraphicsBackend.OpenGL;
        if (newBackend == _desiredBackend) return;
        _desiredBackend = newBackend;

        Log($"Switching backend → {_desiredBackend} …");

        // Reinitialise from the saved surface info (if the surface has already
        // been created).  If it hasn't fired yet, InitializeVeldrid will be
        // called from OnSurfaceCreated when the widget is realised.
        if (_surfaceInfo != null && _vulkanSurface != null)
        {
            TeardownVeldrid();
            TryInitializeVeldridWhenReady("BackendChanged");
        }
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

        int scale = (int)surface.BackingScaleFactor;
        Log($"SurfaceCreated  type={info.SurfaceType}  size={surface.Size}  scale={scale}");
        LogSurfaceSpatialSnapshot("SurfaceCreated");
        _surfaceInfo = info;

        TryInitializeVeldridWhenReady("SurfaceCreated");
    }

    void TryInitializeVeldridWhenReady(string reason)
    {
        if (_gd != null || _surfaceInfo == null || _vulkanSurface == null)
            return;

        int scale = (int)_vulkanSurface.BackingScaleFactor;
        int w = _vulkanSurface.Width * scale;
        int h = _vulkanSurface.Height * scale;
        if (w < MinSurfaceInitDim || h < MinSurfaceInitDim)
        {
            var snapshot = $"{_vulkanSurface.Size}:{scale}:{w}x{h}";
            if (!string.Equals(snapshot, _lastInitDeferralSnapshot, StringComparison.Ordinal))
            {
                _lastInitDeferralSnapshot = snapshot;
                Log($"Deferring Veldrid init ({reason}) — surface not ready yet: size={_vulkanSurface.Size} scale={scale}");
            }
            return;
        }

        _lastInitDeferralSnapshot = null;
        InitializeVeldrid(_surfaceInfo, _vulkanSurface);
    }

    /// <summary>
    /// Creates the Veldrid <see cref="GraphicsDevice"/> and
    /// <see cref="VeldridRenderer"/> for the desired backend.
    ///
    /// Tries <see cref="_desiredBackend"/> first.  If that fails the code
    /// automatically falls back to <see cref="GraphicsBackend.OpenGL"/> and
    /// updates the dropdown to reflect the change.
    /// </summary>
    void InitializeVeldrid(IVulkanSurfaceInfo info, VulkanSurface surface)
    {
        bool isWayland = info.SurfaceType == VulkanSurfaceType.Wayland;
        // Use physical-pixel dimensions for the swapchain.  On HiDPI displays
        // (scale ≥ 2) the Vulkan buffer must cover the full physical area; the
        // wl_surface_set_buffer_scale hint in VulkanSurfaceHandler already tells
        // the compositor to interpret the buffer at that scale.
        int scale = (int)surface.BackingScaleFactor;
        // On Wayland, compositors (e.g. Mutter/KWin) issue a protocol error (EPROTO)
        // if a VkSwapchainKHR is created with a degenerate extent (height=1 or 0).
        // GTK emits SizeAllocated with h=1 during intermediate layout passes before
        // the widget has been given its final allocation, so SurfaceCreated may fire
        // while the surface has only 1 logical-pixel height.  Using MinSwapchainDim
        // as the floor for the initial creation avoids the protocol error; the first
        // valid SizeAllocated will trigger a proper resize via the warm-up path below.
        const int MinSwapchainDim = MinSurfaceInitDim;
        int w = Math.Max(MinSwapchainDim, surface.Width  * scale);
        int h = Math.Max(MinSwapchainDim, surface.Height * scale);

        // ── Shared GraphicsDeviceOptions ──────────────────────────────────────
        // D24_UNorm_S8_UInt is supported on all backends and gives a 24-bit
        // depth buffer — required by VeldridRenderer.RenderFrame.
        var options = new GraphicsDeviceOptions
        {
            Debug                            = false,
            SwapchainDepthFormat             = Veldrid.PixelFormat.D24_UNorm_S8_UInt,
            SyncToVerticalBlank              = false,
            ResourceBindingModel             = ResourceBindingModel.Improved,
            PreferDepthRangeZeroToOne        = true,
            PreferStandardClipSpaceYDirection = true,
        };

        // Try the desired backend; if it fails, fall back to OpenGL.
        bool created = TryCreateDevice(_desiredBackend, info, options, w, h);
        if (!created && _desiredBackend != GraphicsBackend.OpenGL)
        {
            Log("  → falling back to OpenGL …");
            _desiredBackend = GraphicsBackend.OpenGL;
            Application.Instance.AsyncInvoke(() => _backendDrop.SelectedIndex = 1);
            created = TryCreateDevice(GraphicsBackend.OpenGL, info, options, w, h);
        }

        if (!created || _gd == null)
        {
            Log("All backends failed — no GPU rendering available.");
            _lblStatus.Text  = "No GPU backend available";
            _lblBackend.Text = "–";
            return;
        }

        try
        {
            _renderer = new VeldridRenderer(_gd, (uint)w, (uint)h);
            _lastRender = DateTime.Now;
            _swapchainW = (uint)w;
            _swapchainH = (uint)h;
            _swapchainCanResize = false; // avoid immediate recreate churn before first present

            _lblBackend.Text = _gd.BackendType.ToString();
            _lblStatus.Text  = $"Rendering ({_gd.DeviceName})";
            Log($"Renderer ready  backend={_gd.BackendType}  device={_gd.DeviceName}");
        }
        catch (Exception ex)
        {
            Log($"VeldridRenderer creation failed: {ex.Message}");
            _lblStatus.Text  = $"Renderer error: {ex.Message}";
            _lblBackend.Text = "–";
            _gd?.Dispose();
            _gd = null;
        }
    }

    /// <summary>
    /// Attempts to create a <see cref="GraphicsDevice"/> for the given
    /// <paramref name="backend"/>.  Returns <see langword="false"/> on any
    /// failure, leaving <see cref="_gd"/> unchanged.
    /// </summary>
    bool TryCreateDevice(
        GraphicsBackend backend,
        IVulkanSurfaceInfo info,
        GraphicsDeviceOptions options,
        int w, int h)
    {
        bool isWayland = info.SurfaceType == VulkanSurfaceType.Wayland;
        try
        {
            switch (backend)
            {
                // ── Vulkan ────────────────────────────────────────────────────
                case GraphicsBackend.Vulkan:
                {
                    // Build the platform-specific swapchain source from the
                    // native window handles that VulkanSurface exposes.
                    SwapchainSource source;
                    if (isWayland)
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
                        source, (uint)w, (uint)h,
                        Veldrid.PixelFormat.D24_UNorm_S8_UInt,
                        syncToVerticalBlank: false);

                    Log("  Creating Vulkan device …");
                    _gd = GraphicsDevice.CreateVulkan(options, swapchainDesc);
                    Log($"  GraphicsDevice created  backend={_gd.BackendType}");
                    return true;
                }

                // ── OpenGL ────────────────────────────────────────────────────
                // The VulkanSurface's native window handle is reused here — for
                // OpenGL we bind a GL context directly to the same X11 window or
                // Wayland wl_surface instead of creating a Vulkan swapchain.
                case GraphicsBackend.OpenGL:
                {
                    OpenGLPlatformInfo? platformInfo;

                    if (isWayland)
                    {
                        // Wayland path: EGL + wl_egl_window
                        Log("  Creating EGL context (Wayland) …");
                        platformInfo = OpenGLHelper.TryCreateEglPlatformInfo(
                            xDisplay:  IntPtr.Zero,  xWindow:  0,
                            wlDisplay: info.WlDisplay, wlSurface: info.WlSurface,
                            width: w, height: h,
                            outEglDisplay:  out _eglDpy,
                            outEglContext:  out _eglCtx,
                            outEglSurface:  out _eglSurf,
                            outWlEglWindow: out _wlEglWindow);

                        if (platformInfo == null)
                        {
                            Log("  EGL context creation failed — is libEGL.so.1 / libwayland-egl.so.1 installed?");
                            return false;
                        }
                        Log($"  EGL ready  display={FormatPtr(_eglDpy)}");
                    }
                    else
                    {
                        // X11 path: try GLX first, then fall back to EGL.
                        Log("  Creating GLX context (X11) …");
                        platformInfo = OpenGLHelper.TryCreateGlxPlatformInfo(
                            info.XDisplay, info.XWindow, out _glxCtx);

                        if (platformInfo == null)
                        {
                            Log("  GLX failed — trying EGL on X11 …");
                            platformInfo = OpenGLHelper.TryCreateEglPlatformInfo(
                                xDisplay:  info.XDisplay, xWindow: info.XWindow,
                                wlDisplay: IntPtr.Zero,   wlSurface: IntPtr.Zero,
                                width: w, height: h,
                                outEglDisplay:  out _eglDpy,
                                outEglContext:  out _eglCtx,
                                outEglSurface:  out _eglSurf,
                                outWlEglWindow: out _wlEglWindow);

                            if (platformInfo == null)
                            {
                                Log("  EGL on X11 also failed.");
                                return false;
                            }
                            Log($"  EGL (X11) ready  display={FormatPtr(_eglDpy)}");
                        }
                        else
                        {
                            Log($"  GLX ready  context={FormatPtr(_glxCtx)}");
                        }
                    }

                    Log("  Creating OpenGL device …");
                    _gd = GraphicsDevice.CreateOpenGL(options, platformInfo, (uint)w, (uint)h);
                    Log($"  GraphicsDevice created  backend={_gd.BackendType}");
                    return true;
                }

                default:
                    Log($"  Backend {backend} is not supported on this platform.");
                    return false;
            }
        }
        catch (Exception ex)
        {
            // Unwrap TypeInitializationException from missing Vulkan loader.
            var root = ex;
            while (root.InnerException != null) root = root.InnerException;
            Log($"  {backend} init failed: {root.Message}");
            if (root != ex) Log($"    (caused by: {ex.GetType().Name})");
            return false;
        }
    }

    void OnVulkanRender(VulkanSurface surface)
    {
        if (_renderer == null || _gd == null) return;

        // Use physical-pixel dimensions, matching the swapchain created in InitializeVeldrid.
        int scale = (int)surface.BackingScaleFactor;
        int w = surface.Width  * scale;
        int h = surface.Height * scale;

        // GTK emits SizeAllocated with intermediate allocations (e.g. h=1) before the
        // widget has its final size.  Passing degenerate extents to vkCreateSwapchainKHR
        // causes a Wayland protocol error (EPROTO=71) on compositors such as Mutter.
        // Skip the frame entirely; the next SizeAllocated with the real allocation will
        // trigger a new render via the handler's AsyncInvoke path.
        const int MinRenderDim = 4;
        if (w < MinRenderDim || h < MinRenderDim) return;

        // For Wayland EGL: the wl_egl_window must be resized before the frame
        // so the compositor knows the new dimensions before we swap.
        if (_wlEglWindow != IntPtr.Zero)
            OpenGLHelper.ResizeWlEglWindow(_wlEglWindow, w, h);

        uint targetW = (uint)w;
        uint targetH = (uint)h;

        // Handle swapchain resize only after at least one successful present.
        // On some Wayland setups, immediately recreating the Vulkan swapchain
        // after device creation can trigger a Veldrid internal crash.
        if (_swapchainCanResize && (targetW != _swapchainW || targetH != _swapchainH))
        {
            try
            {
                _renderer.Resize(targetW, targetH);
                _swapchainW = targetW;
                _swapchainH = targetH;
            }
            catch (Exception ex)
            {
                Log($"ResizeMainWindow error: {ex.Message}");
                return;
            }
        }

        // Compute the combined camera angles — same formula as the Eto wireframe
        // so both cubes always display exactly the same orientation.
        float yaw   = _userYaw   + _angle * 0.6f;
        float pitch = _userPitch + _angle * 0.35f;

        _totalFrames++;
        _lblFrames.Text = _totalFrames.ToString();
        if (_diagnosticsEnabled && (_totalFrames % DiagnosticsHeartbeatIntervalFrames) == 0)
            Log($"Render heartbeat  backend={_gd.BackendType}  frames={_totalFrames}  surface={surface.Size}  scale={(int)surface.BackingScaleFactor}");

        try
        {
            _renderer.RenderFrame(yaw, pitch, _zoom);
            _swapchainCanResize = true;
        }
        catch (Exception ex)
        {
            Log($"RenderFrame error: {ex.Message}");
        }
    }

    void OnSurfaceDestroyed()
    {
        LogSurfaceSpatialSnapshot("SurfaceDestroyed");
        TeardownVeldrid();
        Log("SurfaceDestroyed — Veldrid resources disposed.");
        _lblStatus.Text  = "Surface destroyed";
        _lblBackend.Text = "–";
    }

    /// <summary>
    /// Disposes the Veldrid device, renderer, and any GL context handles.
    /// Safe to call multiple times or before the device is created.
    /// </summary>
    void TeardownVeldrid()
    {
        _renderer?.Dispose();
        _renderer = null;
        _swapchainCanResize = false;
        _swapchainW = 0;
        _swapchainH = 0;

        try { _gd?.WaitForIdle(); } catch { }
        _gd?.Dispose();
        _gd = null;

        // GLX cleanup (X11)
        if (_glxCtx != IntPtr.Zero && _surfaceInfo != null)
        {
            OpenGLHelper.DestroyGlxContext(_surfaceInfo.XDisplay, _glxCtx);
            _glxCtx = IntPtr.Zero;
        }

        // EGL cleanup (Wayland or X11-EGL)
        if (_eglDpy != IntPtr.Zero)
        {
            OpenGLHelper.DestroyEglContext(_eglDpy, _eglCtx, _eglSurf, _wlEglWindow);
            _eglDpy       = IntPtr.Zero;
            _eglCtx       = IntPtr.Zero;
            _eglSurf      = IntPtr.Zero;
            _wlEglWindow  = IntPtr.Zero;
        }
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
                new TableCell(new Label { Text = "  Active:", VerticalAlignment = VerticalAlignment.Center }),
                new TableCell(_lblBackend)
            )
        ) { Spacing = new Size(4, 0) };

    void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Application.Instance.AsyncInvoke(() => _log.Text += line + "\n");
    }

    static bool DiagnosticsEnabled()
    {
        var value = Environment.GetEnvironmentVariable("ETO_VELDRID_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value == "1"
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    void StartDiagnosticsTimer()
    {
        if (_diagnosticsTimer != null)
            return;
        _diagnosticsTimer = new UITimer { Interval = 1.0 };
        _diagnosticsTimer.Elapsed += (_, _) => LogSurfaceSpatialSnapshot("Tick");
        _diagnosticsTimer.Start();
    }

    void LogSurfaceSpatialSnapshot(string reason)
    {
        if (!_diagnosticsEnabled || _vulkanSurface == null)
            return;

        var surface = _vulkanSurface;
        var parent = surface.Parent;
        var root = ParentWindow;

        var snapshot =
            $"surface.bounds={surface.Bounds} size={surface.Size} visible={surface.Visible} enabled={surface.Enabled} " +
            $"parent={parent?.GetType().Name ?? "<null>"} parent.bounds={(parent != null ? parent.Bounds.ToString() : "<null>")} " +
            $"window.client={ClientSize} window.bounds={Bounds} parentWindow={(root != null ? root.Bounds.ToString() : "<null>")} " +
            $"backend={_gd?.BackendType.ToString() ?? "<none>"}";

        if (snapshot == _lastSpatialSnapshot && reason == "Tick")
            return;

        _lastSpatialSnapshot = snapshot;
        Log($"[Diag:{reason}] {snapshot}");
    }

    static string FormatPtr(IntPtr ptr) =>
        ptr == IntPtr.Zero ? "(zero)" : $"0x{ptr.ToInt64():X}";
}
