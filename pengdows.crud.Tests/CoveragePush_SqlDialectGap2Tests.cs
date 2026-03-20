using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Targeted tests covering uncovered paths in dialects/SqlDialect.cs:
/// - AppendPaging argument validation (lines 2372, 2377)
/// - RenderMergeSource null guards (lines 865, 870, 875)
/// - RenderMergeOnClause null predicate (line 909)
/// - AnalyzeException with OperationCanceledException (lines 1600-1606)
/// - IsUniqueViolation / IsForeignKeyViolation / IsNotNull / IsCheck default cases (lines 1476, 1511-1512, 1548)
/// - MessageIndicatesUniqueViolation with null/empty message (line 2679)
/// - ExtractProductNameFromVersion branches (lines 2001, 2006, 2011, 2031, 2036)
/// - IsPrime / GetPrime / TicksToMicroseconds edge cases via reflection (lines 2735, 2740, 1176, 1178, 2757-2765)
/// - IsValidParameterName edge cases via reflection (lines 2150, 2155, 2169)
/// - TryParseMajorVersion empty input (line 2719)
/// - TryGetProviderErrorCode / TryGetProviderSqlState via reflection (lines 2691-2695, 2702-2707)
/// - GetReadOnlyConnectionString with empty input (line 1308)
/// - IsSnapshotIsolationOn base returns false (line 1402)
/// - EvaluateSessionSettings exception handler (lines 691-694)
/// </summary>
public class CoveragePush_SqlDialectGap2Tests
{
    private static readonly BindingFlags NonPublicStatic =
        BindingFlags.NonPublic | BindingFlags.Static;

    private static IDatabaseContext CreateDuckDbContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        return new DatabaseContext("Data Source=:memory:;EmulatedProduct=DuckDB", factory);
    }

    private static IDatabaseContext CreateSqliteContext()
    {
        return new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));
    }

    // SqlServer dialect does not override ExtractProductNameFromVersion — uses base SqlDialect logic.
    private static IDatabaseContext CreateSqlServerContext()
    {
        return new DatabaseContext(
            "Data Source=.;EmulatedProduct=SqlServer",
            new fakeDbFactory(SupportedDatabase.SqlServer));
    }

    // Helper to get the SqlDialect from a context (it is internal but visible via InternalsVisibleTo)
    private static SqlDialect GetSqlDialect(IDatabaseContext ctx) => (SqlDialect)ctx.GetDialect();

    // =========================================================================
    // AppendPaging argument validation (lines 2372, 2377)
    // =========================================================================

    [Fact]
    public void AppendPaging_NegativeOffset_ThrowsArgumentOutOfRangeException()
    {
        using var ctx = CreateSqliteContext();
        using var query = new SqlQueryBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ctx.GetDialect().AppendPaging(query, offset: -1, limit: 10));
    }

    [Fact]
    public void AppendPaging_ZeroLimit_ThrowsArgumentOutOfRangeException()
    {
        using var ctx = CreateSqliteContext();
        using var query = new SqlQueryBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ctx.GetDialect().AppendPaging(query, offset: 0, limit: 0));
    }

    [Fact]
    public void AppendPaging_NegativeLimit_ThrowsArgumentOutOfRangeException()
    {
        using var ctx = CreateSqliteContext();
        using var query = new SqlQueryBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ctx.GetDialect().AppendPaging(query, offset: 0, limit: -1));
    }

    // =========================================================================
    // RenderMergeSource null guards (lines 865, 870, 875)
    // =========================================================================

    [Fact]
    public void RenderMergeSource_NullColumns_ThrowsArgumentNullException()
    {
        using var ctx = CreateDuckDbContext();
        var dialect = ctx.GetDialect();
        Assert.Throws<ArgumentNullException>(() =>
            dialect.RenderMergeSource(null!, new[] { "p0" }));
    }

    [Fact]
    public void RenderMergeSource_NullParameterNames_ThrowsArgumentNullException()
    {
        using var ctx = CreateDuckDbContext();
        var dialect = ctx.GetDialect();
        var mockCol = new Mock<IColumnInfo>();
        mockCol.SetupGet(c => c.Name).Returns("col1");
        mockCol.SetupGet(c => c.IsJsonType).Returns(false);
        Assert.Throws<ArgumentNullException>(() =>
            dialect.RenderMergeSource(new[] { mockCol.Object }, null!));
    }

    [Fact]
    public void RenderMergeSource_MismatchedCounts_ThrowsArgumentException()
    {
        using var ctx = CreateDuckDbContext();
        var dialect = ctx.GetDialect();
        var mockCol = new Mock<IColumnInfo>();
        mockCol.SetupGet(c => c.Name).Returns("col1");
        mockCol.SetupGet(c => c.IsJsonType).Returns(false);
        Assert.Throws<ArgumentException>(() =>
            dialect.RenderMergeSource(new[] { mockCol.Object }, new[] { "p0", "p1" }));
    }

    // =========================================================================
    // RenderMergeOnClause null predicate (line 909)
    // =========================================================================

    [Fact]
    public void RenderMergeOnClause_NullPredicate_ThrowsArgumentNullException()
    {
        using var ctx = CreateDuckDbContext();
        Assert.Throws<ArgumentNullException>(() =>
            ctx.GetDialect().RenderMergeOnClause(null!));
    }

    // =========================================================================
    // AnalyzeException with OperationCanceledException (lines 1600-1606)
    // =========================================================================

    [Fact]
    public void AnalyzeException_OperationCanceledException_ReturnsCategoryNone()
    {
        using var ctx = CreateSqliteContext();
        var info = ctx.GetDialect().AnalyzeException(new OperationCanceledException());
        Assert.Equal(DbErrorCategory.None, info.Category);
        Assert.Equal(DbConstraintKind.None, info.ConstraintKind);
        Assert.False(info.IsTransient);
        Assert.False(info.IsRetryable);
    }

    // =========================================================================
    // Violation detection — default cases (DuckDb hits the default: branch)
    // Lines: 1476 (FK), 1511-1512 (NotNull), 1548 (Check)
    // =========================================================================

    [Fact]
    public void IsForeignKeyViolation_UnknownDb_MessageContainsForeignKey_ReturnsTrue()
    {
        using var ctx = CreateDuckDbContext();
        var dialect = GetSqlDialect(ctx);
        var ex = new FakeDbException("foreign key constraint failed");
        Assert.True(dialect.IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_UnknownDb_UnrelatedMessage_ReturnsFalse()
    {
        using var ctx = CreateDuckDbContext();
        var dialect = GetSqlDialect(ctx);
        var ex = new FakeDbException("some other error");
        Assert.False(dialect.IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_UnknownDb_MessageContainsNotNull_ReturnsTrue()
    {
        using var ctx = CreateDuckDbContext();
        var dialect = GetSqlDialect(ctx);
        var ex = new FakeDbException("NOT NULL constraint violated");
        Assert.True(dialect.IsNotNullViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_UnknownDb_MessageContainsDashNotNull_ReturnsTrue()
    {
        using var ctx = CreateDuckDbContext();
        var dialect = GetSqlDialect(ctx);
        var ex = new FakeDbException("not-null constraint");
        Assert.True(dialect.IsNotNullViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_UnknownDb_UnrelatedMessage_ReturnsFalse()
    {
        using var ctx = CreateDuckDbContext();
        var dialect = GetSqlDialect(ctx);
        var ex = new FakeDbException("connection failed");
        Assert.False(dialect.IsNotNullViolation(ex));
    }

    [Fact]
    public void IsCheckConstraintViolation_UnknownDb_MessageContainsCheckConstraint_ReturnsTrue()
    {
        using var ctx = CreateDuckDbContext();
        var dialect = GetSqlDialect(ctx);
        var ex = new FakeDbException("check constraint failed on column x");
        Assert.True(dialect.IsCheckConstraintViolation(ex));
    }

    [Fact]
    public void IsCheckConstraintViolation_UnknownDb_UnrelatedMessage_ReturnsFalse()
    {
        using var ctx = CreateDuckDbContext();
        var dialect = GetSqlDialect(ctx);
        var ex = new FakeDbException("value out of range");
        Assert.False(dialect.IsCheckConstraintViolation(ex));
    }

    // =========================================================================
    // MessageIndicatesUniqueViolation with null/empty message (line 2679)
    // =========================================================================

    [Fact]
    public void MessageIndicatesUniqueViolation_NullMessage_ReturnsFalse()
    {
        var method = typeof(SqlDialect).GetMethod(
            "MessageIndicatesUniqueViolation", NonPublicStatic)!;
        var result = (bool)method.Invoke(null, new object?[] { null })!;
        Assert.False(result);
    }

    [Fact]
    public void MessageIndicatesUniqueViolation_EmptyMessage_ReturnsFalse()
    {
        var method = typeof(SqlDialect).GetMethod(
            "MessageIndicatesUniqueViolation", NonPublicStatic)!;
        var result = (bool)method.Invoke(null, new object?[] { "" })!;
        Assert.False(result);
    }

    [Fact]
    public void MessageIndicatesUniqueViolation_WhitespaceMessage_ReturnsFalse()
    {
        var method = typeof(SqlDialect).GetMethod(
            "MessageIndicatesUniqueViolation", NonPublicStatic)!;
        var result = (bool)method.Invoke(null, new object?[] { "   " })!;
        Assert.False(result);
    }

    // =========================================================================
    // ExtractProductNameFromVersion (lines 2001, 2006, 2011, 2031, 2036)
    // =========================================================================

    [Theory]
    [InlineData("Microsoft SQL Server 2019 (RTM) 15.0.2000.5", "Microsoft SQL Server")]
    [InlineData("8.0.27 MySQL Community Server", "MySQL")]
    [InlineData("10.6.4-MariaDB", "MariaDB")]
    [InlineData("3.39.2 SQLite", "SQLite")]
    [InlineData("Firebird 4.0.0.2496", "Firebird")]
    public void ExtractProductNameFromVersion_VariousVersionStrings_ReturnsExpectedName(
        string versionString, string expectedProduct)
    {
        // SqliteDialect overrides ExtractProductNameFromVersion; use SqlServer which delegates to base.
        using var ctx = CreateSqlServerContext();
        var dialect = GetSqlDialect(ctx);
        var result = dialect.ExtractProductNameFromVersion(versionString);
        Assert.Equal(expectedProduct, result);
    }

    // =========================================================================
    // IsPrime / GetPrime via reflection (lines 2735, 2740, 2745-2746, 2757-2765)
    // =========================================================================

    private static bool CallIsPrime(int n)
    {
        var method = typeof(SqlDialect).GetMethod("IsPrime", NonPublicStatic)!;
        return (bool)method.Invoke(null, new object[] { n })!;
    }

    private static int CallGetPrime(int min)
    {
        var method = typeof(SqlDialect).GetMethod("GetPrime", NonPublicStatic)!;
        return (int)method.Invoke(null, new object[] { min })!;
    }

    [Fact]
    public void IsPrime_One_ReturnsFalse()
    {
        Assert.False(CallIsPrime(1));
    }

    [Fact]
    public void IsPrime_Two_ReturnsTrue()
    {
        Assert.True(CallIsPrime(2));
    }

    [Fact]
    public void IsPrime_Four_ReturnsFalse()
    {
        Assert.False(CallIsPrime(4));
    }

    [Fact]
    public void IsPrime_Seventeen_ReturnsTrue()
    {
        Assert.True(CallIsPrime(17));
    }

    [Fact]
    public void GetPrime_ZeroOrLess_ReturnsTwo()
    {
        Assert.Equal(2, CallGetPrime(0));
        Assert.Equal(2, CallGetPrime(1));
        Assert.Equal(2, CallGetPrime(2));
    }

    [Fact]
    public void GetPrime_SeventeenIsAlreadyPrime_ReturnsSeventeen()
    {
        Assert.Equal(17, CallGetPrime(17));
    }

    [Fact]
    public void GetPrime_EighteenNotPrime_ReturnsNineteen()
    {
        Assert.Equal(19, CallGetPrime(18));
    }

    // =========================================================================
    // TicksToMicroseconds with <=0 ticks (lines 1176, 1178)
    // =========================================================================

    private static double CallTicksToMicroseconds(long ticks)
    {
        var method = typeof(SqlDialect).GetMethod("TicksToMicroseconds", NonPublicStatic)!;
        return (double)method.Invoke(null, new object[] { ticks })!;
    }

    [Fact]
    public void TicksToMicroseconds_ZeroTicks_ReturnsZero()
    {
        Assert.Equal(0d, CallTicksToMicroseconds(0L));
    }

    [Fact]
    public void TicksToMicroseconds_NegativeTicks_ReturnsZero()
    {
        Assert.Equal(0d, CallTicksToMicroseconds(-1L));
    }

    // =========================================================================
    // IsValidParameterName edge cases via reflection (lines 2150, 2155, 2169)
    // =========================================================================

    private static bool CallIsValidParameterName(string name)
    {
        var method = typeof(SqlDialect).GetMethod("IsValidParameterName", NonPublicStatic)!;
        return (bool)method.Invoke(null, new object[] { name })!;
    }

    [Fact]
    public void IsValidParameterName_StartsWithDigit_ReturnsFalse()
    {
        Assert.False(CallIsValidParameterName("0abc"));
    }

    [Fact]
    public void IsValidParameterName_EmptyString_ReturnsFalse()
    {
        Assert.False(CallIsValidParameterName(""));
    }

    [Fact]
    public void IsValidParameterName_ContainsHyphen_ReturnsFalse()
    {
        Assert.False(CallIsValidParameterName("a-b"));
    }

    [Fact]
    public void IsValidParameterName_ValidName_ReturnsTrue()
    {
        // First char must be [a-zA-Z]; subsequent chars may be alphanumeric or underscore.
        Assert.True(CallIsValidParameterName("validParam1"));
        Assert.True(CallIsValidParameterName("p_value"));
    }

    // =========================================================================
    // TryParseMajorVersion with empty/null input (line 2719)
    // =========================================================================

    private static bool CallTryParseMajorVersion(string? version, out int major)
    {
        var method = typeof(SqlDialect).GetMethod("TryParseMajorVersion", NonPublicStatic)!;
        var args = new object?[] { version, 0 };
        var result = (bool)method.Invoke(null, args)!;
        major = (int)args[1]!;
        return result;
    }

    [Fact]
    public void TryParseMajorVersion_EmptyString_ReturnsFalse()
    {
        Assert.False(CallTryParseMajorVersion("", out _));
    }

    [Fact]
    public void TryParseMajorVersion_NullString_ReturnsFalse()
    {
        Assert.False(CallTryParseMajorVersion(null, out _));
    }

    [Fact]
    public void TryParseMajorVersion_ValidVersion_ReturnsTrueWithMajor()
    {
        var result = CallTryParseMajorVersion("14.5.2", out var major);
        Assert.True(result);
        Assert.Equal(14, major);
    }

    // =========================================================================
    // TryGetProviderErrorCode via reflection (lines 2691-2695)
    // =========================================================================

    [Fact]
    public void TryGetProviderErrorCode_ExceptionWithNumberProperty_ReturnsCode()
    {
        var method = typeof(SqlDialect).GetMethod("TryGetProviderErrorCode", NonPublicStatic)!;
        var ex = new NumberedDbException(1062, "duplicate entry");
        var result = (int?)method.Invoke(null, new object[] { ex });
        Assert.Equal(1062, result);
    }

    // =========================================================================
    // TryGetProviderSqlState via reflection (lines 2702-2707)
    // =========================================================================

    [Fact]
    public void TryGetProviderSqlState_ExceptionWithSqlStateProperty_ReturnsState()
    {
        var method = typeof(SqlDialect).GetMethod("TryGetProviderSqlState", NonPublicStatic)!;
        var ex = new SqlStateDbException("23505", "unique violation");
        var result = (string?)method.Invoke(null, new object[] { ex });
        Assert.Equal("23505", result);
    }

    [Fact]
    public void TryGetProviderSqlState_ExceptionWithoutSqlStateProperty_ReturnsNull()
    {
        var method = typeof(SqlDialect).GetMethod("TryGetProviderSqlState", NonPublicStatic)!;
        var ex = new FakeDbException("plain error");
        var result = (string?)method.Invoke(null, new object[] { ex });
        Assert.Null(result);
    }

    // =========================================================================
    // GetReadOnlyConnectionString with empty input (line 1308)
    // =========================================================================

    [Fact]
    public void GetReadOnlyConnectionString_EmptyConnectionString_ReturnsEmpty()
    {
        using var ctx = CreateSqliteContext();
        var dialect = GetSqlDialect(ctx);
        var result = dialect.GetReadOnlyConnectionString("");
        Assert.Equal("", result);
    }

    [Fact]
    public void GetReadOnlyConnectionString_WhitespaceConnectionString_ReturnsUnchanged()
    {
        using var ctx = CreateSqliteContext();
        var dialect = GetSqlDialect(ctx);
        var result = dialect.GetReadOnlyConnectionString("   ");
        Assert.Equal("   ", result);
    }

    // =========================================================================
    // IsSnapshotIsolationOn base returns false (line 1402)
    // =========================================================================

    [Fact]
    public void IsSnapshotIsolationOn_BaseSqliteDialect_ReturnsFalse()
    {
        using var ctx = CreateSqliteContext();
        var dialect = GetSqlDialect(ctx);
        var mockConn = new Mock<ITrackedConnection>();
        var result = dialect.IsSnapshotIsolationOn(mockConn.Object);
        Assert.False(result);
    }

    // =========================================================================
    // EvaluateSessionSettings exception handler (lines 691-694)
    // TestDialectWrapper exposes the protected method
    // =========================================================================

    [Fact]
    public void EvaluateSessionSettings_EvaluatorThrows_FallbackIsUsedNoException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var wrapper = new TestDialectWrapper(factory);
        var fallbackCalled = wrapper.TestEvaluateSessionSettings_FallbackCalledWhenEvaluatorThrows();
        Assert.True(fallbackCalled, "Fallback should be called when evaluator throws");
    }

    [Fact]
    public void EvaluateSessionSettings_EvaluatorSucceeds_FallbackNotCalled()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var wrapper = new TestDialectWrapper(factory);
        var fallbackCalled = wrapper.TestEvaluateSessionSettings_FallbackCalledWhenEvaluatorSucceeds();
        Assert.False(fallbackCalled, "Fallback should NOT be called when evaluator succeeds");
    }

    // =========================================================================
    // Helper types
    // =========================================================================

    /// <summary>Minimal DbException with no extra properties.</summary>
    private sealed class FakeDbException : DbException
    {
        public FakeDbException(string message) : base(message) { }
    }

    /// <summary>DbException with a Number property (simulates MySql/SqlServer provider exceptions).</summary>
    private sealed class NumberedDbException : DbException
    {
        public int Number { get; }
        public NumberedDbException(int number, string message) : base(message) { Number = number; }
    }

    /// <summary>DbException with a SqlState property (simulates PostgreSQL provider exceptions).</summary>
    private sealed class SqlStateDbException : DbException
    {
        public new string SqlState { get; }
        public SqlStateDbException(string sqlState, string message) : base(message) { SqlState = sqlState; }
    }

    /// <summary>
    /// Test subclass of SqlDialect (internal abstract) that exposes EvaluateSessionSettings (protected).
    /// Used to cover the exception-handler path in that method.
    /// </summary>
    private sealed class TestDialectWrapper : SqlDialect
    {
        public TestDialectWrapper(DbProviderFactory factory)
            : base(factory, NullLogger.Instance) { }

        public override SupportedDatabase DatabaseType => SupportedDatabase.DuckDB;

        /// <summary>Returns true if the fallback was invoked when evaluator throws.</summary>
        public bool TestEvaluateSessionSettings_FallbackCalledWhenEvaluatorThrows()
        {
            var fallbackCalled = false;
            EvaluateSessionSettings(
                null!,
                _ => throw new InvalidOperationException("evaluator failed"),
                () =>
                {
                    fallbackCalled = true;
                    return default;
                },
                "test failure message");
            return fallbackCalled;
        }

        /// <summary>Returns true if the fallback was invoked when evaluator succeeds (should be false).</summary>
        public bool TestEvaluateSessionSettings_FallbackCalledWhenEvaluatorSucceeds()
        {
            var fallbackCalled = false;
            EvaluateSessionSettings(
                null!,
                _ => default,
                () =>
                {
                    fallbackCalled = true;
                    return default;
                },
                "test failure message");
            return fallbackCalled;
        }
    }
}
