using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-018 (found by the 2026-10-04 performance review): every ExecuteNonQueryAsync copied the
/// whole statement text again (Query.ToString()) to classify it for error messages, and the
/// PostgreSQL family and Firebird copied it once more to look for type-catalog DDL / DDL. A large
/// batch statement landed each copy on the large-object heap. Rendering the command is the one copy
/// needed; nothing else may copy the text again.
/// </summary>
public class WriteSqlCopyAllocationTests
{
    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task ExecuteNonQueryAsync_CopiesTheStatementTextOnlyToRenderIt(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=x;EmulatedProduct={db}",
            DbMode = DbMode.SingleConnection
        }, factory);
        var sql = "INSERT INTO t (a) VALUES " + string.Join(", ", Enumerable.Range(0, 20_000).Select(i => $"({i})"));
        var textBytes = sql.Length * sizeof(char);

        for (var warm = 0; warm < 3; warm++)
        {
            await using var w = context.CreateSqlContainer(sql);
            await w.ExecuteNonQueryAsync();
        }

        await using var sc = context.CreateSqlContainer(sql);
        var before = GC.GetAllocatedBytesForCurrentThread();
        await sc.ExecuteNonQueryAsync();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < textBytes * 2, $"{allocated:N0} B allocated for a {textBytes:N0} B statement");
    }
}
