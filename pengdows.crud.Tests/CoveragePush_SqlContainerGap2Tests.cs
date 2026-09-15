using System;
using System.Data;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Targeted tests covering uncovered paths in SqlContainer.cs:
/// - MaxParameterLimit overflow (lines 694-695)
/// - ClassifyTranslatedException switch cases via reflection (lines 1850-1857)
/// - TicksToMicroseconds with zero/negative ticks via reflection (line 1930)
/// </summary>
[Collection("SqliteSerial")]
public class CoveragePush_SqlContainerGap2Tests : SqlLiteContextTestBase
{
    private static readonly BindingFlags NonPublicStatic =
        BindingFlags.NonPublic | BindingFlags.Static;

    // =========================================================================
    // MaxParameterLimit overflow (lines 694-695)
    // =========================================================================

    [Fact]
    public void CreateCommand_ExceedsMaxParameterLimit_ThrowsInvalidOperationException()
    {
        // The check fires in SqlContainer.CreateCommand(ITrackedConnection).
        // SupportedDatabase.Unknown → Sql92Dialect → MaxParameterLimit = 2000 (base default).
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        using var ctx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Unknown", factory);

        var limit = ctx.MaxParameterLimit; // 2000
        var sc = (SqlContainer)ctx.CreateSqlContainer("SELECT 1");
        for (var i = 0; i <= limit; i++)
        {
            sc.AddParameterWithValue(DbType.Int32, i);
        }

        var conn = ctx.GetConnection(ExecutionType.Write, false);
        var ex = Assert.Throws<InvalidOperationException>(() => sc.CreateCommand(conn));
        Assert.Contains("maximum parameter limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // ClassifyTranslatedException via reflection (lines 1850-1857)
    // =========================================================================

    private static DbErrorCategory CallClassifyTranslatedException(DatabaseException ex)
    {
        var method = typeof(SqlContainer).GetMethod(
            "ClassifyTranslatedException", NonPublicStatic)!;
        return (DbErrorCategory)method.Invoke(null, new object[] { ex })!;
    }

    [Fact]
    public void ClassifyTranslatedException_DeadlockException_ReturnsDeadlock()
    {
        var ex = new DeadlockException("deadlock", SupportedDatabase.Sqlite);
        Assert.Equal(DbErrorCategory.Deadlock, CallClassifyTranslatedException(ex));
    }

    [Fact]
    public void ClassifyTranslatedException_SerializationConflict_ReturnsSerializationFailure()
    {
        var ex = new SerializationConflictException("serialization", SupportedDatabase.PostgreSql);
        Assert.Equal(DbErrorCategory.SerializationFailure, CallClassifyTranslatedException(ex));
    }

    [Fact]
    public void ClassifyTranslatedException_UniqueConstraintViolation_ReturnsConstraintViolation()
    {
        var ex = new UniqueConstraintViolationException("unique", SupportedDatabase.MySql);
        Assert.Equal(DbErrorCategory.ConstraintViolation, CallClassifyTranslatedException(ex));
    }

    [Fact]
    public void ClassifyTranslatedException_CommandTimeoutException_ReturnsTimeout()
    {
        var ex = new CommandTimeoutException("timeout", SupportedDatabase.SqlServer);
        Assert.Equal(DbErrorCategory.Timeout, CallClassifyTranslatedException(ex));
    }

    [Fact]
    public void ClassifyTranslatedException_OtherDatabaseException_ReturnsUnknown()
    {
        var ex = new DatabaseOperationException("other", SupportedDatabase.Sqlite);
        Assert.Equal(DbErrorCategory.Unknown, CallClassifyTranslatedException(ex));
    }

    // =========================================================================
    // TicksToMicroseconds with zero/negative (line 1930)
    // =========================================================================

    private static double CallSqlContainerTicksToMicroseconds(long ticks)
    {
        var method = typeof(SqlContainer).GetMethod("TicksToMicroseconds", NonPublicStatic)!;
        return (double)method.Invoke(null, new object[] { ticks })!;
    }

    [Fact]
    public void SqlContainer_TicksToMicroseconds_ZeroTicks_ReturnsZero()
    {
        Assert.Equal(0d, CallSqlContainerTicksToMicroseconds(0L));
    }

    [Fact]
    public void SqlContainer_TicksToMicroseconds_NegativeTicks_ReturnsZero()
    {
        Assert.Equal(0d, CallSqlContainerTicksToMicroseconds(-100L));
    }
}
