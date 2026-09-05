#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.@internal;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// Contract test: no <see cref="IConnectionStrategy"/> implementation may reach a synchronous
/// <c>Open</c>/<c>Acquire</c> call from its async connection-acquisition path.
/// </summary>
/// <remarks>
/// This exists because the same bug shape reappeared once already:
/// <c>PreventDatabaseUnloadConnectionStrategy.GetConnectionAsync</c> was fixed to call
/// <c>OpenAsync</c> instead of the blocking <c>Open()</c>, but the fix was applied per-class, and
/// the 2.1.0 branch independently regressed back to the sync call in the same method. A per-class
/// regression test (see <c>PreventDatabaseUnloadConnectionStrategyBehaviorTests.GetConnectionAsync_OpensConnectionAsynchronously_NotSynchronously</c>)
/// only protects the one class it was written against; it says nothing about a future rename or a
/// brand-new <see cref="IConnectionStrategy"/> implementation.
///
/// This test instead reflects over the shipping assembly for every concrete
/// <see cref="IConnectionStrategy"/> implementation and requires an explicit fixture entry for
/// each one. Adding a new strategy without adding a fixture here fails the test loudly (a missing
/// mapping, not a silently-skipped case) -- forcing whoever adds it to either prove the async path
/// is genuinely non-blocking or explicitly justify why not.
/// </remarks>
public class ConnectionStrategyAsyncOpenContractTests
{
    /// <summary>
    /// Fixture for one concrete <see cref="IConnectionStrategy"/> implementation.
    /// </summary>
    /// <param name="Mode">DbMode that constructs a DatabaseContext compatible with this strategy.</param>
    /// <param name="ConnectionString">A connection string valid for that mode/database pairing.</param>
    /// <param name="Database">Emulated product for the fakeDb factory.</param>
    /// <param name="Construct">Builds the strategy instance directly (bypassing ConnectionStrategyFactory,
    /// matching how the existing per-class regression tests already construct it).</param>
    /// <param name="ReturnsFreshConnection">
    /// True when GetConnectionAsync hands back a brand-new connection object every call (so it must
    /// never have been synchronously Open()'d before -- OpenCount must be exactly 0). False when the
    /// strategy instead returns an already-established persistent connection (SingleConnectionStrategy):
    /// that connection may legitimately have been opened synchronously during DatabaseContext's own
    /// (inherently synchronous) constructor -- a separate, already-accepted concern -- so the contract
    /// there is narrower: GetConnectionAsync itself must not add a further Open() call.
    /// </param>
    private sealed record StrategyFixture(
        DbMode Mode,
        string ConnectionString,
        SupportedDatabase Database,
        Func<DatabaseContext, IConnectionStrategy> Construct,
        bool ReturnsFreshConnection);

    private static readonly Dictionary<Type, StrategyFixture> Fixtures = new()
    {
        [typeof(StandardConnectionStrategy)] = new StrategyFixture(
            DbMode.Standard,
            "Data Source=contract-standard;EmulatedProduct=SqlServer",
            SupportedDatabase.SqlServer,
            ctx => new StandardConnectionStrategy(ctx),
            ReturnsFreshConnection: true),

        [typeof(PreventDatabaseUnloadConnectionStrategy)] = new StrategyFixture(
            DbMode.PreventDatabaseUnload,
            "Data Source=contract-pdu;EmulatedProduct=SqlServer",
            SupportedDatabase.SqlServer,
            ctx => new PreventDatabaseUnloadConnectionStrategy(ctx),
            ReturnsFreshConnection: true),

        [typeof(SingleConnectionStrategy)] = new StrategyFixture(
            DbMode.SingleConnection,
            "Data Source=:memory:;EmulatedProduct=Sqlite",
            SupportedDatabase.Sqlite,
            ctx => new SingleConnectionStrategy(ctx),
            ReturnsFreshConnection: false),
    };

    public static IEnumerable<object[]> AllConcreteConnectionStrategyTypes()
    {
        return typeof(IConnectionStrategy).Assembly
            .GetTypes()
            .Where(t => typeof(IConnectionStrategy).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => new object[] { t });
    }

    [Theory]
    [MemberData(nameof(AllConcreteConnectionStrategyTypes))]
    public async Task GetConnectionAsync_NeverCallsSynchronousOpen(Type strategyType)
    {
        Assert.True(Fixtures.TryGetValue(strategyType, out var fixture),
            $"No {nameof(StrategyFixture)} registered for '{strategyType.FullName}'. " +
            $"Every concrete {nameof(IConnectionStrategy)} implementation must be covered by this " +
            "contract test -- add a fixture entry (and prove its GetConnectionAsync override never " +
            "reaches a synchronous Open/Acquire) rather than leaving it unverified.");

        var factory = new fakeDbFactory(fixture!.Database);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = fixture.ConnectionString,
            DbMode = fixture.Mode,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var ctx = new DatabaseContext(cfg, factory);
        var strategy = fixture.Construct(ctx);

        var baselineOpenCount = 0;
        if (!fixture.ReturnsFreshConnection &&
            ctx.PersistentConnection is IInternalConnectionWrapper persistentWrapper &&
            persistentWrapper.UnderlyingConnection is fakeDbConnection persistentFake)
        {
            baselineOpenCount = persistentFake.OpenCount;
        }

        var conn = await strategy.GetConnectionAsync(ExecutionType.Read, false);
        try
        {
            var underlying = (fakeDbConnection)((IInternalConnectionWrapper)conn).UnderlyingConnection;

            if (fixture.ReturnsFreshConnection)
            {
                // A brand-new connection object every call -- it must never have gone through the
                // synchronous Open() path. Whether it's opened eagerly here (PreventDatabaseUnload,
                // which deliberately fails fast) or left unopened for a later "open late" step
                // (Standard) is a separate, legitimate implementation choice this contract doesn't
                // constrain -- it only constrains *which* Open overload gets used when opening does
                // happen.
                Assert.Equal(0, underlying.OpenCount);
            }
            else
            {
                // An already-established persistent connection -- GetConnectionAsync must not add
                // a further synchronous Open() call on top of whatever construction already did.
                Assert.Equal(baselineOpenCount, underlying.OpenCount);
            }
        }
        finally
        {
            await strategy.ReleaseConnectionAsync(conn);
        }
    }
}
