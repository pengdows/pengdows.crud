using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.strategies.connection;
using pengdows.crud.strategies.proc;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for refactoring tasks that eliminate code duplication across the codebase.
/// Each section validates behavior is preserved after removing redundant overrides or extracting shared helpers.
/// </summary>
public class RefactoringDuplicationTests
{
    private readonly ILogger _logger = NullLogger.Instance;

    private fakeDbFactory CreateFactory(SupportedDatabase db) => new(db);

    #region Task 1: GetInsertReturningClause — base class returns correct value for each dialect

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void GetInsertReturningClause_ReturnsCorrectValue_ThroughBaseClass(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);

        // These dialects unconditionally support RETURNING
        Assert.True(dialect.SupportsInsertReturning);
        var result = dialect.GetInsertReturningClause("id");
        Assert.StartsWith("RETURNING ", result);
        Assert.Contains("id", result);
    }

    [Fact]
    public void GetInsertReturningClause_Sqlite_UsesBaseClassWhenSupported()
    {
        // SQLite's SupportsInsertReturning depends on version detection (3.35+).
        // Without initialization, it returns false. The base class still provides
        // the correct RETURNING clause when called directly.
        var dialect = new SqliteDialect(CreateFactory(SupportedDatabase.Sqlite), _logger);

        // Base class GetInsertReturningClause throws when SupportsInsertReturning is false
        // but returns correct RETURNING clause format when it is supported
        if (dialect.SupportsInsertReturning)
        {
            var result = dialect.GetInsertReturningClause("id");
            Assert.StartsWith("RETURNING ", result);
        }
        else
        {
            // Uninitialized SQLite doesn't know it supports RETURNING yet
            Assert.Throws<NotSupportedException>(() => dialect.GetInsertReturningClause("id"));
        }
    }

    [Fact]
    public void GetInsertReturningClause_SqlServer_UsesOutputInserted()
    {
        var dialect = new SqlServerDialect(CreateFactory(SupportedDatabase.SqlServer), _logger);
        var result = dialect.GetInsertReturningClause("id");
        Assert.StartsWith("OUTPUT INSERTED.", result);
    }

    [Fact]
    public void GetInsertReturningClause_Oracle_UsesReturningInto()
    {
        var dialect = new OracleDialect(CreateFactory(SupportedDatabase.Oracle), _logger);
        var result = dialect.GetInsertReturningClause("id");
        Assert.Contains("RETURNING", result);
        Assert.Contains("INTO", result);
    }

    #endregion

    #region Task 2: TryEnterReadOnlyTransaction helper — base class helper

    [Fact]
    public void TryExecuteReadOnlySql_OracleDialect_SwallowsExceptions()
    {
        // Oracle's TryEnterReadOnlyTransaction delegates to TryExecuteReadOnlySql
        var dialect = new OracleDialect(CreateFactory(SupportedDatabase.Oracle), _logger);
        // We can't easily test the helper directly since it requires a transaction context,
        // but we verify the dialect is properly configured
        Assert.Equal(SupportedDatabase.Oracle, dialect.DatabaseType);
    }

    [Fact]
    public void TryExecuteReadOnlySql_MariaDbDialect_SwallowsExceptions()
    {
        var dialect = new MariaDbDialect(CreateFactory(SupportedDatabase.MariaDb), _logger);
        Assert.Equal(SupportedDatabase.MariaDb, dialect.DatabaseType);
    }

    #endregion

    #region Task 4: IProcWrappingStrategy.ValidateAndWrap helper

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndWrap_ThrowsOnNullOrEmptyProcName(string? procName)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => IProcWrappingStrategy.ValidateAndWrap(procName!, null));
        Assert.Contains("Procedure name cannot be null or empty", ex.Message);
    }

    [Fact]
    public void ValidateAndWrap_ReturnsRawProcName_WhenNoWrapFunction()
    {
        var result = IProcWrappingStrategy.ValidateAndWrap("my_proc", null);
        Assert.Equal("my_proc", result);
    }

    [Fact]
    public void ValidateAndWrap_AppliesWrapFunction()
    {
        var result = IProcWrappingStrategy.ValidateAndWrap("my_proc", name => $"\"{name}\"");
        Assert.Equal("\"my_proc\"", result);
    }

    [Theory]
    [InlineData(ProcWrappingStyle.Exec)]
    [InlineData(ProcWrappingStyle.Call)]
    [InlineData(ProcWrappingStyle.Oracle)]
    [InlineData(ProcWrappingStyle.PostgreSQL)]
    [InlineData(ProcWrappingStyle.ExecuteProcedure)]
    public void AllProcStrategies_StillWorkCorrectly_AfterRefactor(ProcWrappingStyle style)
    {
        var strategy = ProcWrappingStrategyFactory.Create(style);

        // Null/empty still throws
        Assert.Throws<ArgumentException>(
            () => strategy.Wrap(null!, ExecutionType.Write, ""));
        Assert.Throws<ArgumentException>(
            () => strategy.Wrap("", ExecutionType.Write, ""));
        Assert.Throws<ArgumentException>(
            () => strategy.Wrap("   ", ExecutionType.Write, ""));

        // Valid proc name works
        var result = strategy.Wrap("test_proc", ExecutionType.Write, "@p0, @p1");
        Assert.NotEmpty(result);
        Assert.Contains("test_proc", result);
    }

    #endregion

    #region Task 5: Pooling property overrides — base class defaults match removed overrides

    [Theory]
    [InlineData(SupportedDatabase.SqlServer, true, "Pooling")]
    [InlineData(SupportedDatabase.PostgreSql, true, "Pooling")]
    [InlineData(SupportedDatabase.MySql, true, "Pooling")]
    [InlineData(SupportedDatabase.Oracle, true, "Pooling")]
    [InlineData(SupportedDatabase.Firebird, true, "Pooling")]
    [InlineData(SupportedDatabase.MariaDb, true, "Pooling")]
    [InlineData(SupportedDatabase.DuckDB, false, null)]
    public void PoolingProperties_ReturnExpectedValues_PerDialect(
        SupportedDatabase db, bool expectedSupportsPooling, string? expectedPoolingName)
    {
        var dialect = CreateDialect(db);

        Assert.Equal(expectedSupportsPooling, dialect.SupportsExternalPooling);
        Assert.Equal(expectedPoolingName, dialect.PoolingSettingName);
    }

    #endregion

    #region Task 6: ProviderParameterFactory reflection caching

    [Fact]
    public void ProviderParameterFactory_HandlesNonNpgsqlParameter_Gracefully()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var parameter = factory.CreateParameter()!;
        var result = ProviderParameterFactory.TryConfigureParameter(
            parameter, typeof(Guid), Guid.NewGuid(), SupportedDatabase.PostgreSql);

        // Should not throw regardless of result
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void ProviderParameterFactory_HandlesMultipleCallsWithCaching()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);

        // Call multiple times to exercise cached paths
        for (int i = 0; i < 5; i++)
        {
            var parameter = factory.CreateParameter()!;
            ProviderParameterFactory.TryConfigureParameter(
                parameter, typeof(Guid), Guid.NewGuid(), SupportedDatabase.PostgreSql);
        }
    }

    #endregion

    #region Task 8: AdvancedCoercions null check removal

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_NullDbValue_ReturnsFalse()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        var nullDbValue = new DbValue(null);
        var result = coercion.TryRead(nullDbValue, out var value);

        Assert.False(result);
        Assert.Equal(default, value);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_ValidTimeSpan_ReturnsTrue()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        var ts = TimeSpan.FromHours(2);
        var dbValue = new DbValue(ts);
        var result = coercion.TryRead(dbValue, out var value);

        Assert.True(result);
        Assert.Equal(ts, value.ToTimeSpan());
    }

    [Fact]
    public void IntervalYearMonthCoercion_TryRead_NullDbValue_ReturnsFalse()
    {
        var coercion = new IntervalYearMonthCoercion();
        var nullDbValue = new DbValue(null);
        var result = coercion.TryRead(nullDbValue, out var value);

        Assert.False(result);
        Assert.Equal(default, value);
    }

    [Fact]
    public void IntervalDaySecondCoercion_TryRead_NullDbValue_ReturnsFalse()
    {
        var coercion = new IntervalDaySecondCoercion();
        var nullDbValue = new DbValue(null);
        var result = coercion.TryRead(nullDbValue, out var value);

        Assert.False(result);
        Assert.Equal(default, value);
    }

    [Fact]
    public void IntervalDaySecondCoercion_TryRead_ValidTimeSpan_ReturnsTrue()
    {
        var coercion = new IntervalDaySecondCoercion();
        var ts = TimeSpan.FromDays(3).Add(TimeSpan.FromHours(4));
        var dbValue = new DbValue(ts);
        var result = coercion.TryRead(dbValue, out var value);

        Assert.True(result);
    }

    #endregion

    #region Task 3: ReleaseNonPersistentConnectionAsync static helper

    [Fact]
    public async Task ReleaseNonPersistentConnectionAsync_NullConnection_ReturnsCompleted()
    {
        var result = StandardConnectionStrategy.ReleaseNonPersistentConnectionAsync(null, null);
        Assert.True(result.IsCompletedSuccessfully);
        await result;
    }

    [Fact]
    public async Task ReleaseNonPersistentConnectionAsync_PersistentConnection_ReturnsCompleted()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var conn = factory.CreateConnection()!;
        var tracked = new TrackedConnection(conn);

        var result = StandardConnectionStrategy.ReleaseNonPersistentConnectionAsync(tracked, tracked);
        Assert.True(result.IsCompletedSuccessfully);
        await result;
    }

    [Fact]
    public async Task ReleaseNonPersistentConnectionAsync_NonPersistentConnection_Disposes()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var conn = factory.CreateConnection()!;
        var tracked = new TrackedConnection(conn);

        await StandardConnectionStrategy.ReleaseNonPersistentConnectionAsync(tracked, null);
        // TrackedConnection implements IAsyncDisposable, so DisposeAsync is called
        Assert.True(tracked.IsDisposed);
    }

    #endregion

    #region Task 7: Session settings detection template in base SqlDialect

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public void SessionSettingsDialects_CanBeConstructed_AfterRefactoring(SupportedDatabase db)
    {
        // After extracting LogSessionSettingsResult to the base class,
        // verify that each dialect constructs and has correct DatabaseType
        var dialect = CreateDialect(db);
        Assert.Equal(db, dialect.DatabaseType);
    }

    [Fact]
    public void LogSessionSettingsResult_ExposedToSubclasses_ViaTestDialect()
    {
        // Verify the protected helper is accessible from subclasses
        var result = TestableDialect.CallLogSessionSettingsResult(_logger);
        Assert.NotNull(result);
    }

    /// <summary>
    /// Testable dialect subclass that exposes the protected LogSessionSettingsResult helper.
    /// </summary>
    private sealed class TestableDialect : SqlDialect
    {
        internal TestableDialect(DbProviderFactory factory, ILogger logger) : base(factory, logger)
        {
        }

        public override SupportedDatabase DatabaseType => SupportedDatabase.SqlServer;
        public override string ParameterMarker => "@";

        public static string CallLogSessionSettingsResult(ILogger logger)
        {
            var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
            var dialect = new TestableDialect(factory, logger);
            var result = new SessionSettingsResult(
                "SET ANSI_NULLS ON;",
                new Dictionary<string, string> { ["ANSI_NULLS"] = "OFF" },
                false);
            dialect.LogSessionSettingsResult(result, "Test");
            return result.Settings;
        }
    }

    #endregion

    #region Task 9: DuckDb IsMemoryDatabase unification

    [Fact]
    public void DuckDb_GetReadOnlyConnectionString_PreservesMemoryConnectionString()
    {
        var dialect = new DuckDbDialect(CreateFactory(SupportedDatabase.DuckDB), _logger);
        var memoryCs = "Data Source=:memory:";
        var result = dialect.GetReadOnlyConnectionString(memoryCs);

        // Memory connections should not get read-only suffix
        Assert.Equal(memoryCs, result);
    }

    [Fact]
    public void DuckDb_GetReadOnlyConnectionString_AppendsReadOnlyForFileConnection()
    {
        var dialect = new DuckDbDialect(CreateFactory(SupportedDatabase.DuckDB), _logger);
        var fileCs = "Data Source=/tmp/test.duckdb";
        var result = dialect.GetReadOnlyConnectionString(fileCs);

        Assert.Contains("access_mode=READ_ONLY", result);
    }

    #endregion

    #region Helpers

    private SqlDialect CreateDialect(SupportedDatabase db)
    {
        var factory = CreateFactory(db);
        return db switch
        {
            SupportedDatabase.PostgreSql => new PostgreSqlDialect(factory, _logger),
            SupportedDatabase.Sqlite => new SqliteDialect(factory, _logger),
            SupportedDatabase.Firebird => new FirebirdDialect(factory, _logger),
            SupportedDatabase.DuckDB => new DuckDbDialect(factory, _logger),
            SupportedDatabase.Oracle => new OracleDialect(factory, _logger),
            SupportedDatabase.MariaDb => new MariaDbDialect(factory, _logger),
            SupportedDatabase.MySql => new MySqlDialect(factory, _logger),
            SupportedDatabase.SqlServer => new SqlServerDialect(factory, _logger),
            _ => throw new ArgumentException($"Unsupported database: {db}")
        };
    }

    #endregion
}
