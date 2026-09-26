using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

public class NativeClientEnvironmentTests
{
    private const string BaseDirectory = "/work/bin/Release/net10.0/";
    private const string NativeLib = "/work/bin/Release/net10.0/native/lib";
    private const string NativeRoot = "/work/bin/Release/net10.0/native";

    private static string? Check(SupportedDatabase provider, string? ldLibraryPath, string? informixDir = NativeRoot,
        string? sqlHosts = "/tmp/pengdows-informix-sqlhosts") =>
        NativeClientEnvironment.GetMissingEnvironmentError(provider, BaseDirectory, name => name switch
        {
            "LD_LIBRARY_PATH" => ldLibraryPath,
            "INFORMIXDIR" => informixDir,
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
        Assert.Null(Check(SupportedDatabase.Informix, "/usr/lib:" + NativeLib));
    }

    [Theory]
    [InlineData(SupportedDatabase.Db2)]
    [InlineData(SupportedDatabase.PostgreSql)]
    public void OtherProviders_NeedNothingExported(SupportedDatabase provider)
    {
        Assert.Null(Check(provider, null, null, null));
    }
}
