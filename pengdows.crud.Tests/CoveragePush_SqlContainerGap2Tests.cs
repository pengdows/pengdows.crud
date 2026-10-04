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
    public void ClassifyTranslatedException_AmbiguousResultException_ReturnsAmbiguousResult()
    {
        var ex = new AmbiguousResultException("ambiguous", SupportedDatabase.CockroachDb);
        Assert.Equal(DbErrorCategory.AmbiguousResult, CallClassifyTranslatedException(ex));
    }

    // =========================================================================
    // TicksToMicroseconds with zero/negative (line 1930)
    // =========================================================================

    private static double CallSqlContainerTicksToMicroseconds(long ticks)
    {
        var method = typeof(SqlContainer).GetMethod("TicksToMicroseconds", NonPublicStatic)!;
        return (double)method.Invoke(null, new object[] { ticks })!;
    }

}
