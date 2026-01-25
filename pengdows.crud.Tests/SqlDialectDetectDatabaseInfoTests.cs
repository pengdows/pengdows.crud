#region

using System;
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

public class SqlDialectDetectDatabaseInfoTests
{
    private readonly ILogger<SqlDialect> _logger;
    private readonly fakeDbFactory _factory;

    public SqlDialectDetectDatabaseInfoTests()
    {
        _factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        _logger = new LoggerFactory().CreateLogger<SqlDialect>();
    }

    [Fact]
    public async Task DetectDatabaseInfoAsync_Should_Return_Cached_ProductInfo_When_Already_Detected()
    {
        var dialect = new TestSqlDialect(_factory, _logger);
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        // First call should detect and cache
        var firstResult = await dialect.DetectDatabaseInfoAsync(trackedConnection);

        // Second call should return cached result
        var secondResult = await dialect.DetectDatabaseInfoAsync(trackedConnection);

        Assert.Same(firstResult, secondResult);
    }

    [Fact]
    public async Task DetectDatabaseInfoAsync_Should_Call_GetDatabaseVersionAsync()
    {
        _factory.SetScalarResult("Test Database 1.0");
        var dialect = new TestSqlDialect(_factory, _logger);
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var result = await dialect.DetectDatabaseInfoAsync(trackedConnection);

        Assert.NotNull(result);
        Assert.Equal("Test Database 1.0", result.ProductVersion);
    }

    [Fact]
    public async Task DetectDatabaseInfoAsync_Should_Call_GetProductNameAsync()
    {
        _factory.SetScalarResult("1.0");
        var dialect = new TestSqlDialect(_factory, _logger);
        dialect.SetProductNameResult("Test Database");
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var result = await dialect.DetectDatabaseInfoAsync(trackedConnection);

        Assert.NotNull(result);
        Assert.Equal("Test Database", result.ProductName);
    }

    [Fact]
    public async Task
        DetectDatabaseInfoAsync_Should_Fallback_To_ExtractProductNameFromVersion_When_GetProductName_Returns_Null()
    {
        _factory.SetScalarResult("MySQL Server 8.0.28");
        var dialect = new TestSqlDialect(_factory, _logger);
        dialect.SetProductNameResult(null); // GetProductNameAsync returns null
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var result = await dialect.DetectDatabaseInfoAsync(trackedConnection);

        Assert.NotNull(result);
        Assert.Equal("Extracted Product", result.ProductName); // From ExtractProductNameFromVersion
    }

    [Fact]
    public async Task DetectDatabaseInfoAsync_Should_Parse_Version()
    {
        _factory.SetScalarResult("5.7.35");
        var dialect = new TestSqlDialect(_factory, _logger);
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var result = await dialect.DetectDatabaseInfoAsync(trackedConnection);

        Assert.NotNull(result);
        Assert.NotNull(result.ParsedVersion);
        Assert.Equal(5, result.ParsedVersion!.Major);
        Assert.Equal(7, result.ParsedVersion.Minor);
        Assert.Equal(35, result.ParsedVersion.Build);
    }

    [Fact]
    public async Task DetectDatabaseInfoAsync_Should_Determine_StandardCompliance()
    {
        _factory.SetScalarResult("5.7.35");
        var dialect = new TestSqlDialect(_factory, _logger);
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var result = await dialect.DetectDatabaseInfoAsync(trackedConnection);

        Assert.NotNull(result);
        Assert.Equal(SqlStandardLevel.Sql99, result.StandardCompliance); // From TestSqlDialect
    }

    [Fact]
    public async Task DetectDatabaseInfoAsync_Should_Handle_Exception_Gracefully()
    {
        var dialect = new TestSqlDialect(_factory, _logger);
        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.SetCommandFailure("SELECT version()", new InvalidOperationException("Database error"));
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var result = await dialect.DetectDatabaseInfoAsync(trackedConnection);

        // Should return default product info when detection fails
        Assert.NotNull(result);
        Assert.Equal("Unknown", result.ProductName);
    }

    [Fact]
    public async Task DetectDatabaseInfoAsync_Should_Infer_DatabaseType()
    {
        _factory.SetScalarResult("PostgreSQL 14.5");
        var dialect = new TestSqlDialect(_factory, _logger);
        dialect.SetProductNameResult("PostgreSQL");
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var result = await dialect.DetectDatabaseInfoAsync(trackedConnection);

        Assert.NotNull(result);
        Assert.Equal(SupportedDatabase.PostgreSql, result.DatabaseType);
    }

    [Fact]
    public void DetectDatabaseInfo_Synchronous_Should_Work()
    {
        _factory.SetScalarResult("Test Database 1.0");
        var dialect = new TestSqlDialect(_factory, _logger);
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        trackedConnection.Open();

        var result = dialect.DetectDatabaseInfo(trackedConnection);

        Assert.NotNull(result);
        Assert.Equal("Test Database 1.0", result.ProductVersion);
    }

    [Fact]
    public async Task DetectDatabaseInfoAsync_Should_Handle_Empty_Version_String()
    {
        _factory.SetScalarResult("");
        var dialect = new TestSqlDialect(_factory, _logger);
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var result = await dialect.DetectDatabaseInfoAsync(trackedConnection);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.ProductVersion);
    }

    [Fact]
    public async Task DetectDatabaseInfoAsync_Should_Handle_Null_Version_String()
    {
        _factory.SetScalarResult(null);
        var dialect = new TestSqlDialect(_factory, _logger);
        var connection = (fakeDbConnection)_factory.CreateConnection();
        var trackedConnection = new TrackedConnection(connection);
        await trackedConnection.OpenAsync();

        var result = await dialect.DetectDatabaseInfoAsync(trackedConnection);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.ProductVersion);
    }

    private class TestSqlDialect : SqlDialect
    {
        private string? _productNameResult = "Test Database";

        public TestSqlDialect(DbProviderFactory factory, ILogger logger) : base(factory, logger)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.Sqlite;

        public void SetProductNameResult(string? result)
        {
            _productNameResult = result;
        }

        public override async Task<string?> GetProductNameAsync(ITrackedConnection connection)
        {
            return await Task.FromResult(_productNameResult);
        }

        public override string ExtractProductNameFromVersion(string versionString)
        {
            return "Extracted Product";
        }

        public override SqlStandardLevel DetermineStandardCompliance(Version? version)
        {
            return SqlStandardLevel.Sql99;
        }

        public override Version? ParseVersion(string versionString)
        {
            if (string.IsNullOrWhiteSpace(versionString))
            {
                return null;
            }

            // Simple parsing for test purposes
            var parts = versionString.Split('.');
            if (parts.Length >= 3 && int.TryParse(parts[0], out var major) &&
                int.TryParse(parts[1], out var minor) && int.TryParse(parts[2], out var build))
            {
                return new Version(major, minor, build);
            }

            return null;
        }

        protected override SupportedDatabase InferDatabaseTypeFromInfo(string? productName, string versionString)
        {
            if (productName?.Contains("PostgreSQL") == true)
            {
                return SupportedDatabase.PostgreSql;
            }

            if (productName?.Contains("MySQL") == true)
            {
                return SupportedDatabase.MySql;
            }

            if (productName?.Contains("Oracle") == true)
            {
                return SupportedDatabase.Oracle;
            }

            return SupportedDatabase.Sqlite; // Default for tests
        }
    }
}