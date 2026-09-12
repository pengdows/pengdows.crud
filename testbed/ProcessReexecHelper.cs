namespace testbed;

/// <summary>
/// Pure decision logic for building a "re-exec the same process" argument list — extracted from
/// <c>InformixNativeLibraryBootstrap.ReexecWithCorrectedEnvironment</c> so it's unit-testable
/// without actually spawning a process.
/// </summary>
/// <remarks>
/// <c>Process.GetCurrentProcess().MainModule.FileName</c> (the actual OS process image) and
/// <c>Environment.GetCommandLineArgs()[0]</c> (the CLR's notion of argv[0]) are the SAME path when
/// the process was launched via a native apphost — e.g. <c>dotnet run --project testbed</c>
/// produces and launches the <c>testbed</c> apphost binary directly, so both report that binary's
/// own path. They are DIFFERENT when the process was launched via the "dotnet" muxer executing a
/// managed assembly directly (<c>dotnet exec some.dll args...</c>, or <c>dotnet
/// some.dll args...</c>) — there, <c>MainModule.FileName</c> is the muxer's own path ("dotnet"),
/// while <c>GetCommandLineArgs()[0]</c> is the managed assembly's path. vstest's testhost is
/// always launched this second way. Blindly skipping <c>GetCommandLineArgs()[0]</c> (as if it
/// were always just "argv[0], the exe name, ignore it") silently drops the assembly path needed
/// to re-launch a muxer-hosted process, producing an invocation like
/// <c>dotnet --port 1234 ...</c> with no assembly to run — which the dotnet CLI rejects with
/// "Could not execute because the specified command or file was not found.", crashing the entire
/// process (for vstest, the entire test run, not just one test).
/// </remarks>
public static class ProcessReexecHelper
{
    /// <summary>
    /// Builds the argument list to pass to <c>processPath</c> (as <c>ProcessStartInfo.FileName</c>)
    /// to re-launch the current process with the same effective command line.
    /// </summary>
    /// <param name="processPath">
    /// <c>Process.GetCurrentProcess().MainModule?.FileName</c> — the actual OS process image path.
    /// <see langword="null"/> or empty falls back to the apphost assumption (the previously
    /// unconditional behavior), since there's no path to compare against.
    /// </param>
    /// <param name="commandLineArgs"><c>Environment.GetCommandLineArgs()</c>, unmodified.</param>
    public static IReadOnlyList<string> BuildReexecArguments(string? processPath, IReadOnlyList<string> commandLineArgs)
    {
        if (commandLineArgs.Count == 0)
        {
            return Array.Empty<string>();
        }

        var entryPoint = commandLineArgs[0];
        var rest = commandLineArgs.Skip(1);

        var launchedViaMuxer = !string.IsNullOrEmpty(processPath) &&
                                !string.Equals(entryPoint, processPath, StringComparison.Ordinal);

        return launchedViaMuxer
            ? new[] { entryPoint }.Concat(rest).ToArray()
            : rest.ToArray();
    }

    /// <summary>
    /// True when re-exec cannot reliably work at all, regardless of <see cref="BuildReexecArguments"/>'s
    /// fix: vstest's testhost is launched as
    /// <c>dotnet exec --runtimeconfig &lt;test-project&gt;.runtimeconfig.json --depsfile &lt;test-project&gt;.deps.json testhost.dll --port ...</c> —
    /// confirmed live via <c>ps aux</c> against a real <c>dotnet test</c> run. The muxer consumes
    /// <c>--runtimeconfig</c>/<c>--depsfile</c> itself before the managed entry point ever runs, so
    /// they never appear in <see cref="Environment.GetCommandLineArgs"/> — there is no way for
    /// managed code to recover them, so any re-exec attempt here will launch <c>testhost.dll</c>
    /// with no runtimeconfig override, which fails to find its own (nonexistent)
    /// <c>testhost.runtimeconfig.json</c> next to it and crashes with "A fatal error was
    /// encountered. The library 'libhostpolicy.so' ... was not found". Detect this specific,
    /// unrecoverable shape by the distinctive "testhost.dll" entry point name and fail with a
    /// clear, catchable exception instead of attempting (and worse, partially succeeding at) a
    /// re-exec that corrupts the whole vstest run when it fails.
    /// </summary>
    public static bool IsUnrecoverableTestHostLaunch(IReadOnlyList<string> commandLineArgs)
    {
        return commandLineArgs.Count > 0 &&
               string.Equals(Path.GetFileName(commandLineArgs[0]), "testhost.dll", StringComparison.OrdinalIgnoreCase);
    }
}
