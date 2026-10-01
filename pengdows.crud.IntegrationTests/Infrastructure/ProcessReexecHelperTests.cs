using testbed;
using Xunit;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Locks down <see cref="ProcessReexecHelper.BuildReexecArguments"/> — the fix for a real crash:
/// <c>InformixNativeLibraryBootstrap.ReexecWithCorrectedEnvironment</c> used to build its re-exec
/// argument list as <c>Environment.GetCommandLineArgs().Skip(1)</c> unconditionally, which only
/// works when the current process was launched via a native apphost (where
/// <c>GetCommandLineArgs()[0]</c> and <c>Process.GetCurrentProcess().MainModule.FileName</c> are
/// the same path — true for <c>dotnet run --project testbed</c>'s apphost). Under <c>dotnet test</c>,
/// vstest's testhost is launched via the "dotnet" muxer executing testhost.dll directly
/// (<c>dotnet exec testhost.dll --port ... </c>) — there, <c>MainModule.FileName</c> is the muxer's
/// own path ("dotnet") while <c>GetCommandLineArgs()[0]</c> is testhost.dll's path, a genuinely
/// different string. The old code's <c>Skip(1)</c> silently dropped testhost.dll from the
/// re-exec'd argument list, producing "dotnet --port 1234 ..." with no assembly to run — which the
/// dotnet CLI rejects with "Could not execute because the specified command or file was not
/// found.", crashing the entire vstest test run (not just one test). Confirmed live: this crash
/// reproduced by simply calling <c>ParallelTestOrchestrator.GetTestConfigurations()</c> (which
/// triggers Informix provider discovery, which calls <c>Register()</c>) from any xunit test.
/// </summary>
public sealed class ProcessReexecHelperTests
{
    [Fact]
    public void BuildReexecArguments_ApphostLaunch_EntryPointMatchesProcessPath_SkipsEntryPoint()
    {
        // dotnet run's apphost case: argv[0] IS the process path.
        var processPath = "/app/testbed/bin/Release/net8.0/testbed";
        var commandLineArgs = new[] { processPath, "--only", "PostgreSQL" };

        var result = ProcessReexecHelper.BuildReexecArguments(processPath, commandLineArgs);

        Assert.Equal(new[] { "--only", "PostgreSQL" }, result);
    }

    [Fact]
    public void BuildReexecArguments_MuxerLaunch_EntryPointDiffersFromProcessPath_PrependsEntryPoint()
    {
        // vstest's testhost case: processPath is the "dotnet" muxer; argv[0] is testhost.dll's
        // own path, which the muxer needs back as its first argument or it has nothing to run.
        var processPath = "/usr/share/dotnet/dotnet";
        var commandLineArgs = new[]
        {
            "/home/user/.nuget/packages/microsoft.testplatform.testhost/17.14.0/lib/net8.0/testhost.dll",
            "--port", "12345", "--endpoint", "127.0.0.1:012345", "--parentprocessid", "999"
        };

        var result = ProcessReexecHelper.BuildReexecArguments(processPath, commandLineArgs);

        Assert.Equal(new[]
        {
            "/home/user/.nuget/packages/microsoft.testplatform.testhost/17.14.0/lib/net8.0/testhost.dll",
            "--port", "12345", "--endpoint", "127.0.0.1:012345", "--parentprocessid", "999"
        }, result);
    }

    [Fact]
    public void BuildReexecArguments_NullProcessPath_FallsBackToSkippingEntryPoint()
    {
        // Process.GetCurrentProcess().MainModule can be null/inaccessible in some hosts — treat
        // that the same as the apphost case (the old, already-working default) rather than
        // guessing at muxer behavior with no process path to compare against.
        var commandLineArgs = new[] { "/app/testbed", "--only", "MySQL" };

        var result = ProcessReexecHelper.BuildReexecArguments(null, commandLineArgs);

        Assert.Equal(new[] { "--only", "MySQL" }, result);
    }

    [Fact]
    public void BuildReexecArguments_NoArguments_ReturnsEmpty()
    {
        var result = ProcessReexecHelper.BuildReexecArguments("/app/testbed", Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public void BuildReexecArguments_MuxerLaunch_NoTrailingArgs_StillPrependsEntryPoint()
    {
        var processPath = "/usr/share/dotnet/dotnet";
        var commandLineArgs = new[] { "/path/to/testhost.dll" };

        var result = ProcessReexecHelper.BuildReexecArguments(processPath, commandLineArgs);

        Assert.Equal(new[] { "/path/to/testhost.dll" }, result);
    }

    // ── IsUnrecoverableTestHostLaunch ────────────────────────────────────────
    // Confirmed live: even with BuildReexecArguments' fix applied, re-executing vstest's testhost
    // still crashes ("A fatal error was encountered. The library 'libhostpolicy.so' ... was not
    // found ... because 'testhost.runtimeconfig.json' was not found") because vstest launches it
    // as `dotnet exec --runtimeconfig <test-project>.runtimeconfig.json --depsfile
    // <test-project>.deps.json testhost.dll --port ...` — flags the muxer consumes itself before
    // Main ever runs, so Environment.GetCommandLineArgs() never sees them and no re-exec can
    // recover them. This predicate exists so the bootstrap can fail with a clear, catchable
    // exception in exactly this one unrecoverable shape, instead of attempting a re-exec that
    // corrupts the whole vstest run when it fails partway through.

    [Fact]
    public void IsUnrecoverableTestHostLaunch_TestHostDllEntryPoint_ReturnsTrue()
    {
        var commandLineArgs = new[]
        {
            "/home/user/.nuget/packages/microsoft.testplatform.testhost/17.14.0/lib/net8.0/testhost.dll",
            "--port", "12345"
        };

        Assert.True(ProcessReexecHelper.IsUnrecoverableTestHostLaunch(commandLineArgs));
    }

    [Fact]
    public void IsUnrecoverableTestHostLaunch_CaseInsensitive_ReturnsTrue()
    {
        var commandLineArgs = new[] { "/path/to/TestHost.DLL", "--port", "12345" };

        Assert.True(ProcessReexecHelper.IsUnrecoverableTestHostLaunch(commandLineArgs));
    }

    [Fact]
    public void IsUnrecoverableTestHostLaunch_ApphostLaunch_ReturnsFalse()
    {
        var commandLineArgs = new[] { "/app/testbed/bin/Release/net8.0/testbed", "--only", "PostgreSQL" };

        Assert.False(ProcessReexecHelper.IsUnrecoverableTestHostLaunch(commandLineArgs));
    }

    [Fact]
    public void IsUnrecoverableTestHostLaunch_OrdinaryMuxerLaunch_ReturnsFalse()
    {
        // A plain `dotnet exec testbed.dll args` invocation IS recoverable via
        // BuildReexecArguments — only testhost.dll's specific hidden-flags shape is not.
        var commandLineArgs = new[] { "/app/testbed/bin/Release/net8.0/testbed.dll", "--only", "PostgreSQL" };

        Assert.False(ProcessReexecHelper.IsUnrecoverableTestHostLaunch(commandLineArgs));
    }

    [Fact]
    public void IsUnrecoverableTestHostLaunch_NoArguments_ReturnsFalse()
    {
        Assert.False(ProcessReexecHelper.IsUnrecoverableTestHostLaunch(Array.Empty<string>()));
    }
}
