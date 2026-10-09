using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Environment a provider's native client needs before the test host starts. Informix.Net.Core-lnx
/// loads <c>libthcli15a.so</c>, whose own dependency (<c>libifgls.so</c>) glibc resolves through
/// <c>LD_LIBRARY_PATH</c> read at process start only, and the client reads <c>INFORMIXDIR</c> (its
/// GLS/message files) and <c>INFORMIXSQLHOSTS</c> through native getenv(), which never sees
/// <c>Environment.SetEnvironmentVariable</c> on Unix. The testbed re-executes itself with all three
/// (<c>InformixNativeLibraryBootstrap</c>); vstest's testhost cannot, so they must be exported before
/// <c>dotnet test</c> starts (<c>run-integration-tests.sh</c> does). Db2's bootstrap loads
/// <c>libdb2.so</c> by absolute path, while the integration runner provisions the legacy libxml2/ICU
/// compatibility ABI that the packaged IBM driver requires when the host does not provide it.
/// </summary>
internal static class NativeClientEnvironment
{
    /// <summary>The CSDK root Informix.Net.Core-lnx copies under a build output directory.</summary>
    public static string InformixNativeRoot(string baseDirectory) => Path.Combine(baseDirectory, "native");

    /// <summary>The directory holding <c>libthcli15a.so</c> and its dependencies.</summary>
    public static string InformixNativeLibDirectory(string baseDirectory) =>
        Path.Combine(InformixNativeRoot(baseDirectory), "lib");

    /// <summary>
    /// Returns an error naming exactly what to export when <paramref name="provider"/>'s native client
    /// cannot work in this process, or null when nothing is missing.
    /// </summary>
    public static string? GetMissingEnvironmentError(SupportedDatabase provider, string baseDirectory,
        Func<string, string?> getEnvironmentVariable)
    {
        if (provider != SupportedDatabase.Informix)
        {
            return null;
        }

        var problems = new List<string>();
        var nativeLib = InformixNativeLibDirectory(baseDirectory);
        var ldLibraryPath = getEnvironmentVariable("LD_LIBRARY_PATH");
        var entries = (ldLibraryPath ?? string.Empty).Split(Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!entries.Contains(nativeLib, StringComparer.Ordinal))
        {
            problems.Add($"LD_LIBRARY_PATH must contain '{nativeLib}' (current: '{ldLibraryPath ?? "<unset>"}')");
        }

        if (string.IsNullOrWhiteSpace(getEnvironmentVariable("INFORMIXDIR")))
        {
            problems.Add($"INFORMIXDIR must be set, e.g. to '{InformixNativeRoot(baseDirectory)}'");
        }

        if (string.IsNullOrWhiteSpace(getEnvironmentVariable("INFORMIXSQLHOSTS")))
        {
            problems.Add("INFORMIXSQLHOSTS must be set to a writable file path the fixture can rewrite " +
                         "(e.g. /tmp/pengdows-informix-sqlhosts)");
        }

        if (problems.Count == 0)
        {
            return null;
        }

        return "Informix's native client reads its environment at process start, and vstest's testhost " +
               "cannot re-execute itself to set it. Export before 'dotnet test' (or run " +
               "./run-integration-tests.sh, which does): " + string.Join("; ", problems) + ".";
    }
}
