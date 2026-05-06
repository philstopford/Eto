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

		// ── exported proxy-marshal functions (always present in libwayland-client) ─
		//
		// The per-interface helpers such as wl_display_get_registry,
		// wl_compositor_create_surface, wl_surface_commit, etc. are defined as
		// static-inline functions in the wayland-client-protocol.h header and are
		// therefore NOT exported by the shared library.  We implement them ourselves
		// using the low-level wl_proxy_marshal_array* functions which ARE exported.

		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern int wl_display_roundtrip(IntPtr display);

		/// <summary>Flushes all pending outgoing Wayland protocol messages to the compositor socket.</summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern int wl_display_flush(IntPtr display);

		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern int wl_proxy_add_listener(IntPtr proxy, IntPtr implementation, IntPtr data);

		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern void wl_proxy_destroy(IntPtr proxy);

		/// <summary>
		/// Creates a new proxy for a constructor request that produces a typed object.
		/// Equivalent to calling wl_proxy_marshal_constructor internally.
		/// </summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern IntPtr wl_proxy_marshal_array_constructor(IntPtr proxy, uint opcode, WlArgument* args, IntPtr iface);

		/// <summary>
		/// Like <see cref="wl_proxy_marshal_array_constructor"/> but binds the new proxy
		/// at an explicit version — required for <c>wl_registry_bind</c>.
		/// </summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern IntPtr wl_proxy_marshal_array_constructor_versioned(IntPtr proxy, uint opcode, WlArgument* args, IntPtr iface, uint version);

		/// <summary>Sends a void request (no new object created).</summary>
		[System.Runtime.InteropServices.DllImport(libwayland, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
		static extern void wl_proxy_marshal_array(IntPtr proxy, uint opcode, WlArgument* args);

		// ── wl_argument union ─────────────────────────────────────────────────────
		//
		// Mirrors the C union wl_argument { int32_t i; uint32_t u; wl_fixed_t f;
		//   const char *s; struct wl_object *o; uint32_t n; ... }.
		// All members share offset 0; the size equals one pointer width.

		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
		struct WlArgument
		{
			[System.Runtime.InteropServices.FieldOffset(0)] public int i;    // int32_t
			[System.Runtime.InteropServices.FieldOffset(0)] public uint u;   // uint32_t
			[System.Runtime.InteropServices.FieldOffset(0)] public IntPtr o; // object* or string*
		}

		// ── wayland-protocol opcodes ──────────────────────────────────────────────

		const uint WL_DISPLAY_GET_REGISTRY        = 1;
		const uint WL_REGISTRY_BIND               = 0;
		const uint WL_COMPOSITOR_CREATE_SURFACE   = 0;
		const uint WL_SUBCOMPOSITOR_GET_SUBSURFACE = 1;
		const uint WL_SUBSURFACE_DESTROY          = 0;
		const uint WL_SUBSURFACE_SET_POSITION     = 1;
		const uint WL_SUBSURFACE_PLACE_ABOVE      = 2;
		const uint WL_SUBSURFACE_SET_DESYNC       = 5;
		const uint WL_SURFACE_DESTROY             = 0;
		const uint WL_SURFACE_COMMIT              = 6;
		const uint WL_SURFACE_SET_BUFFER_SCALE    = 8;  // requires wl_compositor version 3+

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

		// wl_interface pointers resolved via NativeLibrary (exported data symbols).
		static IntPtr _registryIface;
		static IntPtr _compositorIface;
		static IntPtr _subcompositorIface;
		static IntPtr _surfaceIface;
		static IntPtr _subsurfaceIface;

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

			// Resolve the wl_interface data pointers exported by libwayland-client.
			// These are needed as the 'interface' argument to wl_proxy_marshal_array_constructor
			// so that the returned proxies are correctly typed inside libwayland.
			try
			{
				var lib = System.Runtime.InteropServices.NativeLibrary.Load(libwayland);
				_registryIface     = System.Runtime.InteropServices.NativeLibrary.GetExport(lib, "wl_registry_interface");
				_compositorIface   = System.Runtime.InteropServices.NativeLibrary.GetExport(lib, "wl_compositor_interface");
				_subcompositorIface = System.Runtime.InteropServices.NativeLibrary.GetExport(lib, "wl_subcompositor_interface");
				_surfaceIface      = System.Runtime.InteropServices.NativeLibrary.GetExport(lib, "wl_surface_interface");
				_subsurfaceIface   = System.Runtime.InteropServices.NativeLibrary.GetExport(lib, "wl_subsurface_interface");
			}
			catch
			{
				return;
			}

			_globalDelegate = OnGlobal;
			_removeDelegate = OnGlobalRemove;

			var listener = new RegistryListener
			{
				global        = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_globalDelegate),
				global_remove = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_removeDelegate),
			};

			var registry = WlDisplayGetRegistry(wlDisplay);
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

		// ── wl_display helpers ────────────────────────────────────────────────────

		/// <summary>
		/// Implements <c>wl_display_get_registry</c> via wl_proxy_marshal_array_constructor.
		/// The function with that name is a static-inline in the header, not an exported symbol.
		/// </summary>
		static IntPtr WlDisplayGetRegistry(IntPtr display)
		{
			// Protocol: wl_display.get_registry (opcode 1) → new_id<wl_registry>
			// args[0] is the new_id placeholder (NULL; libwayland fills the id in).
			var args = stackalloc WlArgument[1];
			args[0].o = IntPtr.Zero;
			return wl_proxy_marshal_array_constructor(display, WL_DISPLAY_GET_REGISTRY, args, _registryIface);
		}

		/// <summary>
		/// Implements <c>wl_registry_bind</c> via wl_proxy_marshal_array_constructor_versioned.
		/// </summary>
		/// <remarks>
		/// The wl_registry.bind wire format for an untyped new_id is:
		///   uint32(name), string(interface_name), uint32(version), new_id.
		/// The interface name is the first field of the <c>wl_interface</c> struct.
		/// </remarks>
		static IntPtr WlRegistryBind(IntPtr registry, uint name, IntPtr iface, uint version)
		{
			// struct wl_interface { const char *name; ... } — name is at offset 0.
			var namePtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(iface);
			var args = stackalloc WlArgument[4];
			args[0].u = name;     // uint32: registry name
			args[1].o = namePtr;  // string: interface name (const char*)
			args[2].u = version;  // uint32: version to bind
			args[3].o = IntPtr.Zero; // new_id placeholder
			return wl_proxy_marshal_array_constructor_versioned(registry, WL_REGISTRY_BIND, args, iface, version);
		}

		// ── compositor / surface helpers (called from VulkanSurfaceHandler) ───────

		/// <summary>Creates a new <c>wl_surface</c> from the compositor.</summary>
		internal static IntPtr wl_compositor_create_surface(IntPtr compositor)
		{
			// Protocol: wl_compositor.create_surface (opcode 0) → new_id<wl_surface>
			var args = stackalloc WlArgument[1];
			args[0].o = IntPtr.Zero;
			return wl_proxy_marshal_array_constructor(compositor, WL_COMPOSITOR_CREATE_SURFACE, args, _surfaceIface);
		}

		/// <summary>Creates a <c>wl_subsurface</c> relationship between <paramref name="surface"/> and <paramref name="parent"/>.</summary>
		internal static IntPtr wl_subcompositor_get_subsurface(IntPtr subcompositor, IntPtr surface, IntPtr parent)
		{
			// Protocol: wl_subcompositor.get_subsurface (opcode 1) → new_id<wl_subsurface>, object, object
			var args = stackalloc WlArgument[3];
			args[0].o = IntPtr.Zero; // new_id placeholder
			args[1].o = surface;
			args[2].o = parent;
			return wl_proxy_marshal_array_constructor(subcompositor, WL_SUBCOMPOSITOR_GET_SUBSURFACE, args, _subsurfaceIface);
		}

		/// <summary>Sets the position of the subsurface relative to its parent.</summary>
		internal static void wl_subsurface_set_position(IntPtr subsurface, int x, int y)
		{
			// Protocol: wl_subsurface.set_position (opcode 1) — int, int
			var args = stackalloc WlArgument[2];
			args[0].i = x;
			args[1].i = y;
			wl_proxy_marshal_array(subsurface, WL_SUBSURFACE_SET_POSITION, args);
		}

		/// <summary>
		/// Puts the subsurface in desynchronized mode so its content can be committed
		/// independently of the parent — required for Vulkan-driven presentation.
		/// </summary>
		internal static void wl_subsurface_set_desync(IntPtr subsurface)
		{
			// Protocol: wl_subsurface.set_desync (opcode 5) — no args
			wl_proxy_marshal_array(subsurface, WL_SUBSURFACE_SET_DESYNC, (WlArgument*)null);
		}

		/// <summary>
		/// Raises the subsurface above <paramref name="sibling"/> in the stacking order.
		/// Pass the parent <c>wl_surface</c> to place above all GTK-drawn content.
		/// </summary>
		internal static void wl_subsurface_place_above(IntPtr subsurface, IntPtr sibling)
		{
			// Protocol: wl_subsurface.place_above (opcode 2) — object
			var args = stackalloc WlArgument[1];
			args[0].o = sibling;
			wl_proxy_marshal_array(subsurface, WL_SUBSURFACE_PLACE_ABOVE, args);
		}

		/// <summary>Destroys the <c>wl_subsurface</c> object.</summary>
		internal static void wl_subsurface_destroy(IntPtr subsurface)
		{
			// Protocol: wl_subsurface.destroy (opcode 0, destructor) — no args
			wl_proxy_marshal_array(subsurface, WL_SUBSURFACE_DESTROY, (WlArgument*)null);
			wl_proxy_destroy(subsurface);
		}

		/// <summary>Commits the pending state for a <c>wl_surface</c>.</summary>
		internal static void wl_surface_commit(IntPtr surface)
		{
			// Protocol: wl_surface.commit (opcode 6) — no args
			wl_proxy_marshal_array(surface, WL_SURFACE_COMMIT, (WlArgument*)null);
		}

		/// <summary>
		/// Sets the buffer scale hint for a <c>wl_surface</c> (requires wl_compositor ≥ v3).
		/// Call before the first buffer commit so the compositor knows the pixel density.
		/// </summary>
		/// <param name="surface">The <c>wl_surface*</c> to configure.</param>
		/// <param name="scale">Integer scale factor (1 = normal DPI, 2 = HiDPI 2×, etc.).</param>
		internal static void wl_surface_set_buffer_scale(IntPtr surface, int scale)
		{
			// Protocol: wl_surface.set_buffer_scale (opcode 8) — int32
			var args = stackalloc WlArgument[1];
			args[0].i = scale;
			wl_proxy_marshal_array(surface, WL_SURFACE_SET_BUFFER_SCALE, args);
		}

		/// <summary>Destroys the <c>wl_surface</c> object.</summary>
		internal static void wl_surface_destroy(IntPtr surface)
		{
			// Protocol: wl_surface.destroy (opcode 0, destructor) — no args
			wl_proxy_marshal_array(surface, WL_SURFACE_DESTROY, (WlArgument*)null);
			wl_proxy_destroy(surface);
		}

		/// <summary>
		/// Flushes all pending outgoing protocol messages to the compositor socket.
		/// Call after a batch of protocol requests to ensure they are sent before
		/// blocking operations (e.g. Vulkan device creation) that may delay the
		/// main loop's next iteration.
		/// </summary>
		internal static void Flush(IntPtr wlDisplay)
		{
			if (wlDisplay != IntPtr.Zero)
				wl_display_flush(wlDisplay);
		}

		// ── registry listener callbacks ───────────────────────────────────────────

		static void OnGlobal(IntPtr data, IntPtr registry, uint name, string iface, uint version)
		{
			if (iface == "wl_compositor" && Compositor == IntPtr.Zero)
				// wl_compositor version 4 introduced wl_surface.damage_buffer; cap there
				// to avoid binding a version higher than we know how to use.
				Compositor = WlRegistryBind(registry, name, _compositorIface, Math.Min(version, 4u));
			else if (iface == "wl_subcompositor" && Subcompositor == IntPtr.Zero)
				Subcompositor = WlRegistryBind(registry, name, _subcompositorIface, 1u);
		}

		static void OnGlobalRemove(IntPtr data, IntPtr registry, uint name) { }
	}
}
#endif
