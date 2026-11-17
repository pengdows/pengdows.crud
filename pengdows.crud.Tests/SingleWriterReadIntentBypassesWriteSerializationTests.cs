using System;
using System.Collections.Generic;
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
/// G6 (CONCURRENCY.md): "SingleWriter serializes write *intent*, not SQL." A modifying statement
/// sent with read intent (the default -- ExecuteReaderAsync() with no ExecutionType argument)
/// takes the READER governor's slot, never the writer's single slot.
///
/// The first test below proves the real, SQL-independent danger G6 describes: two statements
/// that both carry read intent, neither of which the governor treats as a write, can run fully
/// concurrently on the reader pool -- including when their SQL text modifies data -- because
/// nothing routes them through the writer's single-slot exclusivity.
///
/// The second test is a correction surfaced while writing the first: it is NOT true that a
/// read-intent call is always free to proceed regardless of an in-progress write. SingleWriter's
/// writer-preference turnstile (G11) gates *admission of any new reader*, independent of what
/// SQL that reader will run, whenever a writer is active or waiting. So a read-intent call that
/// happens to arrive while a real write is in flight is blocked by the turnstile exactly like a
/// genuine read would be -- it only escapes serialization when no write is concurrently active.
/// G6's "is not serialized" should be read with that qualifier, which CONCURRENCY.md does not
/// currently state.
///
/// Both are provable with fakeDb: this is entirely about which PoolGovernor/turnstile a
/// connection is admitted through, not about whether the real SQLite engine would additionally
/// reject the write (that is G7b, provider-dependent, out of scope here).
/// </summary>
public sealed class SingleWriterReadIntentBypassesWriteSerializationTests
{
    [Fact(Timeout = 10000)]
    public async Task TwoWriteShapedStatements_BothSentWithReadIntent_RunFullyConcurrently_WhenNoWriterIsActive()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            // Shared-cache in-memory SQLite: multiple connections see the same data, which is
            // exactly the scenario G6 calls out -- a single physical database with separate
            // reader/writer governors routed by caller intent, not by the SQL text.
            ConnectionString = "Data Source=file:shared-memory?mode=memory&cache=shared;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            MaxConcurrentReads = 3, // sentinel + two concurrent "read-intent" callers below
            PoolAcquireTimeout = TimeSpan.FromSeconds(2),
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // SingleWriter's read-only sentinel already holds one reader permit. No writer is active
        // or waiting, so the turnstile never engages for the reads below (ShouldUseTurnstileGate
        // is false while WritersActiveOrWaiting == 0).
        Assert.Equal(1, ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);

        // Two statements that both modify data, but neither is sent with ExecutionType.Write --
        // the way code that forgot to flag intent on a write would look.
        var connA = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        connA.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(connA);
        using var scA = ctx.CreateSqlContainer("UPDATE test SET id = 1");
        var readerA = await scA.ExecuteReaderAsync(CommandType.Text);

        var connB = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        connB.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 2 } });
        factory.Connections.Add(connB);
        using var scB = ctx.CreateSqlContainer("UPDATE test SET id = 2");
        var readerB = await scB.ExecuteReaderAsync(CommandType.Text);

        // Both admitted at once, concurrently, through the reader governor -- exactly what the
        // writer's single-slot exclusivity exists to prevent between genuine writes, but does
        // not prevent here, because the governor only ever looks at execution intent.
        Assert.Equal(3, ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);
        Assert.Equal(0, ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer).InUse);

        await readerA.DisposeAsync();
        await readerB.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task ReadIntentCall_ArrivingWhileAWriterIsActive_IsBlockedByTheTurnstile_RegardlessOfItsSql()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file:shared-memory?mode=memory&cache=shared;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            // Short timeout: this test proves a *block*, not a leak, so it should fail fast
            // rather than wait out a generous production-sized timeout.
            PoolAcquireTimeout = TimeSpan.FromMilliseconds(200),
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Hold the sole write permit open with a real write in progress. The writer governor's
        // RegisterWriterTurnstileInterest marks WritersActiveOrWaiting > 0 for as long as this
        // slot is held.
        var writerConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        writerConn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(writerConn);
        using var scWrite = ctx.CreateSqlContainer("UPDATE test SET id = 1");
        var writeReader = await scWrite.ExecuteReaderAsync(ExecutionType.Write, CommandType.Text, default);
        Assert.Equal(1, ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer).InUse);

        // A second, modifying statement sent through the default (read-intent) overload -- the
        // way a caller who forgot ExecutionType.Write would send it. It still has to pass the
        // turnstile like any new reader would, and the turnstile is held by the active writer.
        var readIntentConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        readIntentConn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 2 } });
        factory.Connections.Add(readIntentConn);
        using var scReadIntent = ctx.CreateSqlContainer("UPDATE test SET id = 2");

        await Assert.ThrowsAsync<PoolSaturatedException>(
            () => scReadIntent.ExecuteReaderAsync(CommandType.Text).AsTask());

        // Once the writer releases, the same read-intent call goes through immediately: the
        // block was the turnstile gating a new reader, not a deadlock or a leaked slot.
        await writeReader.DisposeAsync();

        using var reader2 = await scReadIntent.ExecuteReaderAsync(CommandType.Text);
        Assert.NotNull(reader2);
    }
}
