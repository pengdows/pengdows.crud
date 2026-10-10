using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using IBM.Data.Db2;

namespace testbed.Db2;

/// <summary>
/// Registers native-library resolution for IBM.Data.Db2 (libdb2.so). Must run before ANY
/// access to <see cref="DB2Factory"/> — including reflection-based discovery in
/// <c>DbProviderFactoryFinder</c> — since simply reading <c>DB2Factory.Instance</c> can trigger
/// the driver's own native initialization. Call <see cref="Register"/> as the very first
/// statement in Program.cs, before <c>DbProviderFactoryFinder.FindAllFactories()</c> runs.
/// The integration-test project compiles this same file: <c>dotnet test</c> loads it in a separate host
/// that never runs <c>Program.cs</c>, so the module initializer registers the resolver at assembly load.
/// </summary>
internal static class Db2NativeLibraryBootstrap
{
    private static bool _registered;

    [ModuleInitializer]
    internal static void InitializeAtAssemblyLoad() => Register();

    public static void Register()
    {
        // libdb2.so and its clidriver tree are the Linux driver; other platforms use their own.
        if (_registered || !OperatingSystem.IsLinux())
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
        // LD_LIBRARY_PATH is still set for libdb2.so's OWN transitive dependencies (ICU/SSL libs
        // in clidriver/lib/icc), but glibc reads it only at process start (see
        // InformixNativeLibraryBootstrap's remarks), so setting it here likely has no effect on
        // that resolution in this process.
        var baseDir = AppContext.BaseDirectory;
        var clidriverLib = Path.Combine(baseDir, "clidriver", "lib");
        var clidriverIcc = Path.Combine(clidriverLib, "icc");

        // Every assembly that compiles this file has its own copy of the guard above, so the environment is the
        // only state they share: add a directory only when no copy has added it already.
        var existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        var present = (existing ?? string.Empty).Split(Path.PathSeparator);
        var additions = new[] { clidriverLib, clidriverIcc }.Where(dir => !present.Contains(dir)).ToList();
        if (additions.Count > 0)
        {
            var added = string.Join(Path.PathSeparator, additions);
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH",
                string.IsNullOrEmpty(existing) ? added : $"{added}{Path.PathSeparator}{existing}");
        }

        try
        {
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
        catch (InvalidOperationException)
        {
            // A vstest host may already have registered the resolver from the integration-test
            // assembly's module initializer. NativeLibrary permits only one resolver per assembly;
            // the existing resolver is sufficient for this shared provider assembly.
        }
    }
}
