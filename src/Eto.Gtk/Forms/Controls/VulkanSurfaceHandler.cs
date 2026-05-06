namespace Eto.GtkSharp.Forms.Controls
{
#if GTK3
	/// <summary>
	/// GTK3 handler for <see cref="VulkanSurface"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// On <b>Wayland</b> the handler creates a <c>wl_surface</c> + <c>wl_subsurface</c> pair
	/// whose child surface is positioned to exactly cover the control's allocation inside the
	/// GDK top-level window.  The subsurface is set to <em>desynchronized</em> mode so that
	/// Vulkan can present frames independently of GTK's rendering.
	/// </para>
	/// <para>
	/// On <b>X11</b> the handler simply exposes the X11 window ID (<c>XID</c>) and the Xlib
	/// <c>Display *</c> of the control's underlying GDK window.
	/// </para>
	/// </remarks>
	public class VulkanSurfaceHandler
		: GtkControl<Gtk.DrawingArea, VulkanSurface, VulkanSurface.ICallback>,
		  VulkanSurface.IHandler
	{
		// ── Wayland state ─────────────────────────────────────────────────────────
		IntPtr _wlDisplay;
		IntPtr _wlSurface;      // our created wl_surface*
		IntPtr _wlSubsurface;   // our wl_subsurface*

		// ── X11 state ─────────────────────────────────────────────────────────────
		IntPtr _xDisplay;
		ulong _xWindow;

		// ── shared ───────────────────────────────────────────────────────────────
		bool _isWayland;
		bool _surfaceAlive;
		IVulkanSurfaceInfo _surfaceInfo;

		// ── IHandler ─────────────────────────────────────────────────────────────

		/// <inheritdoc/>
		public float BackingScaleFactor => (float)Control.ScaleFactor;

		/// <inheritdoc/>
		public IVulkanSurfaceInfo GetSurfaceInfo() => _surfaceInfo;

		// ── construction / teardown ───────────────────────────────────────────────

		/// <summary>Creates the underlying <see cref="Gtk.DrawingArea"/> widget.</summary>
		public void Create()
		{
			Control = new Gtk.DrawingArea();
			// Tell GTK not to clear the background on this widget — Vulkan owns the pixels.
			Control.AppPaintable = true;
			// Suppress the default drawn signal so the control doesn't flicker.
			Control.Drawn += HandleDrawn;
		}

		/// <inheritdoc/>
		protected override void Initialize()
		{
			base.Initialize();
			_isWayland = DetectWayland();
		}

		/// <inheritdoc/>
		public override void AttachEvent(string id)
		{
			switch (id)
			{
				case VulkanSurface.SurfaceCreatedEvent:
				case VulkanSurface.SurfaceDestroyedEvent:
				case VulkanSurface.RenderEvent:
					// Fired by the handler directly; no GTK event subscription needed.
					return;
				default:
					base.AttachEvent(id);
					break;
			}
		}

		/// <inheritdoc/>
		public override void OnLoadComplete(EventArgs e)
		{
			base.OnLoadComplete(e);
			if (!Control.IsRealized)
				Control.Realized += HandleRealized;
			else
				OnRealized();
		}

		/// <inheritdoc/>
		public override void OnUnLoad(EventArgs e)
		{
			TearDownSurface();
			base.OnUnLoad(e);
		}

		/// <inheritdoc/>
		protected override void Dispose(bool disposing)
		{
			if (disposing)
				TearDownSurface();
			base.Dispose(disposing);
		}

		// ── GTK signal handlers ───────────────────────────────────────────────────

		[GLib.ConnectBefore]
		void HandleDrawn(object o, Gtk.DrawnArgs args)
		{
			// Suppress GTK's default painting so we don't get a flickering background.
			args.RetVal = true;
		}

		void HandleRealized(object o, EventArgs e)
		{
			Control.Realized -= HandleRealized;
			OnRealized();
		}

		void HandleSizeAllocated(object o, Gtk.SizeAllocatedArgs args)
		{
			if (_isWayland)
				UpdateSubsurfacePosition();

			if (_surfaceAlive)
				Callback.OnRender(Widget, new VulkanRenderEventArgs());
		}

		// ── surface lifecycle ─────────────────────────────────────────────────────

		void OnRealized()
		{
			if (_isWayland)
				InitializeWayland();
			else
				InitializeX11();
		}

		void InitializeWayland()
		{
			var gdkDisplay = Gdk.Display.Default;
			if (gdkDisplay == null)
				return;

			_wlDisplay = NativeMethods.gdk_wayland_display_get_wl_display(gdkDisplay.Handle);
			if (_wlDisplay == IntPtr.Zero)
				return;

			// Ensure we have the Wayland globals (idempotent).
			WaylandGlobals.Initialize(_wlDisplay);

			if (WaylandGlobals.Compositor == IntPtr.Zero || WaylandGlobals.Subcompositor == IntPtr.Zero)
				return;

			// Get the parent wl_surface from the top-level GDK window.
			var topLevel = Control.Toplevel;
			if (topLevel?.Window == null)
				return;

			var parentWlSurface = NativeMethods.gdk_wayland_window_get_wl_surface(topLevel.Window.Handle);
			if (parentWlSurface == IntPtr.Zero)
				return;

			// Create our own wl_surface and make it a subsurface of the GTK window surface.
			_wlSurface = WaylandGlobals.wl_compositor_create_surface(WaylandGlobals.Compositor);
			if (_wlSurface == IntPtr.Zero)
				return;

			_wlSubsurface = WaylandGlobals.wl_subcompositor_get_subsurface(
				WaylandGlobals.Subcompositor, _wlSurface, parentWlSurface);

			if (_wlSubsurface == IntPtr.Zero)
			{
				WaylandGlobals.wl_surface_destroy(_wlSurface);
				_wlSurface = IntPtr.Zero;
				return;
			}

			// Desync: Vulkan presents independently of GTK's render loop.
			WaylandGlobals.wl_subsurface_set_desync(_wlSubsurface);

			// Place above the parent wl_surface so the Vulkan content is visible.
			WaylandGlobals.wl_subsurface_place_above(_wlSubsurface, parentWlSurface);

			// Set initial position and schedule a parent commit via GTK.
			UpdateSubsurfacePosition();
			topLevel.QueueDraw();

			_surfaceInfo = new WaylandSurfaceInfo(
				_wlDisplay, _wlSurface, FindPreferredDrmRenderNode());

			Control.SizeAllocated += HandleSizeAllocated;
			FireSurfaceCreated();
		}

		void InitializeX11()
		{
			var gdkDisplay = Gdk.Display.Default;
			if (gdkDisplay == null)
				return;

			_xDisplay = NativeMethods.gdk_x11_display_get_xdisplay(gdkDisplay.Handle);

			if (Control.Window == null)
				return;

			_xWindow = NativeMethods.gdk_x11_window_get_xid(Control.Window.Handle);
			if (_xWindow == 0)
				return;

			_surfaceInfo = new X11SurfaceInfo(_xDisplay, _xWindow);

			Control.SizeAllocated += HandleSizeAllocated;
			FireSurfaceCreated();
		}

		void FireSurfaceCreated()
		{
			_surfaceAlive = true;
			Callback.OnSurfaceCreated(Widget, EventArgs.Empty);
			Callback.OnRender(Widget, new VulkanRenderEventArgs());
		}

		void TearDownSurface()
		{
			if (_surfaceAlive)
			{
				_surfaceAlive = false;
				Callback.OnSurfaceDestroyed(Widget, EventArgs.Empty);
			}

			_surfaceInfo = null;

			Control.SizeAllocated -= HandleSizeAllocated;

			if (_wlSubsurface != IntPtr.Zero)
			{
				WaylandGlobals.wl_subsurface_destroy(_wlSubsurface);
				_wlSubsurface = IntPtr.Zero;
			}
			if (_wlSurface != IntPtr.Zero)
			{
				WaylandGlobals.wl_surface_destroy(_wlSurface);
				_wlSurface = IntPtr.Zero;
			}
		}

		// ── helpers ───────────────────────────────────────────────────────────────

		void UpdateSubsurfacePosition()
		{
			if (_wlSubsurface == IntPtr.Zero)
				return;

			var topLevel = Control.Toplevel;
			if (topLevel == null)
				return;

			if (Control.TranslateCoordinates(topLevel, 0, 0, out int x, out int y))
			{
				WaylandGlobals.wl_subsurface_set_position(_wlSubsurface, x, y);
				// The position is applied on the parent's next commit (triggered by GTK rendering).
				// Committing our own surface here makes Vulkan presentation work immediately.
				WaylandGlobals.wl_surface_commit(_wlSurface);
			}
		}

		static bool DetectWayland()
		{
			var display = Gdk.Display.Default;
			if (display == null)
				return false;

			// "wayland-0", "wayland-1", etc. on Wayland; ":0", ":1" etc. on X11.
			var name = display.Name ?? string.Empty;
			return name.StartsWith("wayland", StringComparison.OrdinalIgnoreCase)
				|| !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
		}

		/// <summary>
		/// Returns the path to the first available DRM render node, which is a hint for
		/// Vulkan physical-device selection on PRIME / hybrid-graphics systems.
		/// </summary>
		static string FindPreferredDrmRenderNode()
		{
			try
			{
				// renderD128 is the primary GPU on single-GPU and most PRIME systems.
				for (int i = 128; i < 256; i++)
				{
					var path = $"/dev/dri/renderD{i}";
					if (File.Exists(path))
						return path;
				}
			}
			catch { /* /dev/dri may not be accessible */ }

			return null;
		}

		// ── IVulkanSurfaceInfo implementations ────────────────────────────────────

		sealed class WaylandSurfaceInfo : IVulkanSurfaceInfo
		{
			public VulkanSurfaceType SurfaceType => VulkanSurfaceType.Wayland;
			public IntPtr WlSurface { get; }
			public IntPtr WlDisplay { get; }
			public string PreferredPhysicalDeviceDrmNode { get; }

			// X11 not applicable
			public IntPtr XDisplay => IntPtr.Zero;
			public ulong XWindow => 0;

			public WaylandSurfaceInfo(IntPtr wlDisplay, IntPtr wlSurface, string drmNode)
			{
				WlDisplay = wlDisplay;
				WlSurface = wlSurface;
				PreferredPhysicalDeviceDrmNode = drmNode;
			}
		}

		sealed class X11SurfaceInfo : IVulkanSurfaceInfo
		{
			public VulkanSurfaceType SurfaceType => VulkanSurfaceType.X11;
			public IntPtr XDisplay { get; }
			public ulong XWindow { get; }

			// Wayland not applicable
			public IntPtr WlSurface => IntPtr.Zero;
			public IntPtr WlDisplay => IntPtr.Zero;
			public string PreferredPhysicalDeviceDrmNode => null;

			public X11SurfaceInfo(IntPtr xDisplay, ulong xWindow)
			{
				XDisplay = xDisplay;
				XWindow = xWindow;
			}
		}
	}
#endif
}
