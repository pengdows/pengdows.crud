using System;
using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

#nullable disable

namespace pengdows.crud.Tests
{

public class DialectCoverageBoostTests
{
    [Fact]
    public void SnowflakeDialect_AdditionalBranches_AreCovered()
    {
        var dialect = new SnowflakeDialect(new fakeDbFactory(SupportedDatabase.Snowflake), NullLogger.Instance);

        Assert.True(dialect.PrepareStatements);
        Assert.True(dialect.SupportsNamespaces);
        Assert.False(dialect.SupportsInsertReturning);
        Assert.False(dialect.SupportsMergeReturning);
        Assert.False(dialect.SupportsInsertOnConflict);
        Assert.False(dialect.SupportsSavepoints);
        Assert.True(dialect.SupportsDropTableIfExists);
        Assert.True(dialect.MergeUpdateRequiresTargetAlias);
        Assert.Equal("src", dialect.UpsertIncomingAlias);
        Assert.Equal("src.\"col\"", dialect.UpsertIncomingColumn("col"));
        Assert.Null(dialect.ParseVersion("   "));
        Assert.Equal(new Version(8, 12, 1), dialect.ParseVersion("8.12.1"));
        Assert.Equal("SET TRANSACTION READ ONLY;", dialect.GetReadOnlySessionSettings());

        var qb = new SqlQueryBuilder();
        dialect.BuildBatchUpdateSql("\"t\"", new[] { "\"val\"" }, new[] { "\"id\"" }, 1, qb,
            (row, col) => col == 0 ? 1 : null);

        var sql = qb.ToString();
        Assert.Contains("NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void FirebirdDialect_AdditionalBranches_AreCovered()
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird), NullLogger.Instance);

        Assert.False(dialect.SupportsBatchInsert);

        var wrapperBuilder = new SqlQueryBuilder();
        dialect.BuildBatchInsertSql("\"t\"", new[] { "\"c\"" }, 1, wrapperBuilder);
        Assert.Contains("EXECUTE BLOCK", wrapperBuilder.ToString(), StringComparison.Ordinal);

        var invalidTableBuilder = new SqlQueryBuilder();
        Assert.Throws<ArgumentException>(() =>
            dialect.BuildBatchInsertSql(" ", new[] { "\"c\"" }, 1, invalidTableBuilder, null));

        var invalidColumnsBuilder = new SqlQueryBuilder();
        Assert.Throws<ArgumentException>(() =>
            dialect.BuildBatchInsertSql("\"t\"", Array.Empty<string>(), 1, invalidColumnsBuilder, null));

        var invalidRowsBuilder = new SqlQueryBuilder();
        Assert.Throws<ArgumentException>(() =>
            dialect.BuildBatchInsertSql("\"t\"", new[] { "\"c\"" }, 0, invalidRowsBuilder, null));

        Assert.Equal(new Version(2, 5, 9, 27139), dialect.ParseVersion("LI-V2.5.9.27139 Firebird"));
        Assert.Equal(new Version(4, 0), dialect.ParseVersion("Firebird 4.0"));

        var dto = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
        var parameter = dialect.CreateDbParameter("p", DbType.DateTimeOffset, dto);
        var converted = Assert.IsType<DateTime>(parameter.Value);
        Assert.Equal(DateTimeKind.Unspecified, converted.Kind);
    }

    [Fact]
    public void OracleDialect_AdditionalBranches_AreCovered()
    {
        var dialect = new OracleDialect(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger.Instance);

        var wrapperBuilder = new SqlQueryBuilder();
        dialect.BuildBatchInsertSql("\"t\"", new[] { "\"c\"" }, 1, wrapperBuilder);
        Assert.Contains("INSERT ALL", wrapperBuilder.ToString(), StringComparison.Ordinal);

        var invalidTableBuilder = new SqlQueryBuilder();
        Assert.Throws<ArgumentException>(() =>
            dialect.BuildBatchInsertSql(" ", new[] { "\"c\"" }, 1, invalidTableBuilder, null));

        var invalidColumnsBuilder = new SqlQueryBuilder();
        Assert.Throws<ArgumentException>(() =>
            dialect.BuildBatchInsertSql("\"t\"", Array.Empty<string>(), 1, invalidColumnsBuilder, null));

        var invalidRowsBuilder = new SqlQueryBuilder();
        Assert.Throws<ArgumentException>(() =>
            dialect.BuildBatchInsertSql("\"t\"", new[] { "\"c\"" }, 0, invalidRowsBuilder, null));

        Assert.Throws<ArgumentNullException>(() => dialect.RenderMergeSource(null!, new[] { "p0" }));
        Assert.Throws<ArgumentNullException>(() => dialect.RenderMergeSource(Array.Empty<IColumnInfo>(), null!));
        Assert.Throws<ArgumentException>(() => dialect.RenderMergeSource(Array.Empty<IColumnInfo>(), new[] { "p0" }));
        Assert.Throws<ArgumentNullException>(() => dialect.RenderMergeOnClause(null!));

        var context = new Mock<IDatabaseContext>();
        context.SetupGet(c => c.ConnectionString).Returns("Data Source=oracle-test");

        var connection = new OracleThrowingConnection();
        dialect.ApplyConnectionSettings(connection, context.Object, false);
        Assert.Equal("Data Source=oracle-test", connection.ConnectionString);
    }

    [Fact]
    public void PostgreSqlDialect_AdditionalBranches_AreCovered()
    {
        var dialect = new TestablePostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);

        var jsonColumn = new Mock<IColumnInfo>();
        jsonColumn.SetupGet(c => c.Name).Returns("payload");
        jsonColumn.SetupGet(c => c.IsJsonType).Returns(true);

        var okParameter = new FakeNpgsqlParameter { ParameterName = "p0" };
        dialect.TryMarkJsonParameter(okParameter, jsonColumn.Object);
        Assert.Equal(FakeNpgsqlDbType.Jsonb, okParameter.NpgsqlDbType);
        Assert.Equal("jsonb", okParameter.DataTypeName);

        var throwingParameter = new ThrowingMetadataParameter { ParameterName = "p1" };
        dialect.TryMarkJsonParameter(throwingParameter, jsonColumn.Object);

        var dto = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.FromHours(3));
        var prepared = dialect.PrepareParameterValue(dto, DbType.DateTimeOffset);
        Assert.Equal(dto.UtcDateTime, prepared);

        var npgsqlConnection = new Npgsql.FakeConnection { ConnectionString = "Host=localhost;" };
        dialect.ConfigureProviderSpecificSettings(npgsqlConnection, Mock.Of<IDatabaseContext>(), false);

        var throwingConnection = new TestConnection();
        typeof(TestConnection).GetField("_connectionString", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(throwingConnection, "MIXED=CASE");
        dialect.ConfigureProviderSpecificSettings(throwingConnection, Mock.Of<IDatabaseContext>(), false);

        dialect.InvokeSetNpgsqlParameterType(okParameter, "Jsonb", "jsonb");
        dialect.InvokeSetNpgsqlParameterType(new ThrowingMetadataParameter { ParameterName = "p2" }, "Jsonb", "jsonb");
    }

    private sealed class TestablePostgreSqlDialect : PostgreSqlDialect
    {
        public TestablePostgreSqlDialect(DbProviderFactory factory, Microsoft.Extensions.Logging.ILogger logger)
            : base(factory, logger)
        {
        }

        public void InvokeSetNpgsqlParameterType(DbParameter parameter, string npgsqlDbTypeName, string dataTypeName)
        {
            SetNpgsqlParameterType(parameter, npgsqlDbTypeName, dataTypeName);
        }
    }

    private enum FakeNpgsqlDbType
    {
        Jsonb
    }

    private class FakeNpgsqlParameter : DbParameter
    {
        public FakeNpgsqlDbType NpgsqlDbType { get; set; }
        public string DataTypeName { get; set; } = string.Empty;

        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override string SourceColumn { get; set; } = string.Empty;
        public override object Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class ThrowingMetadataParameter : FakeNpgsqlParameter
    {
        public new FakeNpgsqlDbType NpgsqlDbType
        {
            get => base.NpgsqlDbType;
            set => throw new InvalidOperationException("cannot set enum metadata");
        }

        public new string DataTypeName
        {
            get => base.DataTypeName;
            set => throw new InvalidOperationException("cannot set data type name");
        }
    }

    private sealed class OracleThrowingConnection : IDbConnection
    {
        public int StatementCacheSize
        {
            get => 0;
            set => throw new InvalidOperationException("cache-size-fail");
        }

        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "oracle";
        public ConnectionState State => ConnectionState.Closed;
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
    }

    private sealed class TestConnection : IDbConnection
    {
        private string _connectionString = string.Empty;

        public string ConnectionString
        {
            get => _connectionString;
            set => throw new InvalidOperationException("set-fail");
        }

        public int ConnectionTimeout => 0;
        public string Database => "test";
        public ConnectionState State => ConnectionState.Closed;
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
    }
}

}

namespace Npgsql
{
    internal sealed class FakeConnection : IDbConnection
    {
        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "npgsql";
        public ConnectionState State => ConnectionState.Closed;
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
    }
}
