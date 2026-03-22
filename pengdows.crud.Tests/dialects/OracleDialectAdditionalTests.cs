#region

using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

public class OracleDialectAdditionalTests
{
    private sealed class FakeOracleConnection : IDbConnection
    {
        public int StatementCacheSize { get; set; } = 0; // property discovered via reflection
        private string _connectionString = string.Empty;

        [AllowNull]
        public string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public int ConnectionTimeout => 0;
        public string Database => string.Empty;
        public ConnectionState State => ConnectionState.Closed;
        public string DataSource => string.Empty;

        public void ChangeDatabase(string databaseName)
        {
        }

        public void Close()
        {
        }

        public IDbTransaction BeginTransaction()
        {
            return null!;
        }

        public IDbTransaction BeginTransaction(IsolationLevel il)
        {
            return null!;
        }

        public void Open()
        {
        }

        public void Dispose()
        {
        }

        public IDbCommand CreateCommand()
        {
            return new fakeDbCommand();
        }
    }

    private static OracleDialect CreateDialect()
    {
        return new OracleDialect(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger<OracleDialect>.Instance);
    }

    [Theory]
    [InlineData(21, SqlStandardLevel.Sql2016)]
    [InlineData(19, SqlStandardLevel.Sql2016)]
    [InlineData(12, SqlStandardLevel.Sql2008)]
    [InlineData(11, SqlStandardLevel.Sql2003)]
    [InlineData(10, SqlStandardLevel.Sql99)]
    public void DetermineStandardCompliance_ByMajor(int major, SqlStandardLevel expected)
    {
        var d = CreateDialect();
        Assert.Equal(expected, d.DetermineStandardCompliance(new Version(major, 0)));
    }

    [Fact]
    public void GetConnectionSessionSettings_DoesNotAppendReadOnly_ForOracle()
    {
        var d = CreateDialect();
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle",
            new fakeDbFactory(SupportedDatabase.Oracle));

        var ro = d.GetConnectionSessionSettings(ctx, true);
        // Oracle has no session-level read-only mode
        Assert.DoesNotContain("READ ONLY", ro);
        Assert.Contains("NLS_TIMESTAMP_FORMAT", ro);

        var rw = d.GetConnectionSessionSettings(ctx, false);
        Assert.DoesNotContain("READ ONLY", rw);
        Assert.Contains("NLS_TIMESTAMP_FORMAT", rw);
    }

    [Fact]
    public void ApplyConnectionSettings_ConfiguresStatementCache()
    {
        var d = CreateDialect();
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle",
            new fakeDbFactory(SupportedDatabase.Oracle));
        var conn = new FakeOracleConnection();

        d.ApplyConnectionSettings(conn, ctx, false);
        Assert.True(conn.StatementCacheSize >= 64);
    }

    [Fact]
    public void CreateDbParameter_Bool_True_IsRemappedToNumeric()
    {
        var d = CreateDialect();
        var param = d.CreateDbParameter("p", DbType.Boolean, true);
        // Oracle maps bool → NUMBER via AdvancedTypeRegistry: DbType.Int16 and integer value 1
        Assert.Equal(DbType.Int16, param.DbType);
        Assert.Equal(1, Convert.ToInt32(param.Value));
    }

    [Fact]
    public void CreateDbParameter_Bool_False_IsRemappedToNumeric()
    {
        var d = CreateDialect();
        var param = d.CreateDbParameter("p", DbType.Boolean, false);
        Assert.Equal(DbType.Int16, param.DbType);
        Assert.Equal(0, Convert.ToInt32(param.Value));
    }

    [Fact]
    public void GetBaseSessionSettings_IncludesNlsTimestampFormat()
    {
        var d = CreateDialect();
        var settings = d.GetBaseSessionSettings();
        Assert.Contains("NLS_TIMESTAMP_FORMAT", settings);
        Assert.Contains("YYYY-MM-DD HH24:MI:SS.FF", settings);
    }

    [Fact]
    public void CreateDbParameter_Guid_IsRemappedToString()
    {
        // Oracle ODP.NET throws for DbType.Guid; OracleDialect remaps to String.
        // ApplyGuidFormat then serializes to "D"-format VARCHAR2(36).
        var d = CreateDialect();
        var guid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var param = d.CreateDbParameter("p", DbType.Guid, guid);
        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", param.Value?.ToString());
    }
}