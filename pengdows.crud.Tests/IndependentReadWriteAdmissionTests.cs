using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// G9a (CONCURRENCY.md): saturating the read side must never delay or block write admission in
/// Standard or PreventDatabaseUnload. The reader and writer governors are separate PoolGovernor
/// instances; in these modes there is no shared turnstile, so PoolGovernor.ShouldUseTurnstileGate
/// returns false and a write's acquire path never references the reader governor, its gate, or
/// its queue. A write that finds a free write permit must therefore be admitted immediately no
/// matter how saturated the read side is.
///
/// These are falsification tests for G9a, written against fakeDb so they are deterministic and
/// need no Docker/real provider. If write admission is ever made to wait on read-side state,
/// the timing assertion below is the one that should start failing.
/// </summary>
public sealed class IndependentReadWriteAdmissionTests
{
    [Fact(Timeout = 10000)]
    public async Task Standard_SaturatedReadersWithFullQueue_DoNotDelayAFreeWrite()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 1,
            MaxQueuedReads = 1,
            MaxConcurrentWrites = 1,
            PoolAcquireTimeout = TimeSpan.FromSeconds(2),
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Hold the only read permit open.
        var conn1 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn1.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn1);
        using var sc1 = ctx.CreateSqlContainer("SELECT id FROM test");
        var reader1 = await sc1.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, default);

        // Queue a second read: no free read permit, but one queue slot is configured, so this
        // waits instead of being rejected.
        var conn2 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn2.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 2 } });
        factory.Connections.Add(conn2);
        using var sc2 = ctx.CreateSqlContainer("SELECT id FROM test");
        var queuedReadTask = sc2.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, default).AsTask();

        await WaitUntilAsync(() => ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader).Queued == 1);

        // A third read now finds the only permit held AND the one queue slot full: it must be
        // rejected synchronously. This is the "read queue saturated" half of G9a's premise.
        using var sc3 = ctx.CreateSqlContainer("SELECT id FROM test");
        await Assert.ThrowsAsync<PoolSaturatedException>(
            () => sc3.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, default).AsTask());

        // The write side has a free permit. G9a says it must be admitted immediately -- it never
        // touches the reader governor at all.
        var conn4 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn4.EnqueueNonQueryResult(1);
        factory.Connections.Add(conn4);
        using var sc4 = ctx.CreateSqlContainer("UPDATE test SET id = 4");

        var stopwatch = Stopwatch.StartNew();
        var affected = await sc4.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, default);
        stopwatch.Stop();

        Assert.Equal(1, affected);
        // Generous relative to the 2s PoolAcquireTimeout: if the write ever had to wait on the
        // read governor, this would be at least an order of magnitude slower, not flaky-close.
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"Write admission took {stopwatch.ElapsedMilliseconds} ms while the read side was " +
            "saturated; it should never have touched the reader governor.");

        // The queued read is exactly where it was -- the write did not disturb it or jump ahead
        // of it on some shared structure.
        Assert.False(queuedReadTask.IsCompleted);

        // Releasing the held read permit lets the queued read complete, proving it really was
        // alive in the governor's queue the whole time, not silently abandoned.
        await reader1.DisposeAsync();
        using var reader2 = await queuedReadTask;
        Assert.NotNull(reader2);
    }

    [Fact(Timeout = 10000)]
    public async Task PreventDatabaseUnload_SaturatedReadPermitsDoNotDelayAFreeWrite()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=db;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload,
            MaxConcurrentReads = 2, // one sentinel + one real read permit
            MaxConcurrentWrites = 2, // one sentinel + one real write permit
            PoolAcquireTimeout = TimeSpan.FromSeconds(2),
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // The sentinel already holds one reader permit before this test touches anything.
        Assert.Equal(1, ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);

        var conn1 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.SqlServer };
        conn1.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn1);
        using var sc1 = ctx.CreateSqlContainer("SELECT id FROM test");
        var reader1 = await sc1.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, default);

        // Both read permits (sentinel + this one) are now held.
        Assert.Equal(2, ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);

        var conn2 = new fakeDbConnection { EmulatedProduct = SupportedDatabase.SqlServer };
        conn2.EnqueueNonQueryResult(1);
        factory.Connections.Add(conn2);
        using var sc2 = ctx.CreateSqlContainer("UPDATE test SET id = 2");

        var stopwatch = Stopwatch.StartNew();
        var affected = await sc2.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, default);
        stopwatch.Stop();

        Assert.Equal(1, affected);
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"Write admission took {stopwatch.ElapsedMilliseconds} ms under PreventDatabaseUnload " +
            "while every read permit (including the sentinel) was held.");

        await reader1.DisposeAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected contention state was not reached before the deadline.");
    }
}
