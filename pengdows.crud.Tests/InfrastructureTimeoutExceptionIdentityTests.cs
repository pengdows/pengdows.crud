using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// Discovered while writing TEST-001 (two-tenant failure containment): CLAUDE.md documents
// ModeContentionException as deliberately "not part of [the DatabaseException] hierarchy" and
// states "a catch (DatabaseException) will not catch it." PoolSaturatedException is built the
// same way (extends TimeoutException directly, carries its own rich Snapshot, is not a
// DatabaseException). Both are meant to propagate to the caller as themselves so that code
// specifically reacting to admission-control backpressure sees the real type.
//
// SqlContainer's own catch (Exception ex) when (ex is not DatabaseException && IsTimeout(ex))
// clause treats ANY TimeoutException-derived exception as a raw provider timeout and translates
// it into CommandTimeoutException via the dialect's exception translator — silently destroying
// the documented type-identity contract for these two specific, intentionally-hierarchy-exempt
// exceptions whenever they originate from inside an actual command execution (as opposed to,
// e.g., BeginTransaction, which does not go through this code path and was already covered).
public class InfrastructureTimeoutExceptionIdentityTests
{
    [Fact]
    public async Task PoolSaturatedException_PropagatesUnwrapped_FromCommandExecution_NotTranslatedToCommandTimeoutException()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=saturation-identity;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            MaxConcurrentWrites = 1,
            PoolAcquireTimeout = TimeSpan.FromMilliseconds(150)
        };
        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));

        // Saturate the single write slot by holding a write connection open.
        var held = context.GetConnection(ExecutionType.Write);
        try
        {
            using var sc = context.CreateSqlContainer("INSERT INTO t (x) VALUES (1)");

            var ex = await Assert.ThrowsAsync<PoolSaturatedException>(
                () => sc.ExecuteNonQueryAsync(CommandType.Text).AsTask());

            Assert.Equal(PoolLabel.Writer, ex.PoolLabel);
        }
        finally
        {
            context.CloseAndDisposeConnection(held);
        }
    }

    [Fact]
    public async Task ModeContentionException_PropagatesUnwrapped_FromOrdinaryOpDuringActiveTransaction_NotTranslatedToCommandTimeoutException()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            ModeLockTimeout = TimeSpan.FromMilliseconds(150)
        };
        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));

        // Hold the SingleConnection transaction gate open with an active transaction.
        using var txn = context.BeginTransaction();

        // An ordinary (non-transactional) operation on the same context must wait for the gate
        // and, once ModeLockTimeout elapses, fail with ModeContentionException — not a translated
        // CommandTimeoutException.
        using var sc = context.CreateSqlContainer("SELECT 1");

        var ex = await Assert.ThrowsAsync<ModeContentionException>(
            () => sc.ExecuteScalarOrNullAsync<int>(CommandType.Text).AsTask());

        Assert.NotNull(ex);
    }
}
