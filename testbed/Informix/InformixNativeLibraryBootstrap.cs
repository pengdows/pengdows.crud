using System.Diagnostics;
using System.Runtime.InteropServices;
using Informix.Net.Core;

namespace testbed.Informix;

/// <summary>
/// Registers native-library resolution for Informix.Net.Core-lnx (libthcli15a.so). Must run
/// before ANY access to <see cref="InformixClientFactory"/> — including reflection-based
/// discovery in <c>DbProviderFactoryFinder</c> — since simply reading
/// <see cref="InformixClientFactory.Instance"/> can trigger the driver's own native
/// initialization. Call <see cref="Register"/> as the very first statement in Program.cs,
/// before <c>DbProviderFactoryFinder.FindAllFactories()</c> runs — same requirement, same
/// reason, as <c>Db2NativeLibraryBootstrap</c>.
/// </summary>
/// <remarks>
/// <c>libthcli15a.so</c> was confirmed as the managed assembly's actual P/Invoke target by
/// disassembling <c>Informix.Net.Core.dll</c> directly (not assumed from the package's file
/// list, which ships ~35 other .so files, one of which — <c>libifgls.so</c> — is
/// libthcli15a.so's own real link-time dependency per <c>ldd</c>, the rest being siblings it
/// does not directly need).
/// <para>
/// <b>Confirmed live (real, non-obvious bug, not assumed):</b> setting <c>LD_LIBRARY_PATH</c>
/// via <see cref="Environment.SetEnvironmentVariable"/> from WITHIN an already-running .NET
/// process does NOT make glibc's dynamic linker honor it for a subsequent <c>dlopen()</c>'s
/// own dependency resolution — this is a genuine, reproducible Linux/glibc limitation
/// (LD_LIBRARY_PATH must be present in the environment at process start, not set mid-process),
/// confirmed by direct reproduction: loading <c>libthcli15a.so</c> by absolute path still
/// failed with "libifgls.so: cannot open shared object file" even with
/// <c>Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", ...)</c> called first (and even
/// after explicitly pre-loading <c>libifgls.so</c> itself via <c>NativeLibrary.Load</c> — that
/// didn't help either). The SAME load succeeded immediately once <c>LD_LIBRARY_PATH</c> was set
/// in the shell BEFORE <c>dotnet run</c> even started. <c>Db2NativeLibraryBootstrap</c>'s own
/// claim that its mid-process <c>SetEnvironmentVariable</c> call is sufficient is therefore
/// unverified/likely accidental — Db2's dependencies are probably resolved via a baked-in
/// RPATH instead, not LD_LIBRARY_PATH. Do not copy that pattern for a new database without
/// checking this exact failure mode first.
/// </para>
/// <para>
/// The fix: if <c>LD_LIBRARY_PATH</c> doesn't already contain the native lib directory when
/// this process starts, re-exec the SAME process (same executable, same args) with a corrected
/// environment, wait for it, and propagate its exit code — guaranteeing the dynamic linker sees
/// the correct value from true process start on the second (re-exec'd) run.
/// </para>
/// CONFIRMED live: the package ships a full CSDK directory layout under native/ (gls/, msg/,
/// etc/, lib/, bin/, release/, license/) — the same shape setup.odbc's own template expects
/// under INFORMIXDIR (defaulting there to /usr/informix, which does not exist in this
/// environment). Error "-23101 Unspecified System Error" is CSDK's generic failure code for,
/// among other things, a client that can't locate its own GLS locale/message-catalog files —
/// this is set unconditionally via Environment.SetEnvironmentVariable (a plain getenv() lookup,
/// not the dynamic linker's own startup-time LD_LIBRARY_PATH resolution, so no re-exec is
/// needed for this one).
/// </remarks>
internal static class InformixNativeLibraryBootstrap
{
    private const string ReexecMarker = "__PENGDOWS_INFORMIX_REEXEC__";
    private static bool _registered;

    /// <summary>
    /// Fixed path for the generated sqlhosts file. CONFIRMED live: setting
    /// <c>INFORMIXSQLHOSTS</c> to a path computed only once the test container's dynamic host
    /// port is known (i.e. inside <c>InformixTestContainer.StartAsync</c>, well after
    /// <see cref="Register"/> has already run) has NO effect — same class of bug as
    /// <c>LD_LIBRARY_PATH</c>: the driver appears to read/cache its environment at first native
    /// contact (during factory discovery, before any container exists), not per-connection. The
    /// fix is to fix the PATH here, early, and let <c>InformixTestContainer</c> rewrite that
    /// same file's contents later once the real port is known — only the env var's value needs
    /// to be stable from first contact onward, not the file's content.
    /// </summary>
    public static readonly string SqlHostsPath = Path.Combine(Path.GetTempPath(), "pengdows-informix-sqlhosts");

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;

        var baseDir = AppContext.BaseDirectory;
        var nativeRoot = Path.Combine(baseDir, "native");
        var nativeLib = Path.Combine(nativeRoot, "lib");

        Environment.SetEnvironmentVariable("INFORMIXDIR", nativeRoot);
        Environment.SetEnvironmentVariable("INFORMIXSQLHOSTS", SqlHostsPath);
        if (!File.Exists(SqlHostsPath))
        {
            File.WriteAllText(SqlHostsPath, string.Empty);
        }

        var existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        var alreadyCorrect = Environment.GetEnvironmentVariable(ReexecMarker) == "1"
                              || (existing?.Split(Path.PathSeparator).Contains(nativeLib) ?? false);

        if (!alreadyCorrect)
        {
            ReexecWithCorrectedEnvironment(nativeRoot, nativeLib, existing);
            // ReexecWithCorrectedEnvironment never returns — it calls Environment.Exit
            // with the child process's exit code once it completes.
        }

        // Belt-and-suspenders: even with LD_LIBRARY_PATH now correctly set from true process
        // start, keep the explicit absolute-path resolver too — it's a no-op once the default
        // probing already finds the library via LD_LIBRARY_PATH, and a safety net if it doesn't.
        NativeLibrary.SetDllImportResolver(typeof(InformixClientFactory).Assembly, (libraryName, _, _) =>
        {
            if (libraryName == "libthcli15a.so")
            {
                var fullPath = Path.Combine(nativeLib, "libthcli15a.so");
                if (File.Exists(fullPath) && NativeLibrary.TryLoad(fullPath, out var handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero; // fall through to default resolution
        });
    }

    private static void ReexecWithCorrectedEnvironment(string nativeRoot, string nativeLib, string? existingLdLibraryPath)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
        {
            // Can't determine how to re-launch ourselves — fall through and hope the
            // DllImportResolver's direct absolute-path load below is enough on its own (it
            // won't be, for libthcli15a.so's own transitive libifgls.so dependency, but this
            // is a better degraded outcome than crashing here).
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false
        };

        foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment["LD_LIBRARY_PATH"] = string.IsNullOrEmpty(existingLdLibraryPath)
            ? nativeLib
            : $"{nativeLib}{Path.PathSeparator}{existingLdLibraryPath}";
        psi.Environment["INFORMIXDIR"] = nativeRoot;
        psi.Environment["INFORMIXSQLHOSTS"] = SqlHostsPath;
        psi.Environment[ReexecMarker] = "1";

        using var child = Process.Start(psi)
                           ?? throw new InvalidOperationException("Failed to re-exec with corrected LD_LIBRARY_PATH.");
        child.WaitForExit();
        Environment.Exit(child.ExitCode);
    }
}
