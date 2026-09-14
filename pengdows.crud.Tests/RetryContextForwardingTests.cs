using System;
using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// RetryContext implements IDatabaseContext almost entirely via pure forwarding to the wrapped
/// parent context (see RetryContext.cs's own "Everything below is pure forwarding" comment) —
/// this locks each forwarded member down against the same underlying DatabaseContext, plus the
/// deliberate NotSupportedException on every BeginTransaction* overload (RetryContext owns its
/// own transaction lifecycle internally, see the design doc's shortcoming #3).
/// </summary>
public class RetryContextForwardingTests
{
    private static DatabaseContext CreateContext(fakeDbFactory factory)
    {
        return new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
    }

    [Fact]
    public async Task SimpleProperties_ForwardToInnerContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        Assert.Equal(ctx.ConnectionMode, rc.ConnectionMode);
        Assert.Equal(ctx.RootId, rc.RootId);
        Assert.Equal(ctx.ReadWriteMode, rc.ReadWriteMode);
        Assert.Equal(ctx.ConnectionString, rc.ConnectionString);
        Assert.Equal(ctx.Name, rc.Name);
        Assert.Same(ctx.DataSourceInfo, rc.DataSourceInfo);
        Assert.Equal(ctx.ModeLockTimeout, rc.ModeLockTimeout);
        Assert.Equal(ctx.ProcWrappingStyle, rc.ProcWrappingStyle);
        Assert.Equal(ctx.MaxParameterLimit, rc.MaxParameterLimit);
        Assert.Equal(ctx.MaxOutputParameters, rc.MaxOutputParameters);
        Assert.Equal(ctx.NumberOfOpenConnections, rc.NumberOfOpenConnections);
        Assert.Equal(ctx.Metrics, rc.Metrics);
        Assert.Same(ctx.Dialect, rc.Dialect);
        Assert.Equal(ctx.Product, rc.Product);
        Assert.Equal(ctx.PeakOpenConnections, rc.PeakOpenConnections);
        Assert.Equal(ctx.ReaderPlanCacheSize, rc.ReaderPlanCacheSize);
        Assert.Equal(ctx.PrepareMode, rc.PrepareMode);
        Assert.Equal(ctx.SupportsInsertReturning, rc.SupportsInsertReturning);
        Assert.Equal(ctx.QuotePrefix, rc.QuotePrefix);
        Assert.Equal(ctx.QuoteSuffix, rc.QuoteSuffix);
        Assert.Equal(ctx.CompositeIdentifierSeparator, rc.CompositeIdentifierSeparator);
        Assert.Equal(ctx.IsReadOnlyConnection, rc.IsReadOnlyConnection);
        Assert.Equal(ctx.RCSIEnabled, rc.RCSIEnabled);
        Assert.Equal(ctx.SnapshotIsolationEnabled, rc.SnapshotIsolationEnabled);
        Assert.Equal(ctx.GetBaseSessionSettings(), rc.GetBaseSessionSettings());
        Assert.Equal(ctx.GetReadOnlySessionSettings(), rc.GetReadOnlySessionSettings());
        Assert.Equal(ctx.GetSupportedIsolationLevels(), rc.GetSupportedIsolationLevels());
        Assert.Equal(ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer), rc.GetPoolStatisticsSnapshot(PoolLabel.Writer));
    }

    [Fact]
    public async Task NameGenerationAndQuotingMethods_ForwardToInnerContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        Assert.Equal(ctx.WrapObjectName("customers"), rc.WrapObjectName("customers"));
        Assert.Equal(ctx.MakeParameterName("p0"), rc.MakeParameterName("p0"));

        using var sc = ctx.CreateSqlContainer();
        var p = sc.AddParameterWithValue("p1", DbType.Int32, 1);
        Assert.Equal(ctx.MakeParameterName(p), rc.MakeParameterName(p));

        // GenerateRandomName/GenerateParameterName produce fresh random text each call — assert
        // shape (non-empty, forwarded from the same context) rather than exact equality.
        Assert.False(string.IsNullOrEmpty(rc.GenerateParameterName()));
        Assert.False(string.IsNullOrEmpty(rc.GenerateRandomName()));
    }

    [Fact]
    public async Task CreateDbParameter_Overloads_ForwardToInnerContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        var p1 = rc.CreateDbParameter("p1", DbType.Int32, 42);
        Assert.Equal(DbType.Int32, p1.DbType);
        Assert.Equal(42, p1.Value);

        var p2 = rc.CreateDbParameter("p2", DbType.String, "hi", ParameterDirection.Output);
        Assert.Equal(ParameterDirection.Output, p2.Direction);

        var p3 = rc.CreateDbParameter(DbType.Boolean, true);
        Assert.Equal(DbType.Boolean, p3.DbType);
    }

    [Fact]
    public async Task MetricsUpdated_AddAndRemove_ForwardToInnerContextsBackingField()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        var field = typeof(DatabaseContext).GetField("_metricsUpdated", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);

        EventHandler<DatabaseMetrics> handler = (_, _) => { };

        rc.MetricsUpdated += handler;
        var afterAdd = (MulticastDelegate?)field!.GetValue(ctx);
        Assert.NotNull(afterAdd);
        Assert.True(Array.IndexOf(afterAdd!.GetInvocationList(), handler) >= 0);

        rc.MetricsUpdated -= handler;
        var afterRemove = (MulticastDelegate?)field.GetValue(ctx);
        Assert.True(afterRemove == null || Array.IndexOf(afterRemove.GetInvocationList(), handler) < 0);
    }

    [Fact]
    public async Task BeginTransaction_IsolationLevelOverload_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        Assert.Throws<NotSupportedException>(() => rc.BeginTransaction());
    }

    [Fact]
    public async Task BeginTransaction_IsolationProfileOverload_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        Assert.Throws<NotSupportedException>(() => rc.BeginTransaction(IsolationProfile.SafeNonBlockingReads));
    }

    [Fact]
    public async Task BeginTransactionAsync_IsolationLevelOverload_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        await Assert.ThrowsAsync<NotSupportedException>(() => rc.BeginTransactionAsync().AsTask());
    }

    [Fact]
    public async Task BeginTransactionAsync_IsolationProfileOverload_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => rc.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads).AsTask());
    }
}
