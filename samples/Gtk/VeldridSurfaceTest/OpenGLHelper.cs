using System;
using System.Runtime.InteropServices;
using Veldrid;
using Veldrid.OpenGL;

namespace VeldridSurfaceTest;

/// <summary>
/// Helpers for creating a Veldrid <see cref="OpenGLPlatformInfo"/> from the native
/// Linux display/window handles that <see cref="VulkanSurface"/> exposes via
/// <see cref="IVulkanSurfaceInfo"/>.
///
/// Two paths are provided:
///
///  <b>GLX (X11)</b>
///    Uses <c>libGL.so.1</c>.  Called when <see cref="IVulkanSurfaceInfo.SurfaceType"/>
///    is <see cref="VulkanSurfaceType.X11"/>.
///    <c>XDisplay</c> + <c>XWindow</c> → GLX context → <see cref="OpenGLPlatformInfo"/>.
///
///  <b>EGL (Wayland or X11-EGL)</b>
///    Uses <c>libEGL.so.1</c>.  Called when
///    <see cref="IVulkanSurfaceInfo.SurfaceType"/> is
///    <see cref="VulkanSurfaceType.Wayland"/>, or as a fallback on X11 when
///    GLX is unavailable.
///    For Wayland, a <c>wl_egl_window</c> is created from the <c>wl_surface</c>
///    via <c>libwayland-egl.so.1</c> before the EGL window surface is constructed.
///
/// Both public methods return <see langword="null"/> on any failure so the caller
/// can fall back gracefully.
/// </summary>
internal static class OpenGLHelper
{
    // ── GLX (X11 / libGL) ────────────────────────────────────────────────────

    const string LibGL = "libGL.so.1";

    // Boolean tokens for glXChooseVisual's attribute list (no associated value).
    const int GLX_RGBA         = 4;
    const int GLX_DOUBLEBUFFER = 5;
    // Integer tokens — each is followed by the minimum acceptable value.
    const int GLX_RED_SIZE     = 8;
    const int GLX_GREEN_SIZE   = 9;
    const int GLX_BLUE_SIZE    = 10;
    const int GLX_ALPHA_SIZE   = 11;
    const int GLX_DEPTH_SIZE   = 12;

    [DllImport(LibGL, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr glXChooseVisual(IntPtr display, int screen, int[] attribList);

    [DllImport(LibGL, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr glXCreateContext(IntPtr display, IntPtr vis, IntPtr shareList,
        [MarshalAs(UnmanagedType.Bool)] bool direct);

    [DllImport(LibGL, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool glXMakeCurrent(IntPtr display, nint drawable, IntPtr ctx);

    [DllImport(LibGL, CallingConvention = CallingConvention.Cdecl)]
    static extern void glXSwapBuffers(IntPtr display, nint drawable);

    [DllImport(LibGL, CallingConvention = CallingConvention.Cdecl)]
    static extern void glXDestroyContext(IntPtr display, IntPtr ctx);

    [DllImport(LibGL, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr glXGetCurrentContext();

    [DllImport(LibGL, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "glXGetProcAddressARB")]
    static extern IntPtr glXGetProcAddressARB(
        [MarshalAs(UnmanagedType.LPStr)] string procName);

    /// <summary>
    /// Attempts to create a GLX OpenGL context on the given X11 window and returns a
    /// <see cref="OpenGLPlatformInfo"/> suitable for
    /// <see cref="GraphicsDevice.CreateOpenGL"/>.
    /// </summary>
    /// <param name="xDisplay">
    ///   X11 <c>Display*</c> from <see cref="IVulkanSurfaceInfo.XDisplay"/>.
    /// </param>
    /// <param name="xWindow">
    ///   X11 <c>Window</c> from <see cref="IVulkanSurfaceInfo.XWindow"/>.
    /// </param>
    /// <param name="outGlxContext">
    ///   On success, the native GLX context pointer.  Caller must release it
    ///   via <see cref="DestroyGlxContext"/> when the Veldrid device is disposed.
    /// </param>
    /// <returns>
    ///   A configured <see cref="OpenGLPlatformInfo"/>, or <see langword="null"/>
    ///   if any GLX call failed.
    /// </returns>
    public static OpenGLPlatformInfo? TryCreateGlxPlatformInfo(
        IntPtr xDisplay, ulong xWindow, out IntPtr outGlxContext)
    {
        outGlxContext = IntPtr.Zero;
        try
        {
            // Attribute list for glXChooseVisual.
            // Boolean attributes (GLX_RGBA, GLX_DOUBLEBUFFER) are listed alone.
            // Integer attributes are followed by the minimum acceptable value.
            int[] attribs =
            {
                GLX_RGBA,              // boolean — RGBA visual
                GLX_DOUBLEBUFFER,      // boolean — double-buffered
                GLX_RED_SIZE,   8,
                GLX_GREEN_SIZE, 8,
                GLX_BLUE_SIZE,  8,
                GLX_ALPHA_SIZE, 8,
                GLX_DEPTH_SIZE, 24,
                0,                     // None (terminator)
            };

            IntPtr visual = glXChooseVisual(xDisplay, 0, attribs);
            if (visual == IntPtr.Zero) return null;

            // Request a direct-rendering context (avoids X server round-trips for
            // GL commands, which is what all modern hardware uses).
            IntPtr ctx = glXCreateContext(xDisplay, visual, IntPtr.Zero, direct: true);
            if (ctx == IntPtr.Zero) return null;

            var drawable = (nint)xWindow;
            if (!glXMakeCurrent(xDisplay, drawable, ctx))
            {
                glXDestroyContext(xDisplay, ctx);
                return null;
            }

            outGlxContext = ctx;

            // Capture stable values for use inside the closures below.
            var capDpy = xDisplay;
            var capDrw = drawable;
            var capCtx = ctx;

            return new OpenGLPlatformInfo(
                openGLContextHandle:    capCtx,
                getProcAddress:         name   => glXGetProcAddressARB(name),
                makeCurrent:            handle => glXMakeCurrent(capDpy, capDrw, handle),
                getCurrentContext:      ()     => glXGetCurrentContext(),
                clearCurrentContext:    ()     => glXMakeCurrent(capDpy, 0, IntPtr.Zero),
                deleteContext:          handle => glXDestroyContext(capDpy, handle),
                swapBuffers:            ()     => glXSwapBuffers(capDpy, capDrw),
                setSyncToVerticalBlank: _      => { });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Releases a GLX context created by <see cref="TryCreateGlxPlatformInfo"/>.</summary>
    public static void DestroyGlxContext(IntPtr xDisplay, IntPtr glxCtx)
    {
        if (glxCtx == IntPtr.Zero || xDisplay == IntPtr.Zero) return;
        try
        {
            glXMakeCurrent(xDisplay, 0, IntPtr.Zero);
            glXDestroyContext(xDisplay, glxCtx);
        }
        catch { /* best-effort */ }
    }

    // ── EGL (Wayland or X11-EGL / libEGL + libwayland-egl) ───────────────────

    const string LibEGL        = "libEGL.so.1";
    const string LibWaylandEgl = "libwayland-egl.so.1";

    const uint EGL_OPENGL_API      = 0x30A2u;
    const int  EGL_NONE            = 0x3038;
    const int  EGL_OPENGL_BIT     = 0x0008;
    const int  EGL_WINDOW_BIT     = 0x0004;
    const int  EGL_RED_SIZE       = 0x3024;
    const int  EGL_GREEN_SIZE     = 0x3023;
    const int  EGL_BLUE_SIZE      = 0x3022;
    const int  EGL_ALPHA_SIZE     = 0x3021;
    const int  EGL_DEPTH_SIZE     = 0x3025;
    const int  EGL_SURFACE_TYPE   = 0x3033;
    const int  EGL_RENDERABLE_TYPE = 0x3040;
    const int  EGL_CONTEXT_MAJOR_VERSION = 0x3098;
    const int  EGL_CONTEXT_MINOR_VERSION = 0x30FB;

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr eglGetDisplay(IntPtr nativeDisplay);

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool eglInitialize(IntPtr dpy, out int major, out int minor);

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool eglBindAPI(uint api);

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool eglChooseConfig(IntPtr dpy, int[] attribs,
        IntPtr[] configs, int configSize, out int numConfigs);

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr eglCreateContext(IntPtr dpy, IntPtr config,
        IntPtr shareCtx, int[] attribs);

    // nativeWindow is XWindow on X11, or wl_egl_window* on Wayland.
    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr eglCreateWindowSurface(IntPtr dpy, IntPtr config,
        nint nativeWindow, int[]? attribs);

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool eglMakeCurrent(IntPtr dpy, IntPtr draw, IntPtr read, IntPtr ctx);

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr eglGetCurrentContext();

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool eglSwapBuffers(IntPtr dpy, IntPtr surface);

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr eglGetProcAddress(
        [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool eglDestroyContext(IntPtr dpy, IntPtr ctx);

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool eglDestroySurface(IntPtr dpy, IntPtr surface);

    [DllImport(LibEGL, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool eglTerminate(IntPtr dpy);

    // wl_egl_window is the Wayland EGL native window abstraction.
    // On X11 the XWindow is used directly as the native window, so these
    // wl_egl_window functions are only called on the Wayland path.
    [DllImport(LibWaylandEgl, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr wl_egl_window_create(IntPtr wlSurface, int width, int height);

    [DllImport(LibWaylandEgl, CallingConvention = CallingConvention.Cdecl)]
    static extern void wl_egl_window_destroy(IntPtr wlEglWindow);

    [DllImport(LibWaylandEgl, CallingConvention = CallingConvention.Cdecl)]
    static extern void wl_egl_window_resize(
        IntPtr wlEglWindow, int width, int height, int dx, int dy);

    /// <summary>
    /// Attempts to create an EGL OpenGL context and returns a
    /// <see cref="OpenGLPlatformInfo"/> suitable for
    /// <see cref="GraphicsDevice.CreateOpenGL"/>.
    ///
    /// Works on both X11 (using <paramref name="xDisplay"/> / <paramref name="xWindow"/>)
    /// and Wayland (using <paramref name="wlDisplay"/> / <paramref name="wlSurface"/>).
    /// Pass only the parameters appropriate for the current display server; the other
    /// pair should be <see cref="IntPtr.Zero"/> / 0.
    /// </summary>
    /// <param name="xDisplay">X11 <c>Display*</c> (X11 path only; zero on Wayland).</param>
    /// <param name="xWindow"> X11 <c>Window</c>   (X11 path only; zero on Wayland).</param>
    /// <param name="wlDisplay">Wayland <c>wl_display*</c> (Wayland path only; zero on X11).</param>
    /// <param name="wlSurface"> Wayland <c>wl_surface*</c> (Wayland path only; zero on X11).</param>
    /// <param name="width">Initial surface width  (pixels).</param>
    /// <param name="height">Initial surface height (pixels).</param>
    /// <param name="outEglDisplay">EGL display — must be terminated on cleanup.</param>
    /// <param name="outEglContext">EGL context  — must be destroyed on cleanup.</param>
    /// <param name="outEglSurface">EGL surface  — must be destroyed on cleanup.</param>
    /// <param name="outWlEglWindow">
    ///   <c>wl_egl_window*</c> on Wayland, <see cref="IntPtr.Zero"/> on X11.
    ///   Must be destroyed on cleanup if non-zero.
    /// </param>
    /// <returns>
    ///   A configured <see cref="OpenGLPlatformInfo"/>, or <see langword="null"/>
    ///   if any EGL call failed.
    /// </returns>
    public static OpenGLPlatformInfo? TryCreateEglPlatformInfo(
        IntPtr xDisplay,   ulong  xWindow,
        IntPtr wlDisplay,  IntPtr wlSurface,
        int width, int height,
        out IntPtr outEglDisplay, out IntPtr outEglContext,
        out IntPtr outEglSurface, out IntPtr outWlEglWindow)
    {
        outEglDisplay  = IntPtr.Zero;
        outEglContext  = IntPtr.Zero;
        outEglSurface  = IntPtr.Zero;
        outWlEglWindow = IntPtr.Zero;
        try
        {
            bool isWayland = wlDisplay != IntPtr.Zero;

            // ── EGL display ──────────────────────────────────────────────────
            // On X11: pass the X Display* directly.
            // On Wayland: pass the wl_display*.
            IntPtr nativeDpy = isWayland ? wlDisplay : xDisplay;
            var eglDpy = eglGetDisplay(nativeDpy);
            if (eglDpy == IntPtr.Zero) return null;
            outEglDisplay = eglDpy;

            if (!eglInitialize(eglDpy, out _, out _)) return null;

            // Request desktop OpenGL, not OpenGL ES.
            if (!eglBindAPI(EGL_OPENGL_API)) return null;

            // ── EGL config ───────────────────────────────────────────────────
            int[] cfgAttribs =
            {
                EGL_RED_SIZE,        8,
                EGL_GREEN_SIZE,      8,
                EGL_BLUE_SIZE,       8,
                EGL_ALPHA_SIZE,      8,
                EGL_DEPTH_SIZE,      24,
                EGL_SURFACE_TYPE,    EGL_WINDOW_BIT,
                EGL_RENDERABLE_TYPE, EGL_OPENGL_BIT,
                EGL_NONE,
            };
            var configs = new IntPtr[1];
            if (!eglChooseConfig(eglDpy, cfgAttribs, configs, 1, out int n) || n < 1)
                return null;
            var cfg = configs[0];

            // ── Native window ────────────────────────────────────────────────
            nint nativeWin;
            if (isWayland)
            {
                // Wayland requires a wl_egl_window wrapping the wl_surface.
                var wlEglWin = wl_egl_window_create(
                    wlSurface, Math.Max(1, width), Math.Max(1, height));
                if (wlEglWin == IntPtr.Zero) return null;
                outWlEglWindow = wlEglWin;
                nativeWin = (nint)wlEglWin;
            }
            else
            {
                // X11: the XWindow is the native window directly.
                nativeWin = (nint)xWindow;
            }

            // ── EGL surface ──────────────────────────────────────────────────
            var eglSurf = eglCreateWindowSurface(eglDpy, cfg, nativeWin, null);
            if (eglSurf == IntPtr.Zero) return null;
            outEglSurface = eglSurf;

            // ── EGL context (OpenGL 3.3 Core) ─────────────────────────────────
            // Veldrid requires at least OpenGL 3.0; request 3.3 which is
            // universally available on mesa and proprietary drivers since ~2012.
            int[] ctxAttribs =
            {
                EGL_CONTEXT_MAJOR_VERSION, 3,
                EGL_CONTEXT_MINOR_VERSION, 3,
                EGL_NONE,
            };
            var eglCtx = eglCreateContext(eglDpy, cfg, IntPtr.Zero, ctxAttribs);
            if (eglCtx == IntPtr.Zero) return null;
            outEglContext = eglCtx;

            if (!eglMakeCurrent(eglDpy, eglSurf, eglSurf, eglCtx)) return null;

            // Capture stable values for closures.
            var capDpy  = eglDpy;
            var capSurf = eglSurf;
            var capCtx  = eglCtx;

            return new OpenGLPlatformInfo(
                openGLContextHandle:    capCtx,
                getProcAddress:         name   => eglGetProcAddress(name),
                makeCurrent:            handle => eglMakeCurrent(capDpy, capSurf, capSurf, handle),
                getCurrentContext:      ()     => eglGetCurrentContext(),
                clearCurrentContext:    ()     => eglMakeCurrent(capDpy, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero),
                deleteContext:          handle => eglDestroyContext(capDpy, handle),
                swapBuffers:            ()     => eglSwapBuffers(capDpy, capSurf),
                setSyncToVerticalBlank: _      => { });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Releases EGL resources created by <see cref="TryCreateEglPlatformInfo"/>.</summary>
    public static void DestroyEglContext(
        IntPtr eglDisplay, IntPtr eglContext, IntPtr eglSurface, IntPtr wlEglWindow)
    {
        try
        {
            if (eglDisplay != IntPtr.Zero)
            {
                eglMakeCurrent(eglDisplay, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (eglContext != IntPtr.Zero) eglDestroyContext(eglDisplay, eglContext);
                if (eglSurface != IntPtr.Zero) eglDestroySurface(eglDisplay, eglSurface);
                eglTerminate(eglDisplay);
            }
        }
        catch { /* best-effort */ }
        finally
        {
            if (wlEglWindow != IntPtr.Zero)
            {
                try { wl_egl_window_destroy(wlEglWindow); } catch { }
            }
        }
    }

    /// <summary>
    /// Resizes the <c>wl_egl_window</c> when the surface size changes on Wayland.
    /// This must be called before presenting a new frame at a different size.
    /// No-op if <paramref name="wlEglWindow"/> is <see cref="IntPtr.Zero"/>.
    /// </summary>
    public static void ResizeWlEglWindow(IntPtr wlEglWindow, int width, int height)
    {
        if (wlEglWindow != IntPtr.Zero)
        {
            try { wl_egl_window_resize(wlEglWindow, Math.Max(1, width), Math.Max(1, height), 0, 0); }
            catch { }
        }
    }
}
