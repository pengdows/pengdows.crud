using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

public class NativeClientEnvironmentTests
{
    private const string BaseDirectory = "/work/bin/Release/net10.0/";

    // Built the way the implementation builds them: Path.Combine uses the OS separator, so a literal
    // "/work/.../native/lib" never matches on Windows.
    private static readonly string NativeLib = NativeClientEnvironment.InformixNativeLibDirectory(BaseDirectory);
    private static readonly string NativeRoot = NativeClientEnvironment.InformixNativeRoot(BaseDirectory);

    // Marks "use the default"; NativeRoot is no longer a compile-time constant, so it can't be a default value.
    private const string DefaultInformixDir = "\0default";

    private static string? Check(SupportedDatabase provider, string? ldLibraryPath,
        string? informixDir = DefaultInformixDir,
        string? sqlHosts = "/tmp/pengdows-informix-sqlhosts") =>
        NativeClientEnvironment.GetMissingEnvironmentError(provider, BaseDirectory, name => name switch
        {
            "LD_LIBRARY_PATH" => ldLibraryPath,
            "INFORMIXDIR" => informixDir == DefaultInformixDir ? NativeRoot : informixDir,
            "INFORMIXSQLHOSTS" => sqlHosts,
            _ => null
        });

    [Fact]
    public void Informix_WithoutNativeLibDirectory_ReportsTheDirectoryToExport()
    {
        var error = Check(SupportedDatabase.Informix, "/usr/lib");

        Assert.NotNull(error);
        Assert.Contains("LD_LIBRARY_PATH", error);
        Assert.Contains(NativeLib, error);
    }

    [Fact]
    public void Informix_WithUnsetLibraryPath_ReportsTheDirectoryToExport()
    {
        var error = Check(SupportedDatabase.Informix, null);

        Assert.NotNull(error);
        Assert.Contains(NativeLib, error);
    }

    [Fact]
    public void Informix_WithoutInformixDir_ReportsTheDirectoryToExport()
    {
        var error = Check(SupportedDatabase.Informix, NativeLib, informixDir: null);

        Assert.NotNull(error);
        Assert.Contains("INFORMIXDIR", error);
        Assert.Contains(NativeRoot, error);
    }

    [Fact]
    public void Informix_WithoutSqlHosts_ReportsTheVariableToExport()
    {
        var error = Check(SupportedDatabase.Informix, NativeLib, sqlHosts: null);

        Assert.NotNull(error);
        Assert.Contains("INFORMIXSQLHOSTS", error);
    }

    [Fact]
    public void Informix_WithEverythingExported_ReportsNothing()
    {
        // The path-list separator is ':' on Linux but ';' on Windows.
        Assert.Null(Check(SupportedDatabase.Informix, "/usr/lib" + Path.PathSeparator + NativeLib));
    }

    [Theory]
    [InlineData(SupportedDatabase.Db2)]
    [InlineData(SupportedDatabase.PostgreSql)]
    public void OtherProviders_NeedNothingExported(SupportedDatabase provider)
    {
        Assert.Null(Check(provider, null, null, null));
    }
}
