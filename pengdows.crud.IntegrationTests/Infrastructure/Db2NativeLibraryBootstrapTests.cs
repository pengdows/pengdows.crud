using System.Runtime.InteropServices;
using IBM.Data.Db2;
using testbed.Db2;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// <c>dotnet test</c> loads this assembly in a host that never runs the testbed's Program.cs, so the shared
/// bootstrap registers itself from a module initializer. These tests need no Db2 server: they check that the
/// registration has already happened by the time any test runs, and that asking again changes nothing.
/// </summary>
public class Db2NativeLibraryBootstrapTests
{
    private static string ClidriverLib => Path.Combine(AppContext.BaseDirectory, "clidriver", "lib");

    private static int OccurrencesOnLdLibraryPath(string directory) =>
        (Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? string.Empty)
        .Split(Path.PathSeparator)
        .Count(entry => entry == directory);

    // One test, in this order: the first two checks must see the state the module initializer left, before
    // any explicit Register call below could mask a missing initializer. Another test in this assembly that
    // starts a Db2 container also calls Register, so the first checks only prove the initializer when this
    // test runs before it; the repeat call checks idempotency either way.
    [Fact]
    public void TheModuleInitializerRegistersOnce_AndRegisteringAgainChangesNothing()
    {
        var expectedOccurrences = OperatingSystem.IsLinux() ? 1 : 0;

        Assert.Equal(expectedOccurrences, OccurrencesOnLdLibraryPath(ClidriverLib));

        // NativeLibrary permits one resolver per assembly, so a second registration throws exactly when
        // the first one happened.
        if (OperatingSystem.IsLinux())
        {
            Assert.Throws<InvalidOperationException>(() =>
                NativeLibrary.SetDllImportResolver(typeof(DB2Factory).Assembly, (_, _, _) => IntPtr.Zero));
        }

        Db2NativeLibraryBootstrap.Register();
        Db2NativeLibraryBootstrap.Register();

        Assert.Equal(expectedOccurrences, OccurrencesOnLdLibraryPath(ClidriverLib));
    }
}
