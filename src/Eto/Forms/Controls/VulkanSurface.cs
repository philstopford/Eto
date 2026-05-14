namespace Eto.Forms;

/// <summary>
/// Identifies the display-server backend used by a <see cref="VulkanSurface"/>.
/// </summary>
public enum VulkanSurfaceType
{
	/// <summary>Wayland compositor protocol.</summary>
	Wayland,
	/// <summary>X Window System (Xlib).</summary>
	X11,
}

/// <summary>
/// Platform-specific native surface handles that Vulkan consumers need in order to create a
/// <c>VkSurfaceKHR</c> via the appropriate WSI extension (<c>VK_KHR_wayland_surface</c> or
/// <c>VK_KHR_xlib_surface</c>).
/// </summary>
/// <remarks>
/// Obtain an instance through <see cref="VulkanSurface.GetSurfaceInfo"/>.
/// Members that do not apply to the active <see cref="SurfaceType"/> return
/// <see cref="IntPtr.Zero"/> / zero / <see langword="null"/>.
/// </remarks>
[CLSCompliant(false)]
public interface IVulkanSurfaceInfo
{
	/// <summary>Gets the display-server backend in use.</summary>
	VulkanSurfaceType SurfaceType { get; }

	// ── Wayland ──────────────────────────────────────────────────────────

	/// <summary>
	/// (Wayland only) Pointer to the <c>wl_surface</c> owned by this control.
	/// Pass to <c>VkWaylandSurfaceCreateInfoKHR.surface</c>.
	/// </summary>
	IntPtr WlSurface { get; }

	/// <summary>
	/// (Wayland only) Pointer to the <c>wl_display</c> connection used by GDK.
	/// Pass to <c>VkWaylandSurfaceCreateInfoKHR.display</c>.
	/// </summary>
	IntPtr WlDisplay { get; }

	/// <summary>
	/// (Wayland only) Path to the DRM render node of the preferred physical device, e.g.
	/// <c>/dev/dri/renderD128</c>.  May be <see langword="null"/> when the information is
	/// unavailable.
	/// </summary>
	/// <remarks>
	/// On PRIME / hybrid-graphics systems the Vulkan physical device used for rendering must
	/// match the device driving the display.  Compare this path against
	/// <c>VkPhysicalDeviceDrmPropertiesEXT.renderMajor</c> / <c>renderMinor</c> to select the
	/// correct device.
	/// </remarks>
	string PreferredPhysicalDeviceDrmNode { get; }

	// ── X11 ──────────────────────────────────────────────────────────────

	/// <summary>
	/// (X11 only) Pointer to the Xlib <c>Display *</c>.
	/// Pass to <c>VkXlibSurfaceCreateInfoKHR.dpy</c>.
	/// </summary>
	IntPtr XDisplay { get; }

	/// <summary>
	/// (X11 only) X Window ID (<c>Window</c>) of the control's native window.
	/// Pass to <c>VkXlibSurfaceCreateInfoKHR.window</c>.
	/// </summary>
	ulong XWindow { get; }
}

/// <summary>
/// Event arguments for the <see cref="VulkanSurface.Render"/> event.
/// </summary>
public class VulkanRenderEventArgs : EventArgs
{
	// Intentionally empty.  Consumers check VulkanSurface.Size and BackingScaleFactor
	// directly to detect resize and drive swapchain recreation.
}

/// <summary>
/// A control that owns a native Vulkan-renderable surface.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="Drawable"/>, this control is not painted via Cairo / GDI+ / CoreGraphics.
/// The platform handler creates the lowest-level native surface appropriate for the current
/// display server (a <c>wl_subsurface</c> on Wayland, an X11 window on X11) and fires lifecycle
/// events so that consumers can drive a Vulkan swapchain.
/// </para>
/// <para>
/// Typical usage:
/// <code>
/// var surface = new VulkanSurface();
/// surface.SurfaceCreated += (s, e) =>
/// {
///     var info = surface.GetSurfaceInfo();
///     // create VkSurfaceKHR and swapchain using info
/// };
/// surface.Render += (s, e) =>
/// {
///     // recreate swapchain if surface.Size changed, then present
/// };
/// surface.SurfaceDestroyed += (s, e) =>
/// {
///     // destroy swapchain and VkSurfaceKHR
/// };
/// </code>
/// </para>
/// </remarks>
[Handler(typeof(VulkanSurface.IHandler))]
public class VulkanSurface : Control
{
	new IHandler Handler => (IHandler)base.Handler;

	static VulkanSurface()
	{
		EventLookup.Register<VulkanSurface>(c => c.OnSurfaceCreated(null), SurfaceCreatedEvent);
		EventLookup.Register<VulkanSurface>(c => c.OnSurfaceDestroyed(null), SurfaceDestroyedEvent);
		EventLookup.Register<VulkanSurface>(c => c.OnRender(null), RenderEvent);
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="VulkanSurface"/> class.
	/// </summary>
	public VulkanSurface()
	{
		// IHandler is [AutoInitialize(false)], so Widget..ctor() does NOT call Initialize()
		// automatically. We must call Create() first (so the native widget is ready) and
		// then Initialize() ourselves, exactly like Drawable does.
		Handler.Create();
		Initialize();
	}

	#region Events

	/// <summary>Event identifier for handlers when attaching <see cref="SurfaceCreated"/>.</summary>
	public const string SurfaceCreatedEvent = "VulkanSurface.SurfaceCreated";
	/// <summary>Event identifier for handlers when attaching <see cref="SurfaceDestroyed"/>.</summary>
	public const string SurfaceDestroyedEvent = "VulkanSurface.SurfaceDestroyed";
	/// <summary>Event identifier for handlers when attaching <see cref="Render"/>.</summary>
	public const string RenderEvent = "VulkanSurface.Render";

	/// <summary>
	/// Raised after the native surface has been created and is ready for Vulkan use.
	/// Call <see cref="GetSurfaceInfo"/> to obtain the platform-specific handles.
	/// </summary>
	public event EventHandler<EventArgs> SurfaceCreated
	{
		add { Properties.AddHandlerEvent(SurfaceCreatedEvent, value); }
		remove { Properties.RemoveEvent(SurfaceCreatedEvent, value); }
	}

	/// <summary>Raises the <see cref="SurfaceCreated"/> event.</summary>
	protected virtual void OnSurfaceCreated(EventArgs e) =>
		Properties.TriggerEvent(SurfaceCreatedEvent, this, e);

	/// <summary>
	/// Raised just before the native surface is torn down (on control unload or disposal).
	/// Consumers <em>must</em> destroy the Vulkan swapchain and <c>VkSurfaceKHR</c> before
	/// returning from this event.
	/// </summary>
	public event EventHandler<EventArgs> SurfaceDestroyed
	{
		add { Properties.AddHandlerEvent(SurfaceDestroyedEvent, value); }
		remove { Properties.RemoveEvent(SurfaceDestroyedEvent, value); }
	}

	/// <summary>Raises the <see cref="SurfaceDestroyed"/> event.</summary>
	protected virtual void OnSurfaceDestroyed(EventArgs e) =>
		Properties.TriggerEvent(SurfaceDestroyedEvent, this, e);

	/// <summary>
	/// Raised when the surface should present a new frame.
	/// Check <see cref="Control.Size"/> against the current swapchain extent; if they differ
	/// recreate the swapchain before presenting.
	/// </summary>
	public event EventHandler<VulkanRenderEventArgs> Render
	{
		add { Properties.AddHandlerEvent(RenderEvent, value); }
		remove { Properties.RemoveEvent(RenderEvent, value); }
	}

	/// <summary>Raises the <see cref="Render"/> event.</summary>
	protected virtual void OnRender(VulkanRenderEventArgs e) =>
		Properties.TriggerEvent(RenderEvent, this, e);

	#endregion

	/// <summary>
	/// Gets the ratio between logical (Eto) points and physical pixels on this surface.
	/// Use this to correctly size the Vulkan swapchain images.
	/// </summary>
	public float BackingScaleFactor => Handler.BackingScaleFactor;

	/// <summary>
	/// Returns the platform-specific surface handles, or <see langword="null"/> if the surface
	/// has not yet been created (i.e. before <see cref="SurfaceCreated"/> fires).
	/// </summary>
	[CLSCompliant(false)]
	public IVulkanSurfaceInfo GetSurfaceInfo() => Handler.GetSurfaceInfo();

	/// <summary>
	/// Handler interface that platform implementations must implement.
	/// </summary>
	[AutoInitialize(false)]
	[CLSCompliant(false)]
	public new interface IHandler : Control.IHandler
	{
		/// <summary>Creates the underlying native widget. Called before <see cref="Widget.Initialize"/>.</summary>
		void Create();

		/// <summary>Gets the backing-scale factor for this surface.</summary>
		float BackingScaleFactor { get; }

		/// <summary>
		/// Returns the current <see cref="IVulkanSurfaceInfo"/>, or <see langword="null"/>
		/// when the native surface has not yet been created.
		/// </summary>
		[CLSCompliant(false)]
		IVulkanSurfaceInfo GetSurfaceInfo();
	}

	/// <summary>
	/// Callback interface used by handler implementations to raise widget events.
	/// </summary>
	public new interface ICallback : Control.ICallback
	{
		/// <summary>Raises <see cref="SurfaceCreated"/>.</summary>
		void OnSurfaceCreated(VulkanSurface widget, EventArgs e);
		/// <summary>Raises <see cref="SurfaceDestroyed"/>.</summary>
		void OnSurfaceDestroyed(VulkanSurface widget, EventArgs e);
		/// <summary>Raises <see cref="Render"/>.</summary>
		void OnRender(VulkanSurface widget, VulkanRenderEventArgs e);
	}

	/// <summary>Default callback implementation.</summary>
	protected new class Callback : Control.Callback, ICallback
	{
		/// <inheritdoc/>
		public void OnSurfaceCreated(VulkanSurface widget, EventArgs e)
		{
			using (widget.Platform.Context)
				widget.OnSurfaceCreated(e);
		}

		/// <inheritdoc/>
		public void OnSurfaceDestroyed(VulkanSurface widget, EventArgs e)
		{
			using (widget.Platform.Context)
				widget.OnSurfaceDestroyed(e);
		}

		/// <inheritdoc/>
		public void OnRender(VulkanSurface widget, VulkanRenderEventArgs e)
		{
			using (widget.Platform.Context)
				widget.OnRender(e);
		}
	}

	static readonly object _callback = new Callback();

	/// <inheritdoc/>
	protected override object GetCallback() => _callback;
}
