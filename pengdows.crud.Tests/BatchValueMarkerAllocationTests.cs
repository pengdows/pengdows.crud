using System;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-012: the batch SQL builders allocated each cell's parameter marker as a string
/// (string.Concat(marker, "b", index)) before appending it. The pieces are appended directly.
/// </summary>
public sealed class BatchValueMarkerAllocationTests
{
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Sqlite)]
    public void BatchInsertSql_IntoASizedBuilder_AllocatesNoMarkers(SupportedDatabase db)
    {
        using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));
        var dialect = context.Dialect;
        var columns = new[] { "\"a\"", "\"b\"", "\"c\"", "\"d\"" };
        object boxed = 1; // batch cells are already-extracted objects; don't box in the test
        Func<int, int, object?> value = (_, _) => boxed;
        using var sc = context.CreateSqlContainer();
        dialect.BuildBatchInsertSql("\"t\"", columns, 50, sc.Query, value);
        sc.Query.Clear();
        dialect.BuildBatchInsertSql("\"t\"", columns, 50, sc.Query, value);
        sc.Query.Clear();

        var before = GC.GetAllocatedBytesForCurrentThread();
        dialect.BuildBatchInsertSql("\"t\"", columns, 50, sc.Query, value);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // 200 cells: anything per cell would be thousands of bytes.
        Assert.True(allocated < 200, $"{allocated} B for 200 cells");
    }
}
