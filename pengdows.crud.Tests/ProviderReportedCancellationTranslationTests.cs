using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// G5 (CONCURRENCY.md) regression. Live against a real SQL Server container
/// (CommandCancellationStopsServerExecutionSqlServerTests), cancelling a running command through
/// pengdows.crud genuinely stopped it on the server -- but Microsoft.Data.SqlClient reported that
/// as a SqlException ("...Operation cancelled by user.") instead of a real
/// OperationCanceledException, and pengdows.crud's translation fell through to the generic
/// DatabaseOperationException. That is a direct contradiction of the documented contract
/// ("OperationCanceledException is never wrapped — cancellation propagates as-is").
///
/// This pins the fix at the unit level, deterministically, using fakeDb's new
/// fakeDbConnection.CancellationThrowsAs (added specifically for this: a provider that doesn't
/// propagate cancellation as the real CLR type is exactly the kind of behavior fakeDb should be
/// able to simulate rather than requiring Docker for every regression check).
/// </summary>
public sealed class ProviderReportedCancellationTranslationTests
{
    private static (DatabaseContext Context, fakeDbConnection Connection) CreateGatedSqlServerContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=test;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.SingleConnection
        };
        var context = new DatabaseContext(config, factory);
        var connection = factory.CreatedConnections.Single();
        return (context, connection);
    }

    [Fact(Timeout = 10000)]
    public async Task ExecuteReaderAsync_WhenSqlServerReportsCancellationAsASqlException_StillThrowsOperationCanceled()
    {
        var (context, connection) = CreateGatedSqlServerContext();
        await using var _ = context;

        var gate = connection.SetExecuteGate();
        connection.CancellationThrowsAs = FakeDbConstraintErrors.ProviderError(
            "A severe error occurred on the current command.  The results, if any, should be discarded.\n" +
            "Operation cancelled by user.");

        using var sc = context.CreateSqlContainer("WAITFOR DELAY '00:00:30'");
        using var cts = new CancellationTokenSource();

        var task = sc.ExecuteReaderAsync(CommandType.Text, cts.Token).AsTask();
        await connection.ExecuteGateEntered;
        Assert.False(task.IsCompleted);

        cts.Cancel();

        // This is the actual bug: without the fix, this throws DatabaseOperationException
        // wrapping the fake SqlException instead.
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task ExecuteNonQueryAsync_WhenSqlServerReportsCancellationAsASqlException_StillThrowsOperationCanceled()
    {
        var (context, connection) = CreateGatedSqlServerContext();
        await using var _ = context;

        var gate = connection.SetExecuteGate();
        connection.CancellationThrowsAs = FakeDbConstraintErrors.ProviderError(
            "A severe error occurred on the current command.  The results, if any, should be discarded.\n" +
            "Operation cancelled by user.");

        using var sc = context.CreateSqlContainer("WAITFOR DELAY '00:00:30'");
        using var cts = new CancellationTokenSource();

        var task = sc.ExecuteNonQueryAsync(ExecutionType.Read, CommandType.Text, cts.Token).AsTask();
        await connection.ExecuteGateEntered;
        Assert.False(task.IsCompleted);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact(Timeout = 10000)]
    public async Task ExecuteReaderAsync_WhenProviderThrowsThatMessage_ButTheCallerNeverCancelled_IsNotReclassified()
    {
        // The fix must be gated on the caller's own token actually being canceled, not merely on
        // message content -- otherwise a provider exception that happens to use similar wording
        // for an unrelated reason (e.g. a DBA killing the session, which some providers also
        // phrase as "cancelled") would be silently misreported as this caller's own
        // cancellation, when nothing here ever requested one.
        var (context, connection) = CreateGatedSqlServerContext();
        await using var _ = context;

        var gate = connection.SetExecuteGate();
        connection.CancellationThrowsAs = FakeDbConstraintErrors.ProviderError(
            "A severe error occurred on the current command.  The results, if any, should be discarded.\n" +
            "Operation cancelled by user.");

        using var sc = context.CreateSqlContainer("WAITFOR DELAY '00:00:30'");

        var task = sc.ExecuteNonQueryAsync(ExecutionType.Read, CommandType.Text, CancellationToken.None).AsTask();
        await connection.ExecuteGateEntered;

        // Nothing ever cancels this token; release the gate itself so the only way the configured
        // exception surfaces is if ExecuteWithGateAsync's own cancellation-catch path fired
        // spuriously, which it must not since ct.IsCancellationRequested is false throughout.
        gate.TrySetResult(true);

        await task; // must complete normally; the fake exception is never even thrown.
    }
}
