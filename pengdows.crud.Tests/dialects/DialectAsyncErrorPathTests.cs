#region

using System;
using System.Data;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

public class DialectAsyncErrorPathTests
{
    [Fact]
    public void FirebirdDialect_CreateDbParameter_BooleanType_ConvertsToInt16()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird.ToString());
        var dialect = new FirebirdDialect(factory, NullLogger.Instance);

        var trueParam = dialect.CreateDbParameter("test", DbType.Boolean, true);
        var falseParam = dialect.CreateDbParameter("test", DbType.Boolean, false);

        Assert.Equal(DbType.Int16, trueParam.DbType);
        Assert.Equal((short)1, trueParam.Value);
        Assert.Equal(DbType.Int16, falseParam.DbType);
        Assert.Equal((short)0, falseParam.Value);
    }

    [Fact]
    public void FirebirdDialect_CreateDbParameter_GuidType_ConvertsToBinary()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird.ToString());
        var dialect = new FirebirdDialect(factory, NullLogger.Instance);
        var testGuid = Guid.NewGuid();

        var param = dialect.CreateDbParameter("test", DbType.Guid, testGuid);

        Assert.Equal(DbType.Binary, param.DbType);
        Assert.Equal(testGuid.ToByteArray(), param.Value);
    }

    [Fact]
    public void FirebirdDialect_ParseVersion_InvalidVersionString_ReturnsNull()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird.ToString());
        var dialect = new FirebirdDialect(factory, NullLogger.Instance);

        var result1 = dialect.ParseVersion("");
        var result2 = dialect.ParseVersion("   ");
        var result3 = dialect.ParseVersion("invalid version string");
        var result4 = dialect.ParseVersion("LI-Vinvalid.format");
        var result5 = dialect.ParseVersion("Firebird invalid");

        Assert.Null(result1);
        Assert.Null(result2);
        Assert.Null(result3);
        Assert.Null(result4);
        Assert.Null(result5);
    }

    [Fact]
    public void FirebirdDialect_ParseVersion_ValidLegacyFormat_ReturnsVersion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird.ToString());
        var dialect = new FirebirdDialect(factory, NullLogger.Instance);

        var result = dialect.ParseVersion("LI-V3.0.7");

        Assert.NotNull(result);
        Assert.Equal(3, result.Major);
        Assert.Equal(0, result.Minor);
        Assert.Equal(7, result.Build);
    }

    [Fact]
    public void FirebirdDialect_ParseVersion_ValidFirebirdFormat_ReturnsVersion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird.ToString());
        var dialect = new FirebirdDialect(factory, NullLogger.Instance);

        var result = dialect.ParseVersion("Firebird 4.0");

        Assert.NotNull(result);
        Assert.Equal(4, result.Major);
        Assert.Equal(0, result.Minor);
    }

    [Fact]
    public void FirebirdDialect_ExtractProductNameFromVersion_ReturnsFirebird()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird.ToString());
        var dialect = new FirebirdDialect(factory, NullLogger.Instance);

        var result = dialect.ExtractProductNameFromVersion("any version string");

        Assert.Equal("Firebird", result);
    }

    [Fact]
    public void FirebirdDialect_DetermineStandardCompliance_NullVersion_ReturnsSql92()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird.ToString());
        var dialect = new FirebirdDialect(factory, NullLogger.Instance);

        var result = dialect.DetermineStandardCompliance(null);

        Assert.Equal(SqlStandardLevel.Sql92, result);
    }

    [Theory]
    [InlineData(5, 0, 0, SqlStandardLevel.Sql2016)]
    [InlineData(4, 0, 0, SqlStandardLevel.Sql2011)]
    [InlineData(3, 0, 0, SqlStandardLevel.Sql2008)]
    [InlineData(2, 0, 0, SqlStandardLevel.Sql2003)]
    [InlineData(1, 0, 0, SqlStandardLevel.Sql92)]
    public void FirebirdDialect_DetermineStandardCompliance_VariousVersions_ReturnsCorrectLevel(int major, int minor, int build, SqlStandardLevel expected)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird.ToString());
        var dialect = new FirebirdDialect(factory, NullLogger.Instance);
        var version = new Version(major, minor, build);

        var result = dialect.DetermineStandardCompliance(version);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void DuckDbDialect_ErrorPaths_HandleGracefully()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB.ToString());
        var dialect = new DuckDbDialect(factory, NullLogger.Instance);

        var result1 = dialect.ParseVersion("");
        var result2 = dialect.ParseVersion("invalid");

        Assert.Null(result1);
        Assert.Null(result2);
    }

    [Fact]
    public void DuckDbDialect_GetConnectionSessionSettings_ReturnsCorrectSettings()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB.ToString());
        var dialect = new DuckDbDialect(factory, NullLogger.Instance);

        var settings = dialect.GetConnectionSessionSettings();

        Assert.NotNull(settings);
    }

    [Fact]
    public void SqlDialect_TryParseMajorVersion_InvalidInput_ReturnsFalse()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite.ToString());
        var dialect = new SqliteDialect(factory, NullLogger.Instance);

        var method = typeof(SqlDialect).GetMethod("TryParseMajorVersion", BindingFlags.NonPublic | BindingFlags.Static);

        var result1 = (bool)method!.Invoke(null, new object[] { null!, 0 })!;
        var result2 = (bool)method.Invoke(null, new object[] { "", 0 })!;
        var result3 = (bool)method.Invoke(null, new object[] { "not a number", 0 })!;

        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);
    }

    [Fact]
    public void SqlDialect_TryParseMajorVersion_ValidInput_ReturnsTrue()
    {
        var method = typeof(SqlDialect).GetMethod("TryParseMajorVersion", BindingFlags.NonPublic | BindingFlags.Static);

        var parameters = new object[] { "14.2.1", 0 };
        var result = (bool)method!.Invoke(null, parameters)!;
        var majorVersion = (int)parameters[1];

        Assert.True(result);
        Assert.Equal(14, majorVersion);
    }

    [Fact]
    public void SqlDialect_GetPrime_EdgeCases_ReturnsValidPrimes()
    {
        var method = typeof(SqlDialect).GetMethod("GetPrime", BindingFlags.NonPublic | BindingFlags.Static);

        var result1 = (int)method!.Invoke(null, new object[] { 1 })!;
        var result2 = (int)method.Invoke(null, new object[] { 2 })!;
        var result3 = (int)method.Invoke(null, new object[] { 1000000 })!;

        Assert.True(result1 >= 1);
        Assert.True(result2 >= 2);
        Assert.True(result3 >= 1000000);
    }
}
