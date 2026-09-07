using System.Runtime.InteropServices;
using IBM.Data.Db2;

namespace testbed.Db2;

/// <summary>
/// Registers native-library resolution for IBM.Data.Db2 (libdb2.so). Must run before ANY
/// access to <see cref="DB2Factory"/> — including reflection-based discovery in
/// <c>DbProviderFactoryFinder</c> — since simply reading <c>DB2Factory.Instance</c> can trigger
/// the driver's own native initialization. Call <see cref="Register"/> as the very first
/// statement in Program.cs, before <c>DbProviderFactoryFinder.FindAllFactories()</c> runs.
/// </summary>
internal static class Db2NativeLibraryBootstrap
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;

        // The Net.IBM.Data.Db2-lnx package copies its clidriver/ tree (including libdb2.so)
        // into the build output directory. Setting LD_LIBRARY_PATH alone is NOT sufficient:
        // .NET Core's own native-library probing (deps.json runtimes/{rid}/native, app dir,
        // shared framework dir) runs first and does not consult LD_LIBRARY_PATH at all — it
        // fails with DllNotFoundException before ever reaching an OS-level dlopen() that would
        // honor the environment variable. The reliable fix is a custom DllImportResolver that
        // loads libdb2.so from its absolute path directly, bypassing that probing entirely.
        // LD_LIBRARY_PATH is still set so libdb2.so's OWN transitive dependencies (ICU/SSL libs
        // in clidriver/lib/icc) resolve correctly — that inner resolution IS a genuine OS-level
        // dlopen() and does honor the environment variable.
        var baseDir = AppContext.BaseDirectory;
        var clidriverLib = Path.Combine(baseDir, "clidriver", "lib");
        var clidriverIcc = Path.Combine(clidriverLib, "icc");

        var existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        var combined = string.IsNullOrEmpty(existing)
            ? $"{clidriverLib}{Path.PathSeparator}{clidriverIcc}"
            : $"{clidriverLib}{Path.PathSeparator}{clidriverIcc}{Path.PathSeparator}{existing}";
        Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", combined);

        NativeLibrary.SetDllImportResolver(typeof(DB2Factory).Assembly, (libraryName, _, _) =>
        {
            if (libraryName == "libdb2.so")
            {
                var fullPath = Path.Combine(clidriverLib, "libdb2.so");
                if (File.Exists(fullPath) && NativeLibrary.TryLoad(fullPath, out var handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero; // fall through to default resolution
        });
    }
}
