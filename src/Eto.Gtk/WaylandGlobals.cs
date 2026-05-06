#if GTK3
namespace Eto.GtkSharp
{
	/// <summary>
	/// Obtains and caches the Wayland protocol globals needed for Vulkan surface creation
	/// (<c>wl_compositor</c> and <c>wl_subcompositor</c>) by listening on the Wayland registry.
	/// </summary>
	/// <remarks>
	/// All Wayland protocol objects obtained here are bound on GDK's own <c>wl_display</c>
	/// connection, so they can safely be used together with GDK windows on the same display.
	/// Call <see cref="Initialize"/> once, from the UI thread, after GTK has been initialized.
	/// </remarks>
	internal static unsafe class WaylandGlobals
	{
		const string libwayland = "libwayland-client.so.0";

		// ── wayland-client low-level API ─────────────────────────────────────────

		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern IntPtr wl_display_get_registry(IntPtr display);

		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern int wl_display_roundtrip(IntPtr display);

		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern int wl_proxy_add_listener(IntPtr proxy, IntPtr implementation, IntPtr data);

		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern void wl_proxy_destroy(IntPtr proxy);

		/// <summary>wl_registry_bind — binds a global interface.</summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern IntPtr wl_registry_bind(IntPtr registry, uint name, IntPtr iface, uint version);

		// ── wayland-client core-protocol functions (all WL_EXPORT in libwayland-client) ──

		/// <summary>Creates a new <c>wl_surface</c> from the compositor.</summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		internal static extern IntPtr wl_compositor_create_surface(IntPtr compositor);

		/// <summary>Creates a <c>wl_subsurface</c> relationship between <paramref name="surface"/> and <paramref name="parent"/>.</summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		internal static extern IntPtr wl_subcompositor_get_subsurface(IntPtr subcompositor, IntPtr surface, IntPtr parent);

		/// <summary>Sets the position of the subsurface relative to its parent.</summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		internal static extern void wl_subsurface_set_position(IntPtr subsurface, int x, int y);

		/// <summary>
		/// Puts the subsurface in desynchronized mode so its content can be committed
		/// independently of the parent — required for Vulkan-driven presentation.
		/// </summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		internal static extern void wl_subsurface_set_desync(IntPtr subsurface);

		/// <summary>
		/// Raises the subsurface above <paramref name="sibling"/> in the stacking order.
		/// Pass the parent <c>wl_surface</c> to place above all GTK-drawn content.
		/// </summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		internal static extern void wl_subsurface_place_above(IntPtr subsurface, IntPtr sibling);

		/// <summary>Destroys the <c>wl_subsurface</c> object.</summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		internal static extern void wl_subsurface_destroy(IntPtr subsurface);

		/// <summary>Commits the pending state for a <c>wl_surface</c>.</summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		internal static extern void wl_surface_commit(IntPtr surface);

		/// <summary>Destroys the <c>wl_surface</c> object.</summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		internal static extern void wl_surface_destroy(IntPtr surface);

		// ── registry-listener infrastructure ─────────────────────────────────────

		[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
		delegate void RegistryGlobalFunc(
			IntPtr data, IntPtr registry, uint name,
			[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)] string iface,
			uint version);

		[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
		delegate void RegistryGlobalRemoveFunc(IntPtr data, IntPtr registry, uint name);

		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		struct RegistryListener
		{
			public IntPtr global;
			public IntPtr global_remove;
		}

		// ── cached globals ────────────────────────────────────────────────────────

		/// <summary>Bound <c>wl_compositor</c> global, or <see cref="IntPtr.Zero"/> if unavailable.</summary>
		internal static IntPtr Compositor { get; private set; }

		/// <summary>Bound <c>wl_subcompositor</c> global, or <see cref="IntPtr.Zero"/> if unavailable.</summary>
		internal static IntPtr Subcompositor { get; private set; }

		static bool _initialized;

		// Keep delegates alive so the GC doesn't collect them before the roundtrip finishes.
		static RegistryGlobalFunc _globalDelegate;
		static RegistryGlobalRemoveFunc _removeDelegate;

		// Cached interface pointers resolved via NativeLibrary.
		static IntPtr _compositorIface;
		static IntPtr _subcompositorIface;

		/// <summary>
		/// Binds <c>wl_compositor</c> and <c>wl_subcompositor</c> from the Wayland registry.
		/// Safe to call multiple times; subsequent calls are no-ops.
		/// </summary>
		/// <param name="wlDisplay">
		/// The <c>wl_display*</c> of GDK's Wayland connection, obtained via
		/// <c>gdk_wayland_display_get_wl_display</c>.
		/// </param>
		internal static void Initialize(IntPtr wlDisplay)
		{
			if (_initialized)
				return;
			_initialized = true;

			if (wlDisplay == IntPtr.Zero)
				return;

			// Resolve the wl_interface pointers exported by libwayland-client.
			try
			{
				var lib = System.Runtime.InteropServices.NativeLibrary.Load(libwayland);
				_compositorIface = System.Runtime.InteropServices.NativeLibrary.GetExport(lib, "wl_compositor_interface");
				_subcompositorIface = System.Runtime.InteropServices.NativeLibrary.GetExport(lib, "wl_subcompositor_interface");
			}
			catch
			{
				return;
			}

			_globalDelegate = OnGlobal;
			_removeDelegate = OnGlobalRemove;

			var listener = new RegistryListener
			{
				global = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_globalDelegate),
				global_remove = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_removeDelegate),
			};

			var registry = wl_display_get_registry(wlDisplay);
			if (registry == IntPtr.Zero)
				return;

			try
			{
				// Pass a pointer to the listener struct on the stack.
				// This is safe because wl_display_roundtrip returns before we leave this scope.
				RegistryListener* pListener = &listener;
				wl_proxy_add_listener(registry, (IntPtr)pListener, IntPtr.Zero);
				wl_display_roundtrip(wlDisplay);
			}
			finally
			{
				wl_proxy_destroy(registry);
			}
		}

		static void OnGlobal(IntPtr data, IntPtr registry, uint name, string iface, uint version)
		{
			if (iface == "wl_compositor" && Compositor == IntPtr.Zero)
				Compositor = wl_registry_bind(registry, name, _compositorIface, Math.Min(version, 4u));
			else if (iface == "wl_subcompositor" && Subcompositor == IntPtr.Zero)
				Subcompositor = wl_registry_bind(registry, name, _subcompositorIface, 1u);
		}

		static void OnGlobalRemove(IntPtr data, IntPtr registry, uint name) { }
	}
}
#endif
