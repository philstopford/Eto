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
		bool _ownsWlSurface;    // true only when we created _wlSurface ourselves

		// ── X11 state ─────────────────────────────────────────────────────────────
		IntPtr _xDisplay;
		ulong _xWindow;

		// ── shared ───────────────────────────────────────────────────────────────
		bool _isWayland;
		bool _surfaceAlive;
		bool _renderQueued;      // coalesces Invalidate()-triggered renders
		readonly bool _diagnosticsEnabled = DiagnosticsEnabled();
		IVulkanSurfaceInfo _surfaceInfo;
		(int X, int Y)? _lastSubsurfacePosition;

		// ── IHandler ─────────────────────────────────────────────────────────────

		/// <inheritdoc/>
		public float BackingScaleFactor => (float)Control.ScaleFactor;

		/// <inheritdoc/>
		public IVulkanSurfaceInfo GetSurfaceInfo() => _surfaceInfo;

		// ── construction / teardown ───────────────────────────────────────────────

		/// <summary>Creates the underlying <see cref="Gtk.DrawingArea"/> widget.</summary>
		/// <remarks>Called by the Eto platform during handler construction.</remarks>
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
			Diag($"Initialize: wayland={_isWayland} scale={Control.ScaleFactor} display='{Gdk.Display.Default?.Name ?? "<null>"}'");
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
			// Defer GPU rendering to outside GTK's draw callback.  Calling
			// SwapBuffers (Veldrid Vulkan / EGL) from inside a GTK Drawn handler
			// triggers Wayland protocol functions (wl_surface_commit, etc.) while
			// GTK itself is already inside a Wayland event dispatch loop — which
			// is a re-entrancy violation in libwayland that prevents the window
			// from appearing on screen.
			//
			// AsyncInvoke schedules the call for the next main-loop iteration,
			// safely outside any draw or Wayland-event context.  The coalescing
			// flag ensures at most one pending render per Invalidate() burst.
			if (_surfaceAlive && !_renderQueued)
			{
				_renderQueued = true;
				Application.Instance.AsyncInvoke(() =>
				{
					_renderQueued = false;
					if (_surfaceAlive)
						Callback.OnRender(Widget, new VulkanRenderEventArgs());
				});
			}

			// Suppress GTK's default Cairo painting — GPU code owns the pixels.
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
			Diag($"SizeAllocated: alloc={args.Allocation.Width}x{args.Allocation.Height} surfaceAlive={_surfaceAlive}");

			// Defer rendering to outside GTK's layout/draw pass for the same reason
			// as HandleDrawn: calling SwapBuffers from inside a GTK signal handler
			// causes Wayland re-entrancy.  The coalescing flag prevents duplicate
			// renders if a resize and a queued Invalidate() arrive in the same burst.
			if (_surfaceAlive && !_renderQueued)
			{
				_renderQueued = true;
				Application.Instance.AsyncInvoke(() =>
				{
					_renderQueued = false;
					if (_surfaceAlive)
						Callback.OnRender(Widget, new VulkanRenderEventArgs());
				});
			}
		}

		// ── surface lifecycle ─────────────────────────────────────────────────────

		void OnRealized()
		{
			Diag($"OnRealized: isWayland={_isWayland} controlWindow={Control.Window?.Handle ?? IntPtr.Zero}");
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
			Diag($"InitializeWayland: wl_display=0x{_wlDisplay.ToInt64():X}");

			// Ensure we have the Wayland globals (idempotent).
			WaylandGlobals.Initialize(_wlDisplay);

			if (WaylandGlobals.Compositor == IntPtr.Zero || WaylandGlobals.Subcompositor == IntPtr.Zero)
				return;

			// On Wayland, we must NOT pass GTK's own wl_surface to Vulkan directly.
			// vkCreateSwapchainKHR takes exclusive ownership of the wl_surface's buffer
			// queue; any subsequent GTK commit to that same surface (damage, expose, etc.)
			// causes a Wayland protocol violation → EPROTO (error 71).  The correct
			// approach is to create our own wl_surface as a wl_subsurface of the GTK
			// top-level window surface and hand that dedicated surface to Vulkan.

			// Get the parent wl_surface from the top-level GDK window.
			var topLevel = Control.Toplevel;
			if (topLevel?.Window == null)
				return;

			var parentWlSurface = NativeMethods.gdk_wayland_window_get_wl_surface(topLevel.Window.Handle);
			if (parentWlSurface == IntPtr.Zero)
				return;
			Diag($"InitializeWayland: parent_wl_surface=0x{parentWlSurface.ToInt64():X}");

			// Create our own wl_surface and make it a subsurface of the GTK window surface.
			_wlSurface = WaylandGlobals.wl_compositor_create_surface(WaylandGlobals.Compositor);
			if (_wlSurface == IntPtr.Zero)
				return;
			_ownsWlSurface = true;
			Diag($"InitializeWayland: child_wl_surface=0x{_wlSurface.ToInt64():X}");

			_wlSubsurface = WaylandGlobals.wl_subcompositor_get_subsurface(
				WaylandGlobals.Subcompositor, _wlSurface, parentWlSurface);

			if (_wlSubsurface == IntPtr.Zero)
			{
				WaylandGlobals.wl_surface_destroy(_wlSurface);
				_wlSurface = IntPtr.Zero;
				_ownsWlSurface = false;
				return;
			}
			Diag($"InitializeWayland: wl_subsurface=0x{_wlSubsurface.ToInt64():X}");

			// Desync: Vulkan presents independently of GTK's render loop.
			WaylandGlobals.wl_subsurface_set_desync(_wlSubsurface);

			// Explicitly place the Vulkan subsurface above the parent surface.  Some
			// compositor/GTK combinations keep the newly created subsurface effectively
			// occluded unless we apply the z-order request ourselves.
			WaylandGlobals.wl_subsurface_place_above(_wlSubsurface, parentWlSurface);

			// Inform the compositor of the pixel density.  On HiDPI displays (GTK
			// scale ≥ 2) Vulkan will render at physical-pixel resolution; without
			// this hint the compositor would interpret the oversized buffer as a
			// 1:1 buffer, leaving part of the subsurface area uncovered (black).
			int backingScale = Control.ScaleFactor; // integer; 1 on standard-DPI, 2 on HiDPI
			if (backingScale > 1)
				WaylandGlobals.wl_surface_set_buffer_scale(_wlSurface, backingScale);
			Diag($"InitializeWayland: bufferScale={backingScale}");

			// Set initial position (pending until parent commits).
			UpdateSubsurfacePosition();

			// Commit the parent surface NOW to apply all pending subsurface state:
			// set_desync and set_position (and set_buffer_scale on the child surface).
			// This is safe at this point: GTK has already committed its initial state
			// during window realization, so the parent's own pending state is empty —
			// we are NOT attaching a new buffer to the parent, only triggering the
			// subsurface state application.  Relying solely on QueueDraw() would leave
			// the pending state unapplied until GTK's next async frame callback, which
			// races with Veldrid's first present: the subsurface stays in sync mode,
			// queued Vulkan commits are held by the compositor, and the viewport stays
			// black for the entire session.
			WaylandGlobals.wl_surface_commit(parentWlSurface);
			Diag("InitializeWayland: parent commit submitted");

			// Block until the compositor has processed the commit.  After this returns,
			// desync mode is active and the subsurface is at the correct position, so
			// the first Veldrid frame will appear immediately on screen.
			WaylandGlobals.RoundTrip(_wlDisplay);
			Diag("InitializeWayland: display roundtrip complete");

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
			Diag($"InitializeX11: xDisplay=0x{_xDisplay.ToInt64():X} xWindow=0x{_xWindow:X}");

			Control.SizeAllocated += HandleSizeAllocated;
			FireSurfaceCreated();
		}

		void FireSurfaceCreated()
		{
			_surfaceAlive = true;
			Diag("FireSurfaceCreated");

			// Defer OnSurfaceCreated to the next main-loop iteration, for the same
			// reason that the initial render is deferred below.  FireSurfaceCreated
			// is called from OnRealized → HandleRealized, which runs inside GDK's
			// Wayland event dispatch loop (wl_display_dispatch).  OnSurfaceCreated
			// typically creates a Veldrid GraphicsDevice, whose Vulkan/Wayland WSI
			// internally calls wl_display_roundtrip — a nested dispatch — which is
			// a re-entrancy violation in libwayland.  The violation corrupts the
			// compositor's view of the wl_surface, causing all subsequent
			// vkQueuePresentKHR frames to appear as a solid black surface.
			Application.Instance.AsyncInvoke(() =>
			{
				if (!_surfaceAlive)
					return;

				Callback.OnSurfaceCreated(Widget, EventArgs.Empty);

				// Schedule the first render after the surface-created callback
				// has returned, so the user's device/swapchain setup is complete
				// before we ask for the first frame.
				if (!_renderQueued)
				{
					_renderQueued = true;
					Application.Instance.AsyncInvoke(() =>
					{
						_renderQueued = false;
						if (_surfaceAlive)
							Callback.OnRender(Widget, new VulkanRenderEventArgs());
					});
				}
			});
		}

		void TearDownSurface()
		{
			Diag("TearDownSurface begin");
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
				Diag("TearDownSurface: wl_subsurface destroyed");
				_wlSubsurface = IntPtr.Zero;
			}
			if (_wlSurface != IntPtr.Zero)
			{
				if (_ownsWlSurface)
				{
					WaylandGlobals.wl_surface_destroy(_wlSurface);
					Diag("TearDownSurface: wl_surface destroyed");
				}
				else
				{
					Diag("TearDownSurface: GTK-owned wl_surface left intact");
				}
			}
			_wlSurface = IntPtr.Zero;
			_ownsWlSurface = false;
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
				// wl_subsurface.set_position takes logical-pixel coordinates; the position
				// is applied to the parent's next commit (triggered by GTK rendering).
				WaylandGlobals.wl_subsurface_set_position(_wlSubsurface, x, y);
				var position = (x, y);
				if (_lastSubsurfacePosition != position)
				{
					_lastSubsurfacePosition = position;
					Diag($"UpdateSubsurfacePosition: x={x} y={y} topLevelSize={topLevel.AllocatedWidth}x{topLevel.AllocatedHeight} controlSize={Control.AllocatedWidth}x{Control.AllocatedHeight}");
				}
				// Do NOT commit _wlSurface here: an empty commit with no pending buffer or
				// damage sent in desync mode causes some compositors (e.g. Mutter) to
				// treat the surface as if it has no content, clearing the last Vulkan frame.
			}
			else
			{
				Diag("UpdateSubsurfacePosition: TranslateCoordinates failed");
			}
		}

		static bool DiagnosticsEnabled()
		{
			var value = Environment.GetEnvironmentVariable("ETO_VELDRID_DIAGNOSTICS")
				?? Environment.GetEnvironmentVariable("ETO_VULKAN_SURFACE_DIAGNOSTICS");
			if (string.IsNullOrWhiteSpace(value))
				return false;
			return value == "1"
				|| value.Equals("true", StringComparison.OrdinalIgnoreCase)
				|| value.Equals("yes", StringComparison.OrdinalIgnoreCase)
				|| value.Equals("on", StringComparison.OrdinalIgnoreCase);
		}

		void Diag(string message)
		{
			if (!_diagnosticsEnabled)
				return;
			try
			{
				Console.WriteLine($"[VulkanSurfaceHandler] {message}");
			}
			catch
			{
				// best-effort diagnostics only
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
				// DRM render nodes are numbered renderD128–renderD255 by kernel convention
				// (minor numbers 128–255 of the DRM subsystem, as defined in the Linux
				// drm_minor_alloc implementation).  renderD128 is the primary GPU on
				// single-GPU and most PRIME configurations.
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
