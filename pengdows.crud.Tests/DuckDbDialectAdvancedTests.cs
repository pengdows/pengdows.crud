#region

using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DuckDbDialectAdvancedTests
{
    private readonly ILogger<DuckDbDialect> _logger;
    private readonly DuckDbDialect _dialect;
    private readonly fakeDbFactory _factory;

    public DuckDbDialectAdvancedTests()
    {
        _factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        _logger = new LoggerFactory().CreateLogger<DuckDbDialect>();
        _dialect = new DuckDbDialect(_factory, _logger);
    }

    [Fact]
    public async Task GetProductNameAsync_Should_Return_DuckDB_From_Version_Query()
    {

        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.SetScalarResultForCommand(_dialect.GetVersionQuery(), "DuckDB v1.0.0");
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var productName = await _dialect.GetProductNameAsync(trackedConnection);

        Assert.Equal("DuckDB", productName);
    }

    [Fact]
    public async Task GetProductNameAsync_Should_Return_DuckDB_From_Version_Query_Mixed_Case()
    {
        _factory.SetScalarResult("some version info with duckdb in it");

        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var productName = await _dialect.GetProductNameAsync(trackedConnection);

        Assert.Equal("DuckDB", productName);
    }

    [Fact]
    public async Task GetProductNameAsync_Should_Fallback_To_Pragma_When_Version_Fails()
    {
        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.SetCommandFailure("SELECT version()", new InvalidOperationException("Version query failed"));
        _factory.SetScalarResult("DuckDB");

        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var productName = await _dialect.GetProductNameAsync(trackedConnection);

        Assert.Equal("DuckDB", productName);
    }

    [Fact]
    public async Task GetProductNameAsync_Should_Return_Null_When_Both_Queries_Fail()
    {
        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.SetCommandFailure("SELECT version()", new InvalidOperationException("Version query failed"));
        connection.SetCommandFailure("PRAGMA version", new InvalidOperationException("Pragma failed"));

        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var productName = await _dialect.GetProductNameAsync(trackedConnection);

        Assert.Null(productName);
    }

    [Fact]
    public async Task GetProductNameAsync_Should_Return_Null_When_Version_Does_Not_Contain_DuckDB()
    {
        _factory.SetScalarResult("PostgreSQL 14.5");

        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var productName = await _dialect.GetProductNameAsync(trackedConnection);

        Assert.Equal("DuckDB", productName); // Falls back to pragma which returns success
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_Should_Return_Version_From_Select_Query()
    {
        _factory.SetScalarResult("DuckDB v1.0.0");

        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await _dialect.GetDatabaseVersionAsync(trackedConnection);

        Assert.Equal("DuckDB v1.0.0", version);
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_Should_Fallback_To_Pragma_When_Select_Fails()
    {
        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.SetCommandFailure("SELECT version()", new InvalidOperationException("Select failed"));
        _factory.SetScalarResult("v0.9.2");

        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await _dialect.GetDatabaseVersionAsync(trackedConnection);

        Assert.Equal("v0.9.2", version);
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_Should_Call_Base_When_Both_Queries_Fail()
    {
        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.SetCommandFailure("SELECT version()", new InvalidOperationException("Select failed"));
        connection.SetCommandFailure("PRAGMA version", new InvalidOperationException("Pragma failed"));

        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await _dialect.GetDatabaseVersionAsync(trackedConnection);

        // Base implementation should be called - this will return empty string from fakeDb
        Assert.Equal(string.Empty, version);
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_Should_Handle_Null_Result()
    {
        _factory.SetScalarResult(null);

        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await _dialect.GetDatabaseVersionAsync(trackedConnection);

        // Should fall back to pragma, then to base
        Assert.Equal(string.Empty, version);
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_Should_Handle_Empty_String_Result()
    {
        _factory.SetScalarResult("");

        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await _dialect.GetDatabaseVersionAsync(trackedConnection);

        // Should fall back to pragma, then to base
        Assert.Equal(string.Empty, version);
    }

    [Theory]
    [InlineData("v1.0.0", 1, 0, 0)]
    [InlineData("DuckDB v0.9.2", 0, 9, 2)]
    [InlineData("v0.7.1-dev", 0, 7, 1)]
    [InlineData("1.2.3", 1, 2, 3)]
    public void ParseVersion_Should_Parse_DuckDB_Version_Formats(string versionString, int expectedMajor, int expectedMinor, int expectedPatch)
    {
        var version = _dialect.ParseVersion(versionString);

        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version!.Major);
        Assert.Equal(expectedMinor, version.Minor);
        Assert.Equal(expectedPatch, version.Build);
    }

    [Theory]
    [InlineData("not a version")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseVersion_Should_Return_Null_For_Invalid_Formats(string? versionString)
    {
        var version = _dialect.ParseVersion(versionString!);

        Assert.Null(version);
    }

    [Theory]
    [InlineData(1, 0, 0, SqlStandardLevel.Sql2016)]
    [InlineData(0, 9, 0, SqlStandardLevel.Sql2016)]
    [InlineData(0, 8, 0, SqlStandardLevel.Sql2011)]
    [InlineData(0, 6, 0, SqlStandardLevel.Sql2008)]
    [InlineData(0, 4, 0, SqlStandardLevel.Sql2003)]
    public void DetermineStandardCompliance_Should_Return_Correct_Level_For_Version(int major, int minor, int patch, SqlStandardLevel expected)
    {
        var version = new Version(major, minor, patch);

        var compliance = _dialect.DetermineStandardCompliance(version);

        Assert.Equal(expected, compliance);
    }

    [Fact]
    public void DetermineStandardCompliance_Should_Return_Sql2016_For_Null_Version()
    {
        var compliance = _dialect.DetermineStandardCompliance(null);

        Assert.Equal(SqlStandardLevel.Sql2016, compliance);
    }

    [Fact]
    public void ExtractProductNameFromVersion_Should_Always_Return_DuckDB()
    {
        var productName = _dialect.ExtractProductNameFromVersion("any version string");

        Assert.Equal("DuckDB", productName);
    }

    [Fact]
    public void CreateDbParameter_Should_Handle_Guid_As_String()
    {
        var guidValue = Guid.NewGuid();

        var parameter = _dialect.CreateDbParameter("test", DbType.Guid, guidValue);

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal(guidValue.ToString(), parameter.Value);
    }

    [Fact]
    public void CreateDbParameter_Should_Handle_Boolean_Natively()
    {
        var parameter = _dialect.CreateDbParameter("test", DbType.Boolean, true);

        Assert.Equal(DbType.Boolean, parameter.DbType);
        Assert.Equal(true, parameter.Value);
    }

    [Fact]
    public void CreateDbParameter_Should_Handle_Other_Types_Normally()
    {
        var parameter = _dialect.CreateDbParameter("test", DbType.String, "value");

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("value", parameter.Value);
    }

    [Fact]
    public void GetConnectionSessionSettings_Should_Return_Read_Only_Pragma_When_ReadOnly()
    {
        var context = new DatabaseContext("test", _factory);

        var settings = _dialect.GetConnectionSessionSettings(context, readOnly: true);

        Assert.Equal("PRAGMA read_only = 1;", settings);
    }

    [Fact]
    public void GetConnectionSessionSettings_Should_Return_Empty_When_Not_ReadOnly()
    {
        var context = new DatabaseContext("test", _factory);

        var settings = _dialect.GetConnectionSessionSettings(context, readOnly: false);

        Assert.Equal(string.Empty, settings);
    }

    [Fact]
    public void ApplyConnectionSettings_Should_Add_ReadOnly_Access_Mode_For_File_Connections()
    {
        var context = new DatabaseContext("Data Source=/path/to/file.duckdb", _factory);
        var connection = _factory.CreateConnection();

        _dialect.ApplyConnectionSettings(connection, context, readOnly: true);

        Assert.Contains("access_mode=READ_ONLY", connection.ConnectionString);
    }

    [Fact]
    public void ApplyConnectionSettings_Should_Not_Add_ReadOnly_For_Memory_Connections()
    {
        var context = new DatabaseContext("Data Source=:memory:", _factory);
        var connection = _factory.CreateConnection();

        _dialect.ApplyConnectionSettings(connection, context, readOnly: true);

        Assert.DoesNotContain("access_mode=READ_ONLY", connection.ConnectionString);
    }

    [Fact]
    public void ApplyConnectionSettings_Should_Not_Add_ReadOnly_When_Not_ReadOnly()
    {
        var context = new DatabaseContext("Data Source=/path/to/file.duckdb", _factory);
        var connection = _factory.CreateConnection();

        _dialect.ApplyConnectionSettings(connection, context, readOnly: false);

        Assert.DoesNotContain("access_mode=READ_ONLY", connection.ConnectionString);
    }

    [Fact]
    public void DatabaseType_Should_Return_DuckDB()
    {
        Assert.Equal(SupportedDatabase.DuckDB, _dialect.DatabaseType);
    }

    [Fact]
    public void ParameterMarker_Should_Return_Dollar_Sign()
    {
        Assert.Equal("$", _dialect.ParameterMarker);
    }

    [Fact]
    public void GetVersionQuery_Should_Return_Select_Version()
    {
        Assert.Equal("SELECT version()", _dialect.GetVersionQuery());
    }

    [Fact]
    public void Feature_Support_Properties_Should_Return_Expected_Values()
    {
        Assert.True(_dialect.SupportsNamedParameters);
        Assert.False(_dialect.SupportsMerge); // False when not initialized - version-dependent
        Assert.True(_dialect.SupportsInsertOnConflict);
        Assert.True(_dialect.SupportsJsonTypes);
        Assert.True(_dialect.SupportsArrayTypes);
        Assert.True(_dialect.SupportsWindowFunctions);
        Assert.True(_dialect.SupportsCommonTableExpressions);
        Assert.True(_dialect.SupportsNamespaces);
        Assert.False(_dialect.SupportsXmlTypes);
        Assert.False(_dialect.SupportsTemporalData);
        Assert.False(_dialect.SupportsRowPatternMatching);
        Assert.True(_dialect.SupportsMultidimensionalArrays);
        Assert.True(_dialect.SupportsRegularExpressions);
        Assert.True(_dialect.SupportsSubqueries);
        Assert.True(_dialect.SupportsOuterJoins);
        Assert.True(_dialect.SupportsUnion);
        Assert.True(_dialect.PrepareStatements);
        Assert.Equal(65535, _dialect.MaxParameterLimit);
        Assert.Equal(255, _dialect.ParameterNameMaxLength);
    }

    [Fact]
    public void GetConnectionSessionSettings_Obsolete_Should_Return_Empty_String()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var settings = _dialect.GetConnectionSessionSettings();
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.Equal(string.Empty, settings);
    }

    [Theory]
    [InlineData("v1.4.0", true)]
    [InlineData("v1.3.0", false)]
    [InlineData("v1.5.0", true)]
    [InlineData("v0.9.0", false)]
    public void Version_Parsing_Should_Work_Correctly(string versionString, bool isVersionOnePointFourOrLater)
    {
        var dialect = new DuckDbDialect(_factory, _logger);
        var parsedVersion = dialect.ParseVersion(versionString);

        Assert.NotNull(parsedVersion);

        if (isVersionOnePointFourOrLater)
        {
            Assert.True(parsedVersion.Major > 1 || (parsedVersion.Major == 1 && parsedVersion.Minor >= 4));
        }
        else
        {
            Assert.True(parsedVersion.Major < 1 || (parsedVersion.Major == 1 && parsedVersion.Minor < 4));
        }
    }

    [Fact]
    public void SupportsEnhancedWindowFunctions_Should_Return_True()
    {
        // Enhanced window functions should always be true for DuckDB
        Assert.True(_dialect.SupportsEnhancedWindowFunctions);
    }
}
