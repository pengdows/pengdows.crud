using System;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.@internal;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Review 2026-09-29 raised a possible race: the strategy disposes its async repair semaphore, so an
/// async sentinel repair in flight when the context is disposed might throw ObjectDisposedException
/// from its finally Release(). It cannot: DatabaseContext never disposes the strategy during its own
/// disposal. This pins that a context disposed mid-repair lets the repair finish cleanly. The
/// injection hook is now per strategy instance, not a static shared by every instance.
/// </summary>
public sealed class SentinelRepairDisposalRaceTests
{
    private static PreventDatabaseUnloadConnectionStrategy Strategy(DatabaseContext context) =>
        (PreventDatabaseUnloadConnectionStrategy)typeof(DatabaseContext)
            .GetField("_connectionStrategy", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(context)!;

    [Fact]
    public async Task AsyncRepair_ContextDisposedMidRepair_DoesNotThrowFromTheRepairLock()
    {
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=localhost;Database=/data/test.fdb;EmulatedProduct=Firebird",
            DbMode = DbMode.Best
        }, new fakeDbFactory(SupportedDatabase.Firebird));
        var strategy = Strategy(context);
        var sentinel = context.GetSentinelSnapshot()[0].Connection;
        ((fakeDbConnection)((IInternalConnectionWrapper)sentinel).UnderlyingConnection).BreakConnection();
        strategy.PostDisposedCheckHook = () =>
        {
            strategy.PostDisposedCheckHook = null;
            context.Dispose();
        };

        var ex = await Record.ExceptionAsync(async () =>
            await strategy.RestoreSentinelsAfterDdlAsync(default));

        Assert.Null(strategy.PostDisposedCheckHook); // the hook ran: the context was disposed mid-repair
        Assert.True(context.IsDisposed);
        Assert.Null(ex);
    }
}
