using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A PreventDatabaseUnload context keeps one sentinel connection open for its whole life. Db2 LUW deactivates a
/// database when its last application disconnects, so a sentinel that closes during ordinary work lets the database
/// go idle. Every operation the integration suite runs against Db2 must leave the sentinel open.
/// </summary>
public sealed class Db2SentinelLifetimeTests
{
    private static DatabaseContext Db2Context(out fakeDbFactory factory)
    {
        factory = new fakeDbFactory(SupportedDatabase.Db2);
        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=db2;Database=testdb;EmulatedProduct=Db2",
            DbMode = DbMode.PreventDatabaseUnload
        }, factory);
    }

    private static async Task Run(DatabaseContext context, string sql, ExecutionType type = ExecutionType.Write)
    {
        await using var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync(type);
    }

    [Fact]
    public async Task Sentinel_IsNeverLost_AcrossDdlAndReads()
    {
        using var context = Db2Context(out _);
        var floor = context.GetSentinelSnapshot().Count;
        Assert.True(floor >= 1, "a PreventDatabaseUnload context starts with at least one sentinel");

        string[] steps =
        {
            "DROP TABLE t",
            "CREATE TABLE t (id INTEGER NOT NULL PRIMARY KEY)",
            "INSERT INTO t (id) VALUES (1)",
            "CREATE INDEX ix_t_id ON t (id)",
            "DROP TABLE t"
        };

        foreach (var sql in steps)
        {
            await Run(context, sql);
            Assert.True(context.GetSentinelSnapshot().Count >= floor,
                $"after '{sql}' the sentinel count fell below {floor}");
        }
    }
}
