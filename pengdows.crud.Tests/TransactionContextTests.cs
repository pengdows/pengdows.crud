#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.connection;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using pengdows.crud;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TransactionContextTests
{
    private IDatabaseContext CreateContext(SupportedDatabase supportedDatabase)
    {
        var factory = new FakeDbFactory(supportedDatabase);
        var config = new DatabaseContextConfiguration
        {
            DbMode = DbMode.SingleWriter,
            ProviderName = supportedDatabase.ToString(),
            ConnectionString = $"Data Source=test;EmulatedProduct={supportedDatabase}"
        };

        return new DatabaseContext(config, factory);
    }

    [Theory]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.Sqlite)]
    public void Constructor_SetsIsolationLevel_Correctly(SupportedDatabase supportedDatabase)
    {
        var tx = CreateContext(supportedDatabase).BeginTransaction(IsolationLevel.ReadUncommitted);
        if (tx.IsolationLevel < IsolationLevel.Chaos)
        {
            Console.WriteLine($"{supportedDatabase}: {nameof(tx.IsolationLevel)}: {tx.IsolationLevel}");
        }

        Assert.True(IsolationLevel.Chaos < tx.IsolationLevel); // upgraded due to ReadWrite
    }

    [Fact]
    public void Commit_SetsCommittedState()
    {
        var tx = CreateContext(SupportedDatabase.Sqlite).BeginTransaction();
        tx.Commit();

        Assert.True(tx.WasCommitted);
        Assert.True(tx.IsCompleted);
    }

    [Fact]
    public void Rollback_SetsRollbackState()
    {
        var tx = CreateContext(SupportedDatabase.Sqlite).BeginTransaction();
        tx.Rollback();

        Assert.True(tx.WasRolledBack);
        Assert.True(tx.IsCompleted);
    }

    [Fact]
    public void Commit_AfterDispose_Throws()
    {
        var tx = CreateContext(SupportedDatabase.Sqlite).BeginTransaction();
        tx.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tx.Commit());
    }

    [Fact]
    public async Task DisposeAsync_Uncommitted_TriggersRollback()
    {
        var tx = CreateContext(SupportedDatabase.Sqlite).BeginTransaction();
        await tx.DisposeAsync();

        Assert.True(tx.IsCompleted);
    }

    public static IEnumerable<object[]> AllSupportedProviders()
    {
        return Enum.GetValues<SupportedDatabase>()
            .Where(p => p != SupportedDatabase.Unknown)
            .Select(p => new object[] { p });
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void Commit_MarksAsCommitted(SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new FakeDbFactory(product.ToString()));
        var tx = context.BeginTransaction();
        tx.Commit();

        Assert.True(tx.WasCommitted);
        Assert.True(tx.IsCompleted);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void Rollback_MarksAsRolledBack(SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new FakeDbFactory(product.ToString()));
        using var tx = context.BeginTransaction();

        tx.Rollback();

        Assert.True(tx.WasRolledBack);
        Assert.True(tx.IsCompleted);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public async Task DisposeAsync_RollsBackUncommittedTransaction(SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new FakeDbFactory(product.ToString()));
        await using var tx = context.BeginTransaction();

        await tx.DisposeAsync();

        Assert.True(tx.IsCompleted);
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void CreateSqlContainer_AfterCompletion_Throws(SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new FakeDbFactory(product.ToString()));
        using var tx = context.BeginTransaction();

        tx.Rollback();

        Assert.Throws<InvalidOperationException>(() => tx.CreateSqlContainer("SELECT 1"));
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void GenerateRandomName_StartsWithLetter(SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new FakeDbFactory(product.ToString()));
        using var tx = context.BeginTransaction();
        var name = tx.GenerateRandomName(10);

        Assert.True(char.IsLetter(name[0]));
    }

    [Theory]
    [MemberData(nameof(AllSupportedProviders))]
    public void NestedTransactionsFail(SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", new FakeDbFactory(product.ToString()));
        using var tx = context.BeginTransaction();
        var name = tx.GenerateRandomName(10);

        Assert.Throws<InvalidOperationException>(() => ((IDatabaseContext)tx).BeginTransaction(null));
        Assert.True(char.IsLetter(name[0]));
    }

    [Fact]
    public void BeginTransaction_WithIsolationProfile_Throws()
    {
        using var tx = CreateContext(SupportedDatabase.Sqlite).BeginTransaction();
        Assert.Throws<InvalidOperationException>(() => ((IDatabaseContext)tx).BeginTransaction(IsolationProfile.FastWithRisks));
    }

    [Fact]
    public void Commit_Twice_Throws()
    {
        var tx = CreateContext(SupportedDatabase.Sqlite).BeginTransaction();
        tx.Commit();

        Assert.Throws<InvalidOperationException>(() => tx.Commit());
    }

    [Fact]
    public void Rollback_AfterCommit_Throws()
    {
        var tx = CreateContext(SupportedDatabase.Sqlite).BeginTransaction();
        tx.Commit();

        Assert.Throws<InvalidOperationException>(() => tx.Rollback());
    }

    [Fact]
    public void Dispose_Uncommitted_RollsBack()
    {
        var tx = CreateContext(SupportedDatabase.Sqlite).BeginTransaction();
        tx.Dispose();

        Assert.True(tx.IsCompleted);
        Assert.True(tx.WasRolledBack);
    }

    [Fact]
    public async Task GetLock_ReturnsRealAsyncLocker()
    {
        var tx = CreateContext(SupportedDatabase.Sqlite).BeginTransaction();
        await using var locker = tx.GetLock();

        Assert.IsType<RealAsyncLocker>(locker);
        await locker.LockAsync();
    }

    [Fact]
    public void GetLock_AfterDispose_Throws()
    {
        var tx = CreateContext(SupportedDatabase.Sqlite).BeginTransaction();
        tx.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tx.GetLock());
    }

    [Fact]
    public void GetLock_AfterCompletion_Throws()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();
        tx.Commit();
        Assert.Throws<InvalidOperationException>(() => tx.GetLock());
    }

    [Fact]
    public void CloseAndDisposeConnection_IgnoresTransactionConnection()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();
        var conn = tx.GetConnection(ExecutionType.Write);
        tx.CloseAndDisposeConnection(conn);
        Assert.Equal(ConnectionState.Open, conn.State);
    }

    [Fact]
    public void CloseAndDisposeConnection_Null_DoesNothing()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();
        tx.CloseAndDisposeConnection(null);
    }

    [Fact]
    public async Task CloseAndDisposeConnectionAsync_Null_DoesNothing()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        await using var tx = context.BeginTransaction();
        await tx.CloseAndDisposeConnectionAsync(null);
    }

    [Fact]
    public async Task CloseAndDisposeConnectionAsync_DisposesExternalConnection()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        var extra = context.GetConnection(ExecutionType.Read);
        extra.Open();
        await using var tx = context.BeginTransaction();
        await tx.CloseAndDisposeConnectionAsync(extra);
        Assert.Equal(ConnectionState.Closed, extra.State);
    }

    [Fact]
    public void MakeParameterName_ForwardsToContext()
    {
        var context = (DatabaseContext)CreateContext(SupportedDatabase.PostgreSql);
        using var tx = context.BeginTransaction();
        var param = context.CreateDbParameter("foo", DbType.Int32, 1);

        Assert.Equal(context.MakeParameterName("foo"), tx.MakeParameterName("foo"));
        Assert.Equal(context.MakeParameterName(param), tx.MakeParameterName(param));
    }

    [Fact]
    public void ProcWrappingStyle_GetMatchesContext()
    {
        var context = (DatabaseContext)CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();

        Assert.Equal(context.ProcWrappingStyle, tx.ProcWrappingStyle);

        ((IDatabaseContext)tx).ProcWrappingStyle = ProcWrappingStyle.Call;
        Assert.Equal(ProcWrappingStyle.Call, tx.ProcWrappingStyle);

        tx.Dispose();
        Assert.Throws<ObjectDisposedException>(() => ((IDatabaseContext)tx).ProcWrappingStyle = ProcWrappingStyle.Call);
    }

    [Fact]
    public void Commit_RaceOnlyOneSucceeds()
    {
        var context = (DatabaseContext)CreateContext(SupportedDatabase.Sqlite);

        var strategy = new CountingConnectionStrategy();
        ReplaceStrategy(context, strategy);

        using var tx = context.BeginTransaction();

        Exception? e1 = null;
        Exception? e2 = null;

        var t1 = Task.Run(() =>
        {
            try
            {
                tx.Commit();
            }
            catch (Exception ex)
            {
                e1 = ex;
            }
        });

        var t2 = Task.Run(() =>
        {
            try
            {
                tx.Commit();
            }
            catch (Exception ex)
            {
                e2 = ex;
            }
        });

        Task.WaitAll(t1, t2);

        Assert.True((e1 is null) ^ (e2 is null));
        Assert.IsType<InvalidOperationException>(e1 ?? e2!);
        Assert.Equal(1, strategy.ReleaseCount);
    }

    [Fact]
    public void CommitAndRollback_RaceOnlyOneSucceeds()
    {
        var context = (DatabaseContext)CreateContext(SupportedDatabase.Sqlite);

        var strategy = new CountingConnectionStrategy();
        ReplaceStrategy(context, strategy);

        using var tx = context.BeginTransaction();

        Exception? e1 = null;
        Exception? e2 = null;

        var t1 = Task.Run(() =>
        {
            try
            {
                tx.Commit();
            }
            catch (Exception ex)
            {
                e1 = ex;
            }
        });

        var t2 = Task.Run(() =>
        {
            try
            {
                tx.Rollback();
            }
            catch (Exception ex)
            {
                e2 = ex;
            }
        });

        Task.WaitAll(t1, t2);

        Assert.True((e1 is null) ^ (e2 is null));
        Assert.IsType<InvalidOperationException>(e1 ?? e2!);
        Assert.Equal(1, strategy.ReleaseCount);
    }

    private static void ReplaceStrategy(DatabaseContext context, IConnectionStrategy strategy)
    {
        var field = typeof(DatabaseContext).GetField("_connectionStrategy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field!.SetValue(context, strategy);
    }

    private sealed class CountingConnectionStrategy : SafeAsyncDisposableBase, IConnectionStrategy
    {
        private readonly ITrackedConnection _conn;
        public int ReleaseCount { get; private set; }

        public CountingConnectionStrategy()
        {
            var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
            _conn = new TrackedConnection(factory.CreateConnection());
        }

        public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
        {
            return _conn;
        }

        public void ReleaseConnection(ITrackedConnection? connection)
        {
            if (connection != null)
            {
                ReleaseCount++;
                connection.Dispose();
            }
        }

        public async ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
        {
            if (connection != null)
            {
                ReleaseCount++;
                if (connection is IAsyncDisposable ad)
                {
                    await ad.DisposeAsync();
                }
                else
                {
                    connection.Dispose();
                }
            }
        }
    }
}
