#region
using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;
#endregion

namespace pengdows.crud.Tests;

public class FirebirdDialectAdvancedTests
{
    private readonly ILogger<FirebirdDialect> _logger;
    private readonly FirebirdDialect _dialect;
    private readonly fakeDbFactory _factory;

    public FirebirdDialectAdvancedTests()
    {
        _factory = new fakeDbFactory(SupportedDatabase.Firebird);
        _logger = new LoggerFactory().CreateLogger<FirebirdDialect>();
        _dialect = new FirebirdDialect(_factory, _logger);
    }

    [Fact]
    public async Task GetProductNameAsync_Should_Return_Firebird_From_Engine_Version_Query()
    {
        _factory.SetScalarResult("WI-V3.0.7.33374 Firebird 3.0");
        
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var productName = await _dialect.GetProductNameAsync(trackedConnection);

        Assert.Equal("Firebird", productName);
    }

    [Fact]
    public async Task GetProductNameAsync_Should_Fallback_To_Database_Query_When_Engine_Version_Fails()
    {
        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.SetCommandFailure("SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database", 
            new InvalidOperationException("Engine version query failed"));
        _factory.SetScalarResult("success");
        
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var productName = await _dialect.GetProductNameAsync(trackedConnection);

        Assert.Equal("Firebird", productName);
    }

    [Fact]
    public async Task GetProductNameAsync_Should_Return_Null_When_Both_Queries_Fail()
    {
        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.SetCommandFailure("SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database", 
            new InvalidOperationException("Engine version query failed"));
        connection.SetCommandFailure("SELECT * FROM rdb$database", 
            new InvalidOperationException("Database query failed"));
        
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var productName = await _dialect.GetProductNameAsync(trackedConnection);

        Assert.Null(productName);
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_Should_Return_Version_From_Engine_Version_Query()
    {
        _factory.SetScalarResult("WI-V3.0.7.33374 Firebird 3.0");
        
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await _dialect.GetDatabaseVersionAsync(trackedConnection);

        Assert.Equal("WI-V3.0.7.33374 Firebird 3.0", version);
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_Should_Fallback_To_Monitor_Query_When_Engine_Fails()
    {
        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.SetCommandFailure("SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database", 
            new InvalidOperationException("Engine version failed"));
        _factory.SetScalarResult("Firebird 4.0.2");
        
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await _dialect.GetDatabaseVersionAsync(trackedConnection);

        Assert.Equal("Firebird 4.0.2", version);
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_Should_Call_Base_When_Both_Queries_Fail()
    {
        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.SetCommandFailure("SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database", 
            new InvalidOperationException("Engine version failed"));
        connection.SetCommandFailure("SELECT mon$server_version FROM mon$database", 
            new InvalidOperationException("Monitor query failed"));
        
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await _dialect.GetDatabaseVersionAsync(trackedConnection);

        // Base implementation should be called
        Assert.Equal(string.Empty, version);
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_Should_Handle_Null_Result_From_Engine_Query()
    {
        _factory.SetScalarResult(null);
        
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await _dialect.GetDatabaseVersionAsync(trackedConnection);

        // Should fall back to monitor query, then to base
        Assert.Equal(string.Empty, version);
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_Should_Handle_Empty_String_From_Engine_Query()
    {
        _factory.SetScalarResult("");
        
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var version = await _dialect.GetDatabaseVersionAsync(trackedConnection);

        // Should fall back to monitor query, then to base
        Assert.Equal(string.Empty, version);
    }

    [Theory]
    [InlineData("LI-V3.0.7", 3, 0, 7)]
    [InlineData("LI-V4.0.2", 4, 0, 2)]
    [InlineData("LI-V2.5.9", 2, 5, 9)]
    public void ParseVersion_Should_Parse_Legacy_Format(string versionString, int expectedMajor, int expectedMinor, int expectedBuild)
    {
        var version = _dialect.ParseVersion(versionString);

        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version!.Major);
        Assert.Equal(expectedMinor, version.Minor);
        Assert.Equal(expectedBuild, version.Build);
    }

    [Theory]
    [InlineData("Firebird 3.0", 3, 0)]
    [InlineData("Firebird 4.1", 4, 1)]
    [InlineData("Firebird 2.5", 2, 5)]
    public void ParseVersion_Should_Parse_Firebird_Format(string versionString, int expectedMajor, int expectedMinor)
    {
        var version = _dialect.ParseVersion(versionString);

        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version!.Major);
        Assert.Equal(expectedMinor, version.Minor);
    }

    [Theory]
    [InlineData("3.0.7")]
    [InlineData("4.0.2")]
    public void ParseVersion_Should_Parse_Standard_Format(string versionString)
    {
        var version = _dialect.ParseVersion(versionString);

        Assert.NotNull(version);
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
    [InlineData(5, 0, 0, SqlStandardLevel.Sql2016)]
    [InlineData(4, 0, 0, SqlStandardLevel.Sql2011)]
    [InlineData(3, 0, 0, SqlStandardLevel.Sql2008)]
    [InlineData(2, 5, 0, SqlStandardLevel.Sql2003)]
    [InlineData(1, 5, 0, SqlStandardLevel.Sql92)]
    public void DetermineStandardCompliance_Should_Return_Correct_Level_For_Version(int major, int minor, int patch, SqlStandardLevel expected)
    {
        var version = new Version(major, minor, patch);
        
        var compliance = _dialect.DetermineStandardCompliance(version);

        Assert.Equal(expected, compliance);
    }

    [Fact]
    public void DetermineStandardCompliance_Should_Return_Sql92_For_Null_Version()
    {
        var compliance = _dialect.DetermineStandardCompliance(null);

        Assert.Equal(SqlStandardLevel.Sql92, compliance);
    }

    [Fact]
    public void ExtractProductNameFromVersion_Should_Always_Return_Firebird()
    {
        var productName = _dialect.ExtractProductNameFromVersion("any version string");

        Assert.Equal("Firebird", productName);
    }

    [Fact]
    public void CreateDbParameter_Should_Convert_Boolean_To_Int16()
    {
        var parameterTrue = _dialect.CreateDbParameter("test", DbType.Boolean, true);
        var parameterFalse = _dialect.CreateDbParameter("test", DbType.Boolean, false);

        Assert.Equal(DbType.Int16, parameterTrue.DbType);
        Assert.Equal((short)1, parameterTrue.Value);
        
        Assert.Equal(DbType.Int16, parameterFalse.DbType);
        Assert.Equal((short)0, parameterFalse.Value);
    }

    [Fact]
    public void CreateDbParameter_Should_Convert_Guid_To_Binary()
    {
        var guidValue = Guid.NewGuid();
        
        var parameter = _dialect.CreateDbParameter("test", DbType.Guid, guidValue);

        Assert.Equal(DbType.Binary, parameter.DbType);
        Assert.Equal(guidValue.ToByteArray(), parameter.Value);
    }

    [Fact]
    public void CreateDbParameter_Should_Handle_Other_Types_Normally()
    {
        var parameter = _dialect.CreateDbParameter("test", DbType.String, "value");

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("value", parameter.Value);
    }

    [Fact]
    public void GetConnectionSessionSettings_Should_Return_Transaction_And_Dialect_Settings()
    {
        var context = new DatabaseContext("test", _factory);
        
        var settings = _dialect.GetConnectionSessionSettings(context, readOnly: true);

        Assert.Equal("SET TRANSACTION ISOLATION LEVEL READ COMMITTED;\nSET SQL DIALECT 3;", settings);
    }

    [Fact]
    public void GetConnectionSessionSettings_Should_Return_Same_For_ReadWrite()
    {
        var context = new DatabaseContext("test", _factory);
        
        var settings = _dialect.GetConnectionSessionSettings(context, readOnly: false);

        Assert.Equal("SET TRANSACTION ISOLATION LEVEL READ COMMITTED;\nSET SQL DIALECT 3;", settings);
    }

    [Fact]
    public void GetVersionQuery_Should_Return_Engine_Version_Query()
    {
        Assert.Equal("SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database", _dialect.GetVersionQuery());
    }

    [Fact]
    public void DatabaseType_Should_Return_Firebird()
    {
        Assert.Equal(SupportedDatabase.Firebird, _dialect.DatabaseType);
    }

    [Fact]
    public void ParameterMarker_Should_Return_At_Sign()
    {
        Assert.Equal("@", _dialect.ParameterMarker);
    }

    [Fact]
    public void Feature_Support_Properties_Should_Return_Expected_Values()
    {
        Assert.True(_dialect.SupportsNamedParameters);
        Assert.True(_dialect.SupportsSavepoints);
        Assert.False(_dialect.SupportsJsonTypes);
        Assert.True(_dialect.SupportsArrayTypes);
        Assert.True(_dialect.PrepareStatements);
        Assert.Equal(ProcWrappingStyle.ExecuteProcedure, _dialect.ProcWrappingStyle);
        Assert.Equal(65535, _dialect.MaxParameterLimit);
        Assert.Equal(1499, _dialect.MaxOutputParameters);
        Assert.Equal(63, _dialect.ParameterNameMaxLength);
    }

    [Fact]
    public void GetConnectionSessionSettings_Obsolete_Should_Return_Transaction_And_Dialect_Settings()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var settings = _dialect.GetConnectionSessionSettings();
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.Equal("SET TRANSACTION ISOLATION LEVEL READ COMMITTED;\nSET SQL DIALECT 3;", settings);
    }

    [Fact]
    public void Version_Dependent_Features_Should_Work_When_Not_Initialized()
    {
        // These properties check IsInitialized and ProductInfo.ParsedVersion
        // When not initialized, they should return false (default behavior)
        var newDialect = new FirebirdDialect(_factory, _logger);
        
        // These will return false because IsInitialized is false
        Assert.False(newDialect.SupportsMerge);
        Assert.False(newDialect.SupportsWindowFunctions);
        Assert.False(newDialect.SupportsCommonTableExpressions);
    }
}