#region

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;
#endregion

namespace pengdows.crud.Tests;

public class SqliteDialectLimitTests
{
    [Theory]
    [InlineData("3.31.1", 999)]
    [InlineData("3.32.0", 32766)]
    public void MaxParameterLimit_SwitchesOnVersion(string sqliteVersion, int expectedLimit)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.ScalarResultsByCommand["SELECT sqlite_version()"] = sqliteVersion;
        var scalars = new Dictionary<string, object>
        {
            ["SELECT sqlite_version()"] = sqliteVersion
        };

        using var tracked = new FakeTrackedConnection(connection, new DataTable(), scalars);

        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        dialect.DetectDatabaseInfo(tracked);

        Assert.Equal(expectedLimit, dialect.MaxParameterLimit);
    }

    [Theory]
    [InlineData(9876.54321)]
    [InlineData(-99999999.99999999)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void CreateDbParameter_Decimal_StoresAsDouble(double rawValue)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);
        var decimalValue = (decimal)rawValue;

        var p = dialect.CreateDbParameter("col_decimal", DbType.Decimal, decimalValue);

        // SQLite stores DECIMAL as REAL (double). The parameter must use DbType.Double
        // so Microsoft.Data.Sqlite can bind the value correctly — DbType.Decimal causes
        // the driver to store 0 instead of the actual value.
        Assert.Equal(DbType.Double, p.DbType);
        Assert.Equal((double)decimalValue, (double)p.Value!);
    }

    [Fact]
    public async Task GetProductNameAsync_ReturnsSqlite_WhenReaderHasRow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (fakeDbConnection)factory.CreateConnection();
        var scalars = new Dictionary<string, object>
        {
            ["SELECT sqlite_version()"] = "3.45.0"
        };
        using var tracked = new FakeTrackedConnection(connection, new DataTable(), scalars);
        var dialect = new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance);

        var product = await dialect.GetProductNameAsync(tracked);

        Assert.Equal("SQLite", product);
    }

    [Theory]
    [InlineData(3, 45, SqlStandardLevel.Sql2016)]
    [InlineData(3, 35, SqlStandardLevel.Sql2011)]
    [InlineData(3, 25, SqlStandardLevel.Sql2008)]
    [InlineData(3, 8, SqlStandardLevel.Sql2003)]
    [InlineData(4, 0, SqlStandardLevel.Sql92)]
    public void DetermineStandardCompliance_MapsExpectedLevels(int major, int minor, SqlStandardLevel expected)
    {
        var dialect = new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger<SqliteDialect>.Instance);

        var result = dialect.DetermineStandardCompliance(new Version(major, minor));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void DetermineStandardCompliance_NullVersion_ReturnsSql92()
    {
        var dialect = new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger<SqliteDialect>.Instance);
        Assert.Equal(SqlStandardLevel.Sql92, dialect.DetermineStandardCompliance(null));
    }

    [Fact]
    public void IsUniqueViolation_UsesSpecificSqliteCodesOrMessages()
    {
        var dialect = new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger<SqliteDialect>.Instance);

        Assert.True(dialect.IsUniqueViolation(new SqliteDbException(19, "UNIQUE constraint failed")));
        Assert.True(dialect.IsUniqueViolation(new SqliteDbException(1555)));
        Assert.False(dialect.IsUniqueViolation(new SqliteDbException(1)));
        Assert.False(dialect.IsUniqueViolation(new SqliteDbException(19, "FOREIGN KEY constraint failed")));
    }

    [Fact]
    public void IsMemoryDatabase_DetectsMemoryModeToken()
    {
        var dialect = new TestSqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite),
            NullLogger<SqliteDialect>.Instance);

        Assert.True(dialect.CallIsMemoryDatabase("Data Source=test.db;Mode=Memory"));
        Assert.False(dialect.CallIsMemoryDatabase("Data Source=test.db;Mode=ReadWrite"));
    }

    private sealed class SqliteDbException : DbException
    {
        public SqliteDbException(int errorCode, string message = "sqlite error")
            : base(message)
        {
            HResult = errorCode;
        }
    }

    private sealed class TestSqliteDialect(DbProviderFactory factory, Microsoft.Extensions.Logging.ILogger logger)
        : SqliteDialect(factory, logger)
    {
        public bool CallIsMemoryDatabase(string connectionString)
        {
            return IsMemoryDatabase(connectionString);
        }
    }
}
