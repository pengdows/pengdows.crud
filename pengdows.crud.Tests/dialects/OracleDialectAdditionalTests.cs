#region

using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
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
        public void ChangeDatabase(string databaseName) { }
        public void Close() { }
        public IDbTransaction BeginTransaction() => null!;
        public IDbTransaction BeginTransaction(IsolationLevel il) => null!;
        public void Open() { }
        public void Dispose() { }
        public IDbCommand CreateCommand() => new fakeDbCommand();
    }

    private static OracleDialect CreateDialect() => new OracleDialect(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger<OracleDialect>.Instance);

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
    public void GetConnectionSessionSettings_AppendsReadOnly()
    {
        var d = CreateDialect();
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", new fakeDbFactory(SupportedDatabase.Oracle));

        var ro = d.GetConnectionSessionSettings(ctx, readOnly: true);
        Assert.Contains("READ ONLY", ro);

        var rw = d.GetConnectionSessionSettings(ctx, readOnly: false);
        Assert.DoesNotContain("READ ONLY", rw);
    }

    [Fact]
    public void ApplyConnectionSettings_ConfiguresStatementCache()
    {
        var d = CreateDialect();
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", new fakeDbFactory(SupportedDatabase.Oracle));
        var conn = new FakeOracleConnection();

        d.ApplyConnectionSettings(conn, ctx, readOnly: false);
        Assert.True(conn.StatementCacheSize >= 64);
    }
}
