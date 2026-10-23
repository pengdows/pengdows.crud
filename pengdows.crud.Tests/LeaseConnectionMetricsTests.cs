using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// NumberOfOpenConnections, PeakOpenConnections, ConnectionsOpened and ConnectionsClosed count pengdows leases
/// (a connection handed out by the context, until it is disposed), not provider StateChange events: a driver that
/// reports the wrong transitions (MySql.Data raises Open -> Open) or none cannot skew them.
/// </summary>
public sealed class LeaseConnectionMetricsTests
{
    private const string SqlServer = "Server=localhost;Database=test;EmulatedProduct=SqlServer";
    private const string LocalDb = "Server=(localdb)\\mssqllocaldb;Database=TestDb;EmulatedProduct=SqlServer";

    private static DatabaseContext Create(fakeDbFactory factory, string connectionString = SqlServer,
        DbMode mode = DbMode.Standard, ReadWriteMode readWriteMode = ReadWriteMode.ReadWrite, bool metrics = false)
    {
        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = mode,
            ReadWriteMode = readWriteMode,
            EnableMetrics = metrics
        }, factory);
    }

    [Theory]
    [InlineData(ExecutionType.Read)]
    [InlineData(ExecutionType.Write)]
    public void Lease_CountsFromAcquisitionToDisposal(ExecutionType executionType)
    {
        using var context = Create(new fakeDbFactory(SupportedDatabase.SqlServer));
        Assert.Equal(0, context.NumberOfOpenConnections);

        var connection = context.GetConnection(executionType);
        Assert.Equal(1, context.NumberOfOpenConnections);

        connection.Open();
        Assert.Equal(1, context.NumberOfOpenConnections);

        context.CloseAndDisposeConnectionInternal(connection);
        Assert.Equal(0, context.NumberOfOpenConnections);
        Assert.Equal(1, context.PeakOpenConnections);
    }

    [Theory]
    [InlineData(ExecutionType.Read)]
    [InlineData(ExecutionType.Write)]
    public async Task Lease_AsyncAcquisitionAndDisposal(ExecutionType executionType)
    {
        await using var context = Create(new fakeDbFactory(SupportedDatabase.SqlServer));

        var connection = await context.GetConnectionAsync(executionType);
        await connection.OpenAsync();
        Assert.Equal(1, context.NumberOfOpenConnections);

        await context.CloseAndDisposeConnectionAsyncInternal(connection);
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public async Task Lease_DisposedDirectly_IsReleased()
    {
        await using var context = Create(new fakeDbFactory(SupportedDatabase.SqlServer));

        var syncDisposed = context.GetConnection(ExecutionType.Read);
        var asyncDisposed = context.GetConnection(ExecutionType.Write);
        Assert.Equal(2, context.NumberOfOpenConnections);

        syncDisposed.Dispose();
        Assert.Equal(1, context.NumberOfOpenConnections);
        await asyncDisposed.DisposeAsync();
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public void ReaderAndWriterLeases_Aggregate()
    {
        using var context = Create(new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = context.GetConnection(ExecutionType.Read);
        var writer = context.GetConnection(ExecutionType.Write);

        Assert.Equal(2, context.NumberOfOpenConnections);
        Assert.Equal(1, context.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);
        Assert.Equal(1, context.GetPoolStatisticsSnapshot(PoolLabel.Writer).InUse);

        reader.Dispose();
        writer.Dispose();
        Assert.Equal(0, context.NumberOfOpenConnections);
        Assert.Equal(2, context.PeakOpenConnections);
    }

    [Fact]
    public async Task ConcurrentLeases_AreAllCounted()
    {
        using var context = Create(new fakeDbFactory(SupportedDatabase.SqlServer));

        var connections = await Task.WhenAll(
            Enumerate(10, i => Task.Run(() => context.GetConnection(i % 2 == 0 ? ExecutionType.Read : ExecutionType.Write))));
        Assert.Equal(10, context.NumberOfOpenConnections);

        await Task.WhenAll(Enumerate(10, i => Task.Run(() => connections[i].Dispose())));
        Assert.Equal(0, context.NumberOfOpenConnections);
        Assert.Equal(10, context.PeakOpenConnections);
    }

    // The peak is the most leases held at one time, not the reader peak plus the writer peak.
    [Fact]
    public void Peak_IsSimultaneous_NotTheSumOfRolePeaks()
    {
        using var context = Create(new fakeDbFactory(SupportedDatabase.SqlServer));

        context.GetConnection(ExecutionType.Read).Dispose();
        context.GetConnection(ExecutionType.Write).Dispose();

        Assert.Equal(1, context.PeakOpenConnections);
    }

    [Fact]
    public void FailedConnectionCreation_ReleasesItsLease()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var context = Create(factory);
        factory.ThrowOnCreateConnection = new InvalidOperationException("create failed");

        Assert.Throws<InvalidOperationException>(() => context.GetConnection(ExecutionType.Read));

        Assert.Equal(0, context.NumberOfOpenConnections);
        Assert.Equal(0, context.GetPoolStatisticsSnapshot(PoolLabel.Reader).InUse);
    }

    [Fact]
    public void OpenFailure_ThenDisposal_ReleasesTheLease()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var context = Create(factory);
        var failing = new fakeDbConnection { EmulatedProduct = SupportedDatabase.SqlServer };
        failing.SetFailOnOpen();
        factory.Connections.Add(failing);

        var connection = context.GetConnection(ExecutionType.Write);
        Assert.ThrowsAny<Exception>(() => connection.Open());
        Assert.Equal(1, context.NumberOfOpenConnections);

        connection.Dispose();
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public void PreventDatabaseUnload_OpenFailureAtAcquisition_ReleasesTheLease()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var context = Create(factory, LocalDb, DbMode.PreventDatabaseUnload);
        var before = context.NumberOfOpenConnections;
        var failing = new fakeDbConnection { EmulatedProduct = SupportedDatabase.SqlServer };
        failing.SetFailOnOpen();
        factory.Connections.Add(failing);

        Assert.ThrowsAny<Exception>(() => context.GetConnection(ExecutionType.Write));

        Assert.Equal(before, context.NumberOfOpenConnections);
    }

    [Fact]
    public void DisposingALeaseTwice_DoesNotGoNegative()
    {
        using var context = Create(new fakeDbFactory(SupportedDatabase.SqlServer));

        var connection = context.GetConnection(ExecutionType.Read);
        connection.Dispose();
        connection.Dispose();
        context.CloseAndDisposeConnectionInternal(connection);

        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Theory]
    [InlineData(ReadWriteMode.ReadWrite)]
    [InlineData(ReadWriteMode.ReadOnly)]
    public void PreventDatabaseUnload_EachSentinelIsOneLease(ReadWriteMode readWriteMode)
    {
        // A read-write context keeps a sentinel per pool (its reader pool is separate), a read-only one just one.
        var context = Create(new fakeDbFactory(SupportedDatabase.SqlServer), LocalDb, DbMode.PreventDatabaseUnload, readWriteMode);
        Assert.Equal(DbMode.PreventDatabaseUnload, context.ConnectionMode);
        var sentinels = context.GetSentinelSnapshot().Count;
        Assert.Equal(readWriteMode == ReadWriteMode.ReadWrite ? 2 : 1, sentinels);
        Assert.Equal(sentinels, context.NumberOfOpenConnections);

        var work = context.GetConnection(ExecutionType.Read);
        Assert.Equal(sentinels + 1, context.NumberOfOpenConnections);
        context.CloseAndDisposeConnectionInternal(work);
        Assert.Equal(sentinels, context.NumberOfOpenConnections);
        Assert.Equal(sentinels + 1, context.PeakOpenConnections);

        context.Dispose();
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public void PreventDatabaseUnload_ReaderSentinelWithNoReaderGovernorSlot_IsCountedAsAReaderLease()
    {
        // The reader pool is forbidden (no governor slot to hand out), so its sentinel is a tracking-only lease. It
        // belongs to the Reader role, not to the context's own (Writer) role.
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = LocalDb,
            ReadOnlyConnectionString = LocalDb + ";ApplicationIntent=ReadOnly",
            DbMode = DbMode.PreventDatabaseUnload,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            MaxConcurrentReads = 0,
            EnableMetrics = true
        }, new fakeDbFactory(SupportedDatabase.SqlServer));

        var sentinels = context.GetSentinelSnapshot();
        Assert.Contains(sentinels, s => s.ExecutionType == ExecutionType.Read);
        Assert.Equal(sentinels.Count, context.NumberOfOpenConnections);
        Assert.Equal(1, context.Metrics.Read.ConnectionsCurrent);
        Assert.Equal(1, context.Metrics.Write.ConnectionsCurrent);

        context.Dispose();
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public void SingleConnection_SharedConnectionIsOneLease()
    {
        var context = Create(new fakeDbFactory(SupportedDatabase.Sqlite), "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode.SingleConnection);
        Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);
        Assert.Equal(1, context.NumberOfOpenConnections);

        var first = context.GetConnection(ExecutionType.Write);
        var second = context.GetConnection(ExecutionType.Read);
        Assert.Equal(1, context.NumberOfOpenConnections);
        context.CloseAndDisposeConnectionInternal(first);
        context.CloseAndDisposeConnectionInternal(second);
        Assert.Equal(1, context.NumberOfOpenConnections);
        Assert.Equal(1, context.PeakOpenConnections);

        context.Dispose();
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public void InitializationProbe_IsNotCounted()
    {
        using var context = Create(new fakeDbFactory(SupportedDatabase.SqlServer));

        Assert.Equal(0, context.NumberOfOpenConnections);
        Assert.Equal(0, context.PeakOpenConnections);
    }

    // A driver that raises no StateChange, or raises extra ones, changes nothing.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Counts_DoNotDependOnProviderStateChange(bool spurious)
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var context = Create(factory, metrics: true);
        var connection = new MisreportingConnection(spurious) { EmulatedProduct = SupportedDatabase.SqlServer };
        factory.Connections.Add(connection);

        var lease = context.GetConnection(ExecutionType.Write);
        lease.Open();
        lease.Close();
        lease.Open();
        Assert.Equal(1, context.NumberOfOpenConnections);

        context.CloseAndDisposeConnectionInternal(lease);
        Assert.Equal(0, context.NumberOfOpenConnections);
        Assert.Equal(1, context.PeakOpenConnections);
        Assert.Equal(1, context.Metrics.ConnectionsOpened);
        Assert.Equal(1, context.Metrics.ConnectionsClosed);
    }

    [Fact]
    public void Metrics_CountLeasesAcquiredAndReleased()
    {
        using var context = Create(new fakeDbFactory(SupportedDatabase.SqlServer), metrics: true);

        var reader = context.GetConnection(ExecutionType.Read);
        var writer = context.GetConnection(ExecutionType.Write);
        var held = context.Metrics;
        reader.Dispose();
        var afterOne = context.Metrics;
        writer.Dispose();
        var afterBoth = context.Metrics;

        Assert.Equal(2, held.ConnectionsCurrent);
        Assert.Equal(2, held.ConnectionsOpened);
        Assert.Equal(0, held.ConnectionsClosed);
        Assert.Equal(1, held.Read.ConnectionsCurrent);
        Assert.Equal(1, held.Write.ConnectionsCurrent);
        Assert.Equal(1, afterOne.ConnectionsCurrent);
        Assert.Equal(1, afterOne.ConnectionsClosed);
        Assert.Equal(0, afterBoth.ConnectionsCurrent);
        Assert.Equal(2, afterBoth.ConnectionsOpened);
        Assert.Equal(2, afterBoth.ConnectionsClosed);
        Assert.Equal(2, afterBoth.PeakOpenConnections);
        Assert.Equal(1, afterBoth.Read.ConnectionsOpened);
        Assert.Equal(1, afterBoth.Write.ConnectionsClosed);
    }

    [Fact]
    public void Metrics_UnopenedLeaseIsStillALease()
    {
        using var context = Create(new fakeDbFactory(SupportedDatabase.SqlServer), metrics: true);

        context.GetConnection(ExecutionType.Read).Dispose();

        Assert.Equal(1, context.Metrics.ConnectionsOpened);
        Assert.Equal(1, context.Metrics.ConnectionsClosed);
    }

    private static Task<T>[] Enumerate<T>(int count, Func<int, Task<T>> create)
    {
        var tasks = new Task<T>[count];
        for (var i = 0; i < count; i++)
        {
            tasks[i] = create(i);
        }

        return tasks;
    }

    private static Task[] Enumerate(int count, Func<int, Task> create)
    {
        var tasks = new Task[count];
        for (var i = 0; i < count; i++)
        {
            tasks[i] = create(i);
        }

        return tasks;
    }

    // Raises no StateChange (spurious: false), or two Closed -> Open events per transition (spurious: true).
    private sealed class MisreportingConnection(bool spurious) : fakeDbConnection
    {
        protected override void OnStateChange(StateChangeEventArgs stateChange)
        {
            if (!spurious)
            {
                return;
            }

            base.OnStateChange(new StateChangeEventArgs(ConnectionState.Closed, ConnectionState.Open));
            base.OnStateChange(new StateChangeEventArgs(ConnectionState.Closed, ConnectionState.Open));
        }
    }
}
