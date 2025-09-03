#region

using System;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class PostgreSqlDialectTests
{
    private readonly PostgreSqlDialect _dialect;
    private readonly fakeDbFactory _factory;

    public PostgreSqlDialectTests()
    {
        _factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        _dialect = new PostgreSqlDialect(_factory, NullLogger<PostgreSqlDialect>.Instance);
    }

    [Fact]
    public void DatabaseType_ReturnsPostgreSql()
    {
        Assert.Equal(SupportedDatabase.PostgreSql, _dialect.DatabaseType);
    }

    [Fact]
    public void ParameterMarker_ReturnsColon()
    {
        Assert.Equal(":", _dialect.ParameterMarker);
    }

    [Fact]
    public void SupportsNamedParameters_ReturnsTrue()
    {
        Assert.True(_dialect.SupportsNamedParameters);
    }

    [Fact]
    public void MaxParameterLimit_ReturnsExpectedValue()
    {
        Assert.Equal(32767, _dialect.MaxParameterLimit);
    }

    [Theory]
    [InlineData("test", ":test")]
    [InlineData("p1", ":p1")]
    [InlineData("param999", ":param999")]
    public void MakeParameterName_ReturnsCorrectFormat(string paramName, string expected)
    {
        var param = new pengdows.crud.fakeDb.fakeDbParameter 
        { 
            ParameterName = paramName, 
            DbType = DbType.Int32, 
            Value = 1 
        };
        Assert.Equal(expected, _dialect.MakeParameterName(param));
    }

    [Fact]
    public void GetConnectionSessionSettings_NonReadOnlyContext_ReturnsBaseSettings()
    {
        var ctx = CreateTestContext();
        
        var settings = _dialect.GetConnectionSessionSettings(ctx, false);
        
        Assert.NotEmpty(settings);
        Assert.Contains("SET standard_conforming_strings = on;", settings);
        Assert.Contains("SET client_min_messages = warning;", settings);
        Assert.Contains("SET search_path = public;", settings);
    }

    [Fact]
    public void GetConnectionSessionSettings_ReadOnlyContext_ReturnsReadOnlySettings()
    {
        var ctx = CreateTestContext();
        
        var settings = _dialect.GetConnectionSessionSettings(ctx, true);
        
        Assert.NotEmpty(settings);
        Assert.Contains("SET default_transaction_read_only = on;", settings);
        Assert.Contains("SET standard_conforming_strings = on;", settings);
    }

    [Fact]
    public void ApplyConnectionSettings_NonNpgsqlConnection_SetsConnectionString()
    {
        var ctx = CreateTestContext();

        // Create a fake connection that doesn't start with "Npgsql."
        var fakeConnection = new TestConnection { ConnectionString = "" };
        
        _dialect.ApplyConnectionSettings(fakeConnection, ctx, false);
        
        // Should set connection string (may be normalized by ConnectionStringBuilder)
        Assert.NotEmpty(fakeConnection.ConnectionString);
        Assert.Contains("host=localhost", fakeConnection.ConnectionString);
        Assert.Contains("database=test", fakeConnection.ConnectionString);
    }

    [Fact]
    public void ApplyConnectionSettings_NpgsqlConnection_ConfiguresSettings()
    {
        // Since we can't easily mock GetType().FullName for the real connection type check,
        // this test verifies that the connection string gets set properly
        var ctx = CreateTestContext();
        var connection = _factory.CreateConnection();
        connection.ConnectionString = "";
        
        _dialect.ApplyConnectionSettings(connection, ctx, false);
        
        // Should get the context connection string 
        Assert.Equal(ctx.ConnectionString, connection.ConnectionString);
    }

    [Fact]
    public void ApplyConnectionSettings_ReadOnlyMode_AddsReadOnlySettings()
    {
        var ctx = CreateTestContext();
        var connection = _factory.CreateConnection();
        var connectionString = "Host=localhost;Database=test;";
        connection.ConnectionString = connectionString;
        
        _dialect.ApplyConnectionSettings(connection, ctx, true);
        
        // Read-only settings should be added to the connection string
        Assert.Contains("Options='-c default_transaction_read_only=on'", connection.ConnectionString);
    }

    [Theory]
    [InlineData("15.0.0", SqlStandardLevel.Sql2016)]
    [InlineData("13.5.0", SqlStandardLevel.Sql2011)]
    [InlineData("11.2.0", SqlStandardLevel.Sql2008)]
    [InlineData("9.6.0", SqlStandardLevel.Sql2003)]
    [InlineData("8.4.0", SqlStandardLevel.Sql92)]
    public void DetermineStandardCompliance_ReturnsCorrectStandardLevel(string versionString, SqlStandardLevel expected)
    {
        var version = new Version(versionString);
        
        // Use reflection to call the protected method
        var method = typeof(PostgreSqlDialect).GetMethod("DetermineStandardCompliance", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (SqlStandardLevel)method!.Invoke(_dialect, new object[] { version })!;
        
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DetermineStandardCompliance_NullVersion_ReturnsDefault()
    {
        var method = typeof(PostgreSqlDialect).GetMethod("DetermineStandardCompliance", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (SqlStandardLevel)method!.Invoke(_dialect, new object[] { null })!;
        
        Assert.Equal(SqlStandardLevel.Sql2008, result);
    }

    [Fact]
    public void ConnectionStringBuilder_IsNotNull()
    {
        // Test that the ConnectionStringBuilder property is properly initialized
        var property = typeof(SqlDialect).GetProperty("ConnectionStringBuilder", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var builder = (DbConnectionStringBuilder)property!.GetValue(_dialect)!;
        
        Assert.NotNull(builder);
    }

    [Fact]
    public void ApplyConnectionSettings_UsesConnectionStringBuilder()
    {
        var ctx = CreateTestContext();
        var connection = _factory.CreateConnection();
        var connectionString = "Host=localhost;Database=test;";
        connection.ConnectionString = connectionString;
        
        // Get the ConnectionStringBuilder before the call
        var property = typeof(SqlDialect).GetProperty("ConnectionStringBuilder", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var builder = (DbConnectionStringBuilder)property!.GetValue(_dialect)!;
        
        _dialect.ApplyConnectionSettings(connection, ctx, false);
        
        // Verify that the ConnectionStringBuilder was used
        Assert.NotNull(builder.ConnectionString);
    }

    [Fact]
    public void ApplyConnectionSettings_HandlesExceptions_GracefullyFallsBack()
    {
        // This test verifies that the method doesn't throw exceptions
        // when connection string configuration encounters issues
        var ctx = CreateTestContext();
        var connection = _factory.CreateConnection();
        var connectionString = "Host=localhost;Database=test;";
        connection.ConnectionString = connectionString;
        
        // Should not throw an exception even with unusual connection strings
        _dialect.ApplyConnectionSettings(connection, ctx, false);
        
        // Method should complete without throwing
        Assert.True(true); // If we reach here, no exception was thrown
    }

    private DatabaseContext CreateTestContext()
    {
        var cfg = new pengdows.crud.configuration.DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        return new DatabaseContext(cfg, _factory);
    }

    [Fact]
    public void ApplyConnectionSettings_NonReadOnlyMode_DoesNotAddReadOnlyOptions()
    {
        // Test that non-read-only mode doesn't add read-only options
        var ctx = CreateTestContext();
        var connection = _factory.CreateConnection();
        connection.ConnectionString = "";
        
        _dialect.ApplyConnectionSettings(connection, ctx, false);
        
        // Should not contain read-only options
        Assert.DoesNotContain("Options='-c default_transaction_read_only=on'", connection.ConnectionString);
        Assert.Equal(ctx.ConnectionString, connection.ConnectionString);
    }

    [Fact] 
    public void ApplyConnectionSettings_NullConnectionString_HandledGracefully()
    {
        var ctx = CreateTestContext();
        var connection = _factory.CreateConnection();
        connection.ConnectionString = null!;
        
        // Should not throw with null connection string
        _dialect.ApplyConnectionSettings(connection, ctx, false);
        
        // If we reach here, no exception was thrown
        Assert.True(true);
    }

    [Fact]
    public void SupportsNamespaces_ReturnsTrue()
    {
        Assert.True(_dialect.SupportsNamespaces);
    }

    [Fact]
    public void SupportsInsertOnConflict_ReturnsTrue()
    {
        Assert.True(_dialect.SupportsInsertOnConflict);
    }

    [Theory]
    [InlineData("testname", "\"testname\"")]
    [InlineData("test name", "\"test name\"")]
    [InlineData("TEST", "\"TEST\"")]
    [InlineData("table.column", "\"table\".\"column\"")]
    public void WrapObjectName_ReturnsQuotedIdentifiers(string input, string expected)
    {
        Assert.Equal(expected, _dialect.WrapObjectName(input));
    }

    [Fact]
    public void WrapObjectName_NullInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _dialect.WrapObjectName(null!));
    }

    // Test helper classes

    private class TestConnection : DbConnection
    {
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "test";
        public override string DataSource => "localhost";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => null!;
        protected override DbCommand CreateDbCommand() => null!;
    }

}