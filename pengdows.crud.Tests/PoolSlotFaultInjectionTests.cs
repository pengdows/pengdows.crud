using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// G4 (CONCURRENCY.md): "Permits, checkouts, tracked readers and transaction contexts are
/// released exactly once on every exit path: success, exception, cancellation and dispose."
/// The doc explicitly lists fault injection at open, execute and dispose as a pending
/// falsification test -- this is that test.
///
/// Each test deliberately picks the smallest pool possible (MaxConcurrentReads/Writes = 1) so a
/// leaked slot is not a subtle metrics discrepancy -- it is a pool that can never admit another
/// caller again. Proving "no leak" means proving the pool is still usable afterward, not just
/// reading a counter back to zero.
/// </summary>
public sealed class PoolSlotFaultInjectionTests
{
    [Fact(Timeout = 10000)]
    public async Task OpenFailure_DoesNotLeakThePoolSlot()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 1,
            PoolAcquireTimeout = TimeSpan.FromSeconds(2),
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var failingConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        failingConn.SetFailOnOpen(true);
        factory.Connections.Add(failingConn);

        using var sc1 = ctx.CreateSqlContainer("SELECT id FROM test");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sc1.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, default).AsTask());

        // The permit must already be back with the governor -- nothing is still "in use" from
        // the failed attempt.
        Assert.Equal(0, ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);

        // The real proof a one-slot pool didn't leak: a second, healthy attempt must succeed
        // immediately. If the failed open had kept the permit, this would hang until
        // PoolAcquireTimeout and then throw PoolSaturatedException instead.
        var healthyConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        healthyConn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(healthyConn);

        using var sc2 = ctx.CreateSqlContainer("SELECT id FROM test");
        using var reader = await sc2.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, default);
        Assert.NotNull(reader);
    }

    [Fact(Timeout = 10000)]
    public async Task CommandCreationFailure_DuringExecute_DoesNotLeakThePoolSlot()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            MaxConcurrentWrites = 1,
            PoolAcquireTimeout = TimeSpan.FromSeconds(2),
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Open succeeds; the connection only fails once asked to create the command that
        // ExecuteNonQueryAsync needs -- a failure strictly after the slot is already attached to
        // a live TrackedConnection, not during acquisition itself.
        var failingConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        failingConn.SetFailOnCommand(true);
        factory.Connections.Add(failingConn);

        using var sc1 = ctx.CreateSqlContainer("UPDATE test SET id = 1");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sc1.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, default).AsTask());

        Assert.Equal(0, ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer).InUse);

        var healthyConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        healthyConn.EnqueueNonQueryResult(1);
        factory.Connections.Add(healthyConn);

        using var sc2 = ctx.CreateSqlContainer("UPDATE test SET id = 2");
        var affected = await sc2.ExecuteNonQueryAsync(ExecutionType.Write, CommandType.Text, default);
        Assert.Equal(1, affected);
    }

    [Fact(Timeout = 10000)]
    public async Task DisposeFailure_StillReleasesThePoolSlot_AndTheExceptionDoesNotPropagate()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 1,
            PoolAcquireTimeout = TimeSpan.FromSeconds(2),
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var sc1 = ctx.CreateSqlContainer("SELECT id FROM test");
        var reader = await sc1.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, default);

        // The connection's own cleanup fails *after* it has actually closed/disposed -- exactly
        // what Q1/G4 describe: "released after the physical dispose ... even if dispose throws."
        conn.SetFailOnDispose(new InvalidOperationException("provider cleanup failed"));

        // TrackedConnection.DisposeConnectionAsyncCore logs and swallows a failing physical
        // dispose rather than propagating it (its own ReleaseSlot() call is unconditional, in a
        // finally) -- so this must complete normally, not throw.
        await reader.DisposeAsync();

        Assert.Equal(0, ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);

        // And the single slot is genuinely usable again, not stranded behind the swallowed
        // failure.
        var healthyConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        healthyConn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 2 } });
        factory.Connections.Add(healthyConn);

        using var sc2 = ctx.CreateSqlContainer("SELECT id FROM test");
        using var reader2 = await sc2.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, default);
        Assert.NotNull(reader2);
    }
}
