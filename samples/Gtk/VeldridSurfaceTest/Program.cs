using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Eto.Forms;

namespace VeldridSurfaceTest;

static class Program
{
    public static void Main(string[] args)
    {
        // On modern Linux (glibc ≥ 2.34) libdl was merged into libc.so.6.
        // Only the versioned stub libdl.so.2 exists; there is no unversioned
        // libdl.so file.  Veldrid's Vulkan back-end loads Vulkan entry-points
        // via NativeLibraryLoader which P/Invokes "libdl", causing a
        // TypeInitializationException on affected distributions (Arch, CachyOS,
        // etc.).  Register a resolver that redirects the name to libdl.so.2 —
        // or libc.so.6 as a final fallback — for every assembly.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            InstallLibDlResolver();

        var app = new Application(new Eto.GtkSharp.Platform());

        // Now that Eto.GtkSharp is fully loaded, also plug the shim into
        // Eto.Gtk's own DllImportResolverManager so any libdl calls routed
        // through that chain are covered too.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            RegisterLibDlWithEtoGtk();

        app.Run(new MainForm());
    }

    // The libdl resolver lambda — stored so the same delegate instance can be
    // fed into both NativeLibrary.SetDllImportResolver and
    // Eto.GtkSharp.DllImportResolverManager.Add().
    static readonly DllImportResolver s_libDlResolver = static (name, assembly, path) =>
    {
        // Redirect both the unversioned "libdl" / "dl" names that older
        // P/Invoke code uses to the versioned stub present on modern glibc.
        if (name is "libdl" or "dl")
        {
            if (NativeLibrary.TryLoad("libdl.so.2", assembly, path, out var h)) return h;
            if (NativeLibrary.TryLoad("libc.so.6",  assembly, path, out h))     return h;
        }
        return IntPtr.Zero;
    };

    /// <summary>
    /// Installs the libdl shim resolver for every currently-loaded assembly
    /// and hooks <see cref="AppDomain.AssemblyLoad"/> for assemblies loaded
    /// later (e.g. Veldrid, NativeLibraryLoader).
    ///
    /// Eto.Gtk assemblies are deliberately skipped: they own their resolver
    /// slot via <c>Eto.GtkSharp.DllImportResolverManager</c> which calls
    /// <see cref="NativeLibrary.SetDllImportResolver"/> in its own static
    /// constructor.  Registering ours on the same assembly first would cause
    /// <c>DllImportResolverManager..cctor()</c> to throw
    /// <see cref="InvalidOperationException"/> at start-up.
    /// Instead we plug into its resolver chain via <c>Add()</c> after the
    /// platform is initialised.
    /// </summary>
    static void InstallLibDlResolver()
    {
        // ── Per-assembly registration (for Veldrid etc.) ──────────────────
        void TryRegister(Assembly asm)
        {
            // Skip Eto.* assemblies — they manage their own DllImportResolver
            // slot through DllImportResolverManager and would crash if we
            // pre-empt them.
            var asmName = asm.GetName().Name;
            if (asmName != null && asmName.StartsWith("Eto", StringComparison.Ordinal))
                return;

            try { NativeLibrary.SetDllImportResolver(asm, s_libDlResolver); }
            catch
            {
                // InvalidOperationException  – another resolver is already set.
                // ArgumentException          – dynamic assembly.
                // Both are safe to ignore.
            }
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            TryRegister(asm);

        AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryRegister(e.LoadedAssembly);
    }

    /// <summary>
    /// Called by <see cref="MainForm"/> after the Eto.Gtk platform has been
    /// fully initialised, so that the libdl shim is also active for any
    /// native calls routed through <c>DllImportResolverManager</c>.
    /// </summary>
    internal static void RegisterLibDlWithEtoGtk()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;
        try
        {
            // DllImportResolverManager.Add() is internal to Eto.Gtk, so we
            // reach it via reflection to avoid a hard assembly reference that
            // would break non-Gtk builds.
            var managerType = Type.GetType(
                "Eto.GtkSharp.DllImportResolverManager, Eto.Gtk",
                throwOnError: false);
            var addMethod = managerType?.GetMethod(
                "Add",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(DllImportResolver) },
                null);
            addMethod?.Invoke(null, new object[] { s_libDlResolver });
        }
        catch
        {
            // Reflection failure is non-fatal; Veldrid assemblies already
            // have the resolver registered directly via SetDllImportResolver.
        }
    }
}
