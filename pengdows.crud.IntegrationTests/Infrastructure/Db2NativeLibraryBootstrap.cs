using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using IBM.Data.Db2;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Registers IBM Db2's native resolver in the vstest process. The executable testbed has its own
/// bootstrap, but dotnet test loads this assembly in a separate host and does not run testbed.Program.
/// </summary>
internal static class Db2NativeLibraryBootstrap
{
    private static bool _registered;

    [ModuleInitializer]
    internal static void InitializeAtAssemblyLoad() => Register();

    private static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        var clidriverLib = Path.Combine(AppContext.BaseDirectory, "clidriver", "lib");
        var clidriverIcc = Path.Combine(clidriverLib, "icc");
        var existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        Environment.SetEnvironmentVariable(
            "LD_LIBRARY_PATH",
            string.IsNullOrEmpty(existing)
                ? $"{clidriverLib}{Path.PathSeparator}{clidriverIcc}"
                : $"{clidriverLib}{Path.PathSeparator}{clidriverIcc}{Path.PathSeparator}{existing}");

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

            return IntPtr.Zero;
        });
    }
}
