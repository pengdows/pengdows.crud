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
/// Firebird needs a bare ROLLBACK after a failed write, or the provider's implicit transaction and
/// its row locks stay on the pooled connection. The compensating rollback ran with the caller's
/// token, so a write that failed because it was cancelled (the token already cancelled) skipped it:
/// ExecuteNonQueryAsync("ROLLBACK") threw at once and the failure was only logged at Debug.
/// </summary>
public sealed class FailedWriteRollbackCancellationTests
{
    private static async Task<(DatabaseContext Context, fakeDbConnection Exec, TaskCompletionSource<bool> Gate)> CreateAsync()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Firebird });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Firebird };
        var gate = exec.SetExecuteGate();
        factory.Connections.Add(exec);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Firebird",
            DbMode = DbMode.Standard
        }, factory);
        await Task.CompletedTask;
        return (context, exec, gate);
    }

    private static async Task CancelWhileExecutingAsync(fakeDbConnection exec, CancellationTokenSource cts,
        TaskCompletionSource<bool> gate)
    {
        // Cancel once the write is in flight at the gate, then release the gate for the rollback
        // (REV-067: fixed 50 ms delays guessed at both). Cancel() completes the gated wait
        // synchronously, so the release can follow at once.
        await exec.ExecuteGateEntered.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        gate.TrySetResult(true);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_CancelledWrite_StillIssuesRollback()
    {
        var (context, exec, gate) = await CreateAsync();
        await using var _ = context;
        await using var container = context.CreateSqlContainer("UPDATE \"t\" SET \"v\" = 1");
        using var cts = new CancellationTokenSource();

        var write = container.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, cts.Token).AsTask();
        await CancelWhileExecutingAsync(exec, cts, gate);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        Assert.Contains("ROLLBACK", exec.ExecutedNonQueryTexts);
    }

    [Fact]
    public async Task ExecuteReaderAsync_CancelledWrite_StillIssuesRollback()
    {
        var (context, exec, gate) = await CreateAsync();
        await using var _ = context;
        await using var container = context.CreateSqlContainer("INSERT INTO \"t\" (\"v\") VALUES (1) RETURNING \"id\"");
        using var cts = new CancellationTokenSource();

        var write = container.ExecuteReaderAsync(ExecutionType.Write, CommandType.Text, cts.Token).AsTask();
        await CancelWhileExecutingAsync(exec, cts, gate);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        Assert.Contains("ROLLBACK", exec.ExecutedNonQueryTexts);
    }
}
