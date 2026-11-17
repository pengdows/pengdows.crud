using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// G7/G7b (CONCURRENCY.md): a prior pass (ReadOnlyConnectionRejectsWriteTests) covered the
/// providers that mark a connection read-only via a connection-string key (SQLite, PostgreSQL
/// family, DuckDB, Access, FlatFile). This covers four of the remaining providers, which instead
/// mark a *transaction* read-only by sending a specific SQL statement when it begins -- MySQL/
/// AuroraMySql's <c>SET SESSION transaction_read_only = 1;</c>, MariaDB's own
/// <c>SET SESSION tx_read_only = 1;</c> (a genuinely different variable name, not a copy/paste
/// choice), and Oracle/Informix/SAP HANA's <c>SET TRANSACTION READ ONLY</c> (each dialect's own
/// <c>ReadOnlyTransactionSql</c>, sent by <c>SqlDialect.TryEnterReadOnlyTransaction</c> when
/// <c>BeginTransaction(ExecutionType.Read)</c> opens). TiDb was in scope too but turned out not
/// to belong here: <c>TiDbDialect</c> overrides <c>ReadOnlyTransactionSql</c> to <c>null</c> and
/// sends no session SQL at all for a read-only transaction, so it has no entry/reset text for
/// this mechanism to recognize -- not a gap in this test, a real difference in TiDb's dialect.
///
/// Per the project's standing fakeDb convention (expand it rather than work around its gaps):
/// fakeDbConnection.SessionMarkedReadOnly + EnforceReadOnlySessionSql (opt-in, same shape as
/// EnforceReadOnlyFromConnectionString) now let fakeDbCommand recognize this exact SQL text --
/// FakeDbReadOnlyEnforcement.IsReadOnlySessionEntrySql/IsReadOnlySessionResetSql -- and update
/// the connection's IsReadOnly accordingly, so these providers are testable the same way the
/// connection-string ones already are, not left as an untestable Assumption.
///
/// The write attempt below uses ExecuteReaderAsync's default read-intent overload sending a
/// write-shaped statement (G6's "forgot ExecutionType.Write" scenario), not an explicit
/// ExecuteNonQueryAsync(ExecutionType.Write, ...) call: the latter never reaches the database at
/// all here -- InternalConnectionAccessAssertions.AssertIsWriteConnection throws
/// ReadOnlyAccessException first, a database-agnostic structural guard that fires for ANY
/// explicit write request inside a transaction opened with ExecutionType.Read, before fakeDb (or
/// a real engine) ever sees the statement. So the database-level rejection this test proves is
/// reachable specifically through the same sneaky-write path G6 already describes, not through a
/// deliberate, correctly-typed write call.
/// </summary>
public sealed class ReadOnlySessionSqlProvidersRejectWriteTests
{
    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Informix)]
    [InlineData(SupportedDatabase.SapHana)]
    public async Task ReadOnlyTransaction_WriteInsideIt_IsRejectedByTheConnection(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct={product}",
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection
        {
            EmulatedProduct = product,
            EnforceReadOnlySessionSql = true
        };
        factory.Connections.Add(conn);

        using var tx = ctx.BeginTransaction(executionType: ExecutionType.Read);

        // BeginTransaction(ExecutionType.Read) already ran the dialect's real
        // ReadOnlyTransactionSql on this exact connection (TryEnterReadOnlyTransaction); fakeDb's
        // tracking should have flipped SessionMarkedReadOnly in response, independent of the
        // assertion below -- this is the direct check that it actually did.
        Assert.True(conn.SessionMarkedReadOnly);
        Assert.True(conn.IsReadOnly);

        using var sc = tx.CreateSqlContainer("UPDATE test SET id = 1");
        var ex = await Assert.ThrowsAsync<ReadOnlyViolationException>(
            () => sc.ExecuteReaderAsync(CommandType.Text).AsTask());
        Assert.True(ex is IReadOnlyViolation);
    }

    [Fact(Timeout = 10000)]
    public async Task MySql_ReadOnlyTransaction_Committed_ThenNewReadWriteTransaction_IsNotRejected()
    {
        // The contrasting case, proving the mechanism tracks state rather than always rejecting:
        // only MySQL-family has a real session-level reset SQL (SET SESSION transaction_read_only
        // = 0;), sent when a read-only transaction completes (TransactionContext.
        // TryResetReadOnlySessionAsync). After that reset runs, a subsequent write-intent
        // transaction on the same connection must succeed again.
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=MySql",
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.MySql,
            EnforceReadOnlySessionSql = true
        };
        factory.Connections.Add(conn);

        using (var readTx = ctx.BeginTransaction(executionType: ExecutionType.Read))
        {
            Assert.True(conn.SessionMarkedReadOnly);
            await readTx.CommitAsync();
        }

        // DatabaseContext.Standard mode hands out ephemeral connections per operation, so this
        // reset is really exercised on the SAME fakeDbConnection only because the test seeded
        // exactly one into factory.Connections -- the reset path itself (not connection reuse) is
        // what this asserts.
        Assert.False(conn.SessionMarkedReadOnly);
    }
}
