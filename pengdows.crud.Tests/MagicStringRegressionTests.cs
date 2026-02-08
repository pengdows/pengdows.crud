// =============================================================================
// FILE: MagicStringRegressionTests.cs
// PURPOSE: Pins the exact error messages and SQL literals produced by the
//          library.  These tests exist solely to catch regressions when
//          extracting magic strings into named constants — if a constant's
//          value drifts from what the database actually expects, one of these
//          tests fails first.
// =============================================================================

#region

using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.strategies.proc;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class MagicStringRegressionTests
{
    // ── Proc-strategy error messages ──────────────────────────────────────

    [Fact]
    public void ExecProcWrappingStrategy_EmptyName_ThrowsExpectedMessage()
    {
        var strategy = new ExecProcWrappingStrategy();
        var ex = Assert.Throws<ArgumentException>(() =>
            strategy.Wrap("", ExecutionType.Write, ""));
        Assert.StartsWith("Procedure name cannot be null or empty.", ex.Message);
        Assert.Equal("procName", ex.ParamName);
    }

    [Fact]
    public void CallProcWrappingStrategy_EmptyName_ThrowsExpectedMessage()
    {
        var strategy = new CallProcWrappingStrategy();
        var ex = Assert.Throws<ArgumentException>(() =>
            strategy.Wrap("", ExecutionType.Write, ""));
        Assert.StartsWith("Procedure name cannot be null or empty.", ex.Message);
        Assert.Equal("procName", ex.ParamName);
    }

    [Fact]
    public void OracleProcWrappingStrategy_EmptyName_ThrowsExpectedMessage()
    {
        var strategy = new OracleProcWrappingStrategy();
        var ex = Assert.Throws<ArgumentException>(() =>
            strategy.Wrap("", ExecutionType.Write, ""));
        Assert.StartsWith("Procedure name cannot be null or empty.", ex.Message);
        Assert.Equal("procName", ex.ParamName);
    }

    [Fact]
    public void ExecuteProcedureWrappingStrategy_EmptyName_ThrowsExpectedMessage()
    {
        var strategy = new ExecuteProcedureWrappingStrategy();
        var ex = Assert.Throws<ArgumentException>(() =>
            strategy.Wrap("", ExecutionType.Write, ""));
        Assert.StartsWith("Procedure name cannot be null or empty.", ex.Message);
        Assert.Equal("procName", ex.ParamName);
    }

    [Fact]
    public void PostgresProcWrappingStrategy_EmptyName_ThrowsExpectedMessage()
    {
        var strategy = new PostgresProcWrappingStrategy();
        var ex = Assert.Throws<ArgumentException>(() =>
            strategy.Wrap("", ExecutionType.Write, ""));
        Assert.StartsWith("Procedure name cannot be null or empty.", ex.Message);
        Assert.Equal("procName", ex.ParamName);
    }

    // ── Oracle dialect SQL pins ──────────────────────────────────────────

    [Fact]
    public void OracleDialect_ReadWriteSettings_IsNlsOnly()
    {
        var d = CreateOracleDialect();
        using var ctx = CreateContext(SupportedDatabase.Oracle);
        Assert.Equal(
            "ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD';",
            d.GetConnectionSessionSettings(ctx, readOnly: false));
    }

    [Fact]
    public void OracleDialect_ReadOnlySettings_AppendsReadOnly()
    {
        var d = CreateOracleDialect();
        using var ctx = CreateContext(SupportedDatabase.Oracle);
        Assert.Equal(
            "ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD';\nALTER SESSION SET READ ONLY;",
            d.GetConnectionSessionSettings(ctx, readOnly: true));
    }

    [Fact]
    public void OracleDialect_ObsoleteSettings_MatchCurrentReadWrite()
    {
        var d = CreateOracleDialect();
        using var ctx = CreateContext(SupportedDatabase.Oracle);
#pragma warning disable CS0618
        var obsolete = d.GetConnectionSessionSettings();
#pragma warning restore CS0618
        Assert.Equal(d.GetConnectionSessionSettings(ctx, readOnly: false), obsolete);
    }

    // ── Firebird dialect SQL pins ─────────────────────────────────────────

    [Fact]
    public void FirebirdDialect_VersionQuery_IsEngineContext()
    {
        var d = CreateFirebirdDialect();
        Assert.Equal(
            "SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database",
            d.GetVersionQuery());
    }

    [Fact]
    public void FirebirdDialect_SessionSettings_SameForReadOnlyAndReadWrite()
    {
        var d = CreateFirebirdDialect();
        using var ctx = CreateContext(SupportedDatabase.Firebird);
        var expected = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;\nSET SQL DIALECT 3;";
        Assert.Equal(expected, d.GetConnectionSessionSettings(ctx, readOnly: false));
        Assert.Equal(expected, d.GetConnectionSessionSettings(ctx, readOnly: true));
    }

    [Fact]
    public void FirebirdDialect_ObsoleteSettings_MatchCurrentReadWrite()
    {
        var d = CreateFirebirdDialect();
        using var ctx = CreateContext(SupportedDatabase.Firebird);
#pragma warning disable CS0618
        var obsolete = d.GetConnectionSessionSettings();
#pragma warning restore CS0618
        Assert.Equal(d.GetConnectionSessionSettings(ctx, readOnly: false), obsolete);
    }

    // ── PostgreSQL read-only pins ─────────────────────────────────────────

    [Fact]
    public void PostgreSqlDialect_ReadOnlySessionSettings_Value()
    {
        var d = CreatePostgreSqlDialect();
        Assert.Equal("SET default_transaction_read_only = on", d.GetReadOnlySessionSettings());
    }

    [Fact]
    public void PostgreSqlDialect_ReadOnlyConnectionParameter_Value()
    {
        var d = CreatePostgreSqlDialect();
        Assert.Equal("Options='-c default_transaction_read_only=on'", d.GetReadOnlyConnectionParameter());
    }

    [Fact]
    public void PostgreSqlDialect_ReadOnlySettingName_ConsistentBetweenSessionAndConnParam()
    {
        var d = CreatePostgreSqlDialect();
        Assert.Contains("default_transaction_read_only", d.GetReadOnlySessionSettings());
        Assert.Contains("default_transaction_read_only", d.GetReadOnlyConnectionParameter()!);
    }

    // ── DuckDB read-only pins ─────────────────────────────────────────────

    [Fact]
    public void DuckDbDialect_ReadOnlySessionSettings_IsPragma()
    {
        var d = CreateDuckDbDialect();
        using var ctx = CreateContext(SupportedDatabase.DuckDB);
        Assert.Equal("PRAGMA read_only = 1;", d.GetConnectionSessionSettings(ctx, readOnly: true));
    }

    [Fact]
    public void DuckDbDialect_ReadWriteSessionSettings_IsEmpty()
    {
        var d = CreateDuckDbDialect();
        using var ctx = CreateContext(SupportedDatabase.DuckDB);
        Assert.Equal(string.Empty, d.GetConnectionSessionSettings(ctx, readOnly: false));
    }

    [Fact]
    public void DuckDbDialect_ReadOnlyConnectionParameter_Value()
    {
        var d = CreateDuckDbDialect();
        Assert.Equal("access_mode=READ_ONLY", d.GetReadOnlyConnectionParameter());
    }

    // ── Empty ID-list error message ──────────────────────────────────────

    [Table("magic_string_test")]
    private class PinEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)]   public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task RetrieveAsync_EmptyIds_ThrowsExpectedMessage()
    {
        using var ctx = CreateContext(SupportedDatabase.Sqlite);
        var helper = new TableGateway<PinEntity, int>(ctx);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            helper.RetrieveAsync(Array.Empty<int>()));
        Assert.StartsWith("List of IDs cannot be empty.", ex.Message);
        Assert.Equal("ids", ex.ParamName);
    }

    [Fact]
    public async Task DeleteAsync_EmptyIds_ThrowsExpectedMessage()
    {
        using var ctx = CreateContext(SupportedDatabase.Sqlite);
        var helper = new TableGateway<PinEntity, int>(ctx);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            helper.DeleteAsync(Array.Empty<int>()));
        Assert.StartsWith("List of IDs cannot be empty.", ex.Message);
        Assert.Equal("ids", ex.ParamName);
    }

    // ── MySQL / MariaDB read-only session pins ──────────────────────────

    [Fact]
    public void MySqlDialect_ReadOnlySettings_ContainsReadOnlyTransaction()
    {
        var d = CreateMySqlDialect();
        using var ctx = CreateContext(SupportedDatabase.MySql);
        Assert.Contains("SET SESSION TRANSACTION READ ONLY;",
            d.GetConnectionSessionSettings(ctx, readOnly: true));
    }

    [Fact]
    public void MariaDbDialect_ReadOnlySettings_ContainsReadOnlyTransaction()
    {
        var d = CreateMariaDbDialect();
        using var ctx = CreateContext(SupportedDatabase.MariaDb);
        Assert.Contains("SET SESSION TRANSACTION READ ONLY;",
            d.GetConnectionSessionSettings(ctx, readOnly: true));
    }

    // ── Internal constant value pins ──────────────────────────────────

    [Fact]
    public void ConnectionStringHelper_DataSourceKey_Value()
    {
        Assert.Equal("Data Source", ConnectionStringHelper.DataSourceKey);
    }

    [Fact]
    public void DatabaseContextConfiguration_PoolAcquireSeconds_Value()
    {
        Assert.Equal(5, DatabaseContextConfiguration.DefaultPoolAcquireSeconds);
    }

    [Fact]
    public void DatabaseContextConfiguration_ModeLockSeconds_Value()
    {
        Assert.Equal(30, DatabaseContextConfiguration.DefaultModeLockSeconds);
    }

    [Fact]
    public void SqlDialect_FallbackMaxPoolSize_Value()
    {
        Assert.Equal(100, SqlDialect.FallbackMaxPoolSize);
    }

    // ── ConnectionPoolingConfiguration behavior pins ─────────────────

    [Fact]
    public void ConnectionPoolingConfiguration_IsPoolingDisabled_RecognizesPoolingKey()
    {
        var builder = new DbConnectionStringBuilder { ["Pooling"] = "false" };
        Assert.True(ConnectionPoolingConfiguration.IsPoolingDisabled(builder));
    }

    [Fact]
    public void ConnectionPoolingConfiguration_ApplyPoolingDefaults_InjectsPoolingOnly()
    {
        var result = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            "Server=localhost;Database=test",
            SupportedDatabase.SqlServer,
            DbMode.Standard,
            supportsExternalPooling: true);

        Assert.Contains("Pooling", result);
        Assert.DoesNotContain("Min Pool Size", result);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static DatabaseContext CreateContext(SupportedDatabase db) =>
        new($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));

    private static OracleDialect CreateOracleDialect() =>
        new(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger<OracleDialect>.Instance);

    private static FirebirdDialect CreateFirebirdDialect() =>
        new(new fakeDbFactory(SupportedDatabase.Firebird), NullLogger<FirebirdDialect>.Instance);

    private static PostgreSqlDialect CreatePostgreSqlDialect() =>
        new(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<PostgreSqlDialect>.Instance);

    private static DuckDbDialect CreateDuckDbDialect() =>
        new(new fakeDbFactory(SupportedDatabase.DuckDB), NullLogger<DuckDbDialect>.Instance);

    private static MySqlDialect CreateMySqlDialect() =>
        new(new fakeDbFactory(SupportedDatabase.MySql), NullLogger<MySqlDialect>.Instance);

    private static MariaDbDialect CreateMariaDbDialect() =>
        new(new fakeDbFactory(SupportedDatabase.MariaDb), NullLogger<MariaDbDialect>.Instance);
}
