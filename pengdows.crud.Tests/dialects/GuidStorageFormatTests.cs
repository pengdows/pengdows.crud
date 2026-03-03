#region

using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Verifies the unified <c>GuidFormat</c> mechanism: each dialect produces the expected
/// <see cref="DbType"/> and value representation for <see cref="Guid"/> parameters.
/// </summary>
public class GuidStorageFormatTests
{
    private static readonly Guid TestGuid = new("550e8400-e29b-41d4-a716-446655440000");

    // ─── PassThrough dialects (SQL Server, MySQL, MariaDB) ───────────────────

    [Fact]
    public void SqlServer_Guid_UsesPassThrough_DbTypeGuidPreserved()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var dialect = new SqlServerDialect(factory, NullLoggerFactory.Instance.CreateLogger(nameof(SqlServerDialect)));

        var param = dialect.CreateDbParameter("p", DbType.Guid, TestGuid);

        Assert.Equal(DbType.Guid, param.DbType);
        Assert.Equal(TestGuid, param.Value);
    }

    [Fact]
    public void MySql_Guid_UsesPassThrough_DbTypeGuidPreserved()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLoggerFactory.Instance.CreateLogger(nameof(MySqlDialect)));

        var param = dialect.CreateDbParameter("p", DbType.Guid, TestGuid);

        Assert.Equal(DbType.Guid, param.DbType);
        Assert.Equal(TestGuid, param.Value);
    }

    [Fact]
    public void MariaDb_Guid_UsesPassThrough_DbTypeGuidPreserved()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var dialect = new MariaDbDialect(factory, NullLoggerFactory.Instance.CreateLogger(nameof(MariaDbDialect)));

        var param = dialect.CreateDbParameter("p", DbType.Guid, TestGuid);

        Assert.Equal(DbType.Guid, param.DbType);
        Assert.Equal(TestGuid, param.Value);
    }

    // ─── String dialects (SQLite, DuckDB, Oracle, Snowflake) ─────────────────

    [Fact]
    public void Sqlite_Guid_ConvertsToString_HyphenatedFormat()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLoggerFactory.Instance.CreateLogger(nameof(SqliteDialect)));

        var param = dialect.CreateDbParameter("p", DbType.Guid, TestGuid);

        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal(TestGuid.ToString("D"), param.Value);
        Assert.Equal(36, param.Size);
    }

    [Fact]
    public void DuckDb_Guid_ConvertsToString_HyphenatedFormat()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var dialect = new DuckDbDialect(factory, NullLoggerFactory.Instance.CreateLogger(nameof(DuckDbDialect)));

        var param = dialect.CreateDbParameter("p", DbType.Guid, TestGuid);

        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal(TestGuid.ToString("D"), param.Value);
        Assert.Equal(36, param.Size);
    }

    [Fact]
    public void Oracle_Guid_ConvertsToString_HyphenatedFormat()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var dialect = new OracleDialect(factory, NullLoggerFactory.Instance.CreateLogger(nameof(OracleDialect)));

        var param = dialect.CreateDbParameter("p", DbType.Guid, TestGuid);

        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal(TestGuid.ToString("D"), param.Value);
        Assert.Equal(36, param.Size);
    }

    [Fact]
    public void Snowflake_Guid_ConvertsToString_HyphenatedFormat()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        var dialect = new SnowflakeDialect(factory, NullLoggerFactory.Instance.CreateLogger(nameof(SnowflakeDialect)));

        var param = dialect.CreateDbParameter("p", DbType.Guid, TestGuid);

        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal(TestGuid.ToString("D"), param.Value);
        Assert.Equal(36, param.Size);
    }

    // ─── Firebird (mode-configurable) ────────────────────────────────────────

    [Fact]
    public void Firebird_Guid_DefaultsBinary_16Bytes()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var dialect = new FirebirdDialect(factory, NullLogger.Instance);

        var param = dialect.CreateDbParameter("p", DbType.Guid, TestGuid);

        Assert.Equal(DbType.Binary, param.DbType);
        Assert.Equal(TestGuid.ToByteArray(), param.Value);
    }

    [Fact]
    public void Firebird_Guid_StringMode_ConvertsToHyphenatedString()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var dialect = new FirebirdDialect(factory, NullLogger.Instance)
        {
            GuidStorageMode = FirebirdGuidStorageMode.String
        };

        var param = dialect.CreateDbParameter("p", DbType.Guid, TestGuid);

        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal(TestGuid.ToString("D"), param.Value);
        Assert.Equal(36, param.Size);
    }

    // ─── Round-trip consistency: string format is always lowercase "D" ───────

    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void AllStringDialects_UseHyphenatedLowercaseFormat(string guidString)
    {
        var guid = Guid.Parse(guidString);
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLoggerFactory.Instance.CreateLogger(nameof(SqliteDialect)));

        var param = dialect.CreateDbParameter("p", DbType.Guid, guid);

        // "D" format = lowercase, 8-4-4-4-12 with hyphens
        Assert.Equal(guidString.ToLowerInvariant(), param.Value);
        Assert.Equal(36, ((string)param.Value!).Length);
    }

    // ─── Non-Guid types are unaffected ───────────────────────────────────────

    [Fact]
    public void Sqlite_NonGuidTypes_AreUnaffected()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new SqliteDialect(factory, NullLoggerFactory.Instance.CreateLogger(nameof(SqliteDialect)));

        var strParam = dialect.CreateDbParameter("s", DbType.String, "hello");
        var intParam = dialect.CreateDbParameter("i", DbType.Int32, 42);

        Assert.Equal(DbType.String, strParam.DbType);
        Assert.Equal("hello", strParam.Value);
        Assert.Equal(DbType.Int32, intParam.DbType);
        Assert.Equal(42, intParam.Value);
    }
}
