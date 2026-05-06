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

        new Application(new Eto.GtkSharp.Platform()).Run(new MainForm());
    }

    /// <summary>
    /// Installs a <see cref="NativeLibrary.SetDllImportResolver"/> resolver for
    /// every currently loaded assembly and hooks <see cref="AppDomain.AssemblyLoad"/>
    /// to install it for assemblies loaded later (e.g. Veldrid, NativeLibraryLoader).
    /// </summary>
    static void InstallLibDlResolver()
    {
        void TryRegister(Assembly asm)
        {
            try
            {
                NativeLibrary.SetDllImportResolver(asm, static (name, assembly, path) =>
                {
                    // Redirect both the unversioned "libdl" / "dl" names that
                    // older P/Invoke code uses to the versioned stub.
                    if (name is "libdl" or "dl")
                    {
                        if (NativeLibrary.TryLoad("libdl.so.2", assembly, path, out var h)) return h;
                        if (NativeLibrary.TryLoad("libc.so.6",  assembly, path, out h))     return h;
                    }
                    return IntPtr.Zero;
                });
            }
            catch
            {
                // SetDllImportResolver throws InvalidOperationException if a
                // resolver is already registered for this assembly, and
                // ArgumentException for dynamic assemblies.  Both are safe to ignore.
            }
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            TryRegister(asm);

        AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryRegister(e.LoadedAssembly);
    }
}
