using System;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// pengdows.crud holds no connection between operations (a transaction holds one for its own lifetime), so a connection
// that dies costs the operation using it and nothing after it. This is one code path for every database (the
// lease, the governor permit and the session script do not depend on the product), so each test runs against every
// supported database. What a database changes is the connection mode its connection string selects: where the
// writer permit exists (a SingleWriter or Standard context), a connection that broke mid-transaction must not leave it
// taken, or the next write would wait for it until the acquire timeout.
public class BrokenConnectionRecoveryTests
{
    public static TheoryData<SupportedDatabase> Databases()
    {
        var data = new TheoryData<SupportedDatabase>();
        foreach (var database in Enum.GetValues<SupportedDatabase>().Where(d => d != SupportedDatabase.Unknown))
        {
            data.Add(database);
        }

        return data;
    }

    private static (DatabaseContext Context, fakeDbFactory Factory) NewContext(SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var context = new DatabaseContext($"Data Source=recovery.db;EmulatedProduct={database}", factory);
        return (context, factory);
    }

    // SingleConnection (one pinned connection for everything) has no per-operation lease to recover with.
    private static bool HasEphemeralConnections(DatabaseContext context) =>
        context.ConnectionMode != DbMode.SingleConnection;

    // The permits still held once every operation has finished. PreventDatabaseUnload keeps one sentinel connection per
    // pool, and the sentinel holds one permit for as long as it lives (docs/connection/connection-modes.md); in every
    // other mode nothing is held between operations.
    private static int PermitsHeldBetweenOperations(DatabaseContext context) =>
        context.ConnectionMode == DbMode.PreventDatabaseUnload ? 1 : 0;

    private static async Task<int> WriteAsync(IDatabaseContext context, string sql)
    {
        await using var container = context.CreateSqlContainer(sql);
        return await container.ExecuteNonQueryAsync();
    }

    private static void BreakEveryConnection(fakeDbFactory factory)
    {
        foreach (var connection in factory.CreatedConnections.ToArray())
        {
            connection.BreakConnection();
        }
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public async Task AWriteTransactionWhoseConnectionBreaks_FailsAlone_AndTheNextWriteSucceeds(SupportedDatabase database)
    {
        var (context, factory) = NewContext(database);
        using var _ = context;
        if (!HasEphemeralConnections(context))
        {
            return; // asserted for the other modes below; a SingleConnection context pins its one connection by design
        }

        await using (var transaction = context.BeginTransaction())
        {
            await WriteAsync(transaction, "UPDATE t SET v = 1 WHERE id = 1");
            BreakEveryConnection(factory);
            await Assert.ThrowsAnyAsync<Exception>(() => WriteAsync(transaction, "UPDATE t SET v = 2 WHERE id = 2"));
        }

        // The writer permit went back with the transaction. A leaked permit would make this wait out the acquire
        // timeout (five seconds by default) and then throw PoolSaturatedException.
        var next = WriteAsync(context, "UPDATE t SET v = 3 WHERE id = 3");
        Assert.Same(next, await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(2))));
        await next;

        var writer = context.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.Equal(PermitsHeldBetweenOperations(context), writer.InUse);
        Assert.Equal(0, writer.Queued);
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public async Task EveryConnectionBreakingBetweenWrites_LosesNoWrites(SupportedDatabase database)
    {
        var (context, factory) = NewContext(database);
        using var _ = context;
        if (!HasEphemeralConnections(context))
        {
            return;
        }

        for (var i = 0; i < 5; i++)
        {
            await WriteAsync(context, $"UPDATE t SET v = {i} WHERE id = 1");
            BreakEveryConnection(factory); // nothing is held between writes for this to hit
        }

        Assert.Equal(PermitsHeldBetweenOperations(context), context.GetPoolStatisticsSnapshot(PoolLabel.Writer).InUse);
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public async Task ABreakDuringOneTransaction_DoesNotTouchAnotherContext(SupportedDatabase database)
    {
        var (first, firstFactory) = NewContext(database);
        var (second, _) = NewContext(database);
        using var __ = first;
        using var ___ = second;
        if (!HasEphemeralConnections(first))
        {
            return;
        }

        await using (var transaction = first.BeginTransaction())
        {
            BreakEveryConnection(firstFactory);
            await Assert.ThrowsAnyAsync<Exception>(() => WriteAsync(transaction, "UPDATE t SET v = 1 WHERE id = 1"));
        }

        Assert.True(await WriteAsync(second, "UPDATE t SET v = 2 WHERE id = 2") >= 0);
    }
}
