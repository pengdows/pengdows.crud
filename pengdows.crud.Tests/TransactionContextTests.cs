#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.Tests.Mocks;
using pengdows.crud.connection;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
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

    [Fact]
    public void QuoteProperties_DelegateToContext()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();
        Assert.Equal(context.QuotePrefix, tx.QuotePrefix);
        Assert.Equal(context.QuoteSuffix, tx.QuoteSuffix);
        Assert.Equal(context.CompositeIdentifierSeparator, tx.CompositeIdentifierSeparator);
        Assert.NotEqual("?", tx.QuotePrefix);
        Assert.NotEqual("?", tx.QuoteSuffix);
        Assert.NotEqual("?", tx.CompositeIdentifierSeparator);
    }

    [Fact]
    public void WrapObjectName_DelegatesToContext()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();
        var result = tx.WrapObjectName("Test");
        Assert.Equal(context.WrapObjectName("Test"), result);
    }

    [Fact]
    public void WrapObjectName_Null_ReturnsEmpty()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();
        Assert.Equal(string.Empty, tx.WrapObjectName(null));
    }

    [Fact]
    public void MakeParameterName_DelegatesToContext()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();
        var p = tx.CreateDbParameter("p", DbType.Int32, 1);
        Assert.Equal(context.MakeParameterName(p), tx.MakeParameterName(p));
    }

    [Fact]
    public void MakeParameterName_NullParameter_Throws()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();
        Assert.Throws<NullReferenceException>(() => tx.MakeParameterName((DbParameter)null!));
    }

    [Fact]
    public void MakeParameterName_String_DelegatesToContext()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();
        Assert.Equal(context.MakeParameterName("p"), tx.MakeParameterName("p"));
    }

    [Fact]
    public void MakeParameterName_NullString_ReturnsMarker()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();
        Assert.Equal(context.DataSourceInfo.ParameterMarker, tx.MakeParameterName((string)null));
    }

    [Fact]
    public void CreateDbParameter_DelegatesToContext()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();
        var p = tx.CreateDbParameter("p", DbType.Int32, 1);

        Assert.Equal("p", p.ParameterName);
        Assert.Equal(DbType.Int32, p.DbType);
        Assert.Equal(1, p.Value);
    }

    [Fact]
    public void CreateDbParameter_FactoryReturnsNull_Throws()
    {
        var factory = new NullParameterFactory();
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        using var tx = ctx.BeginTransaction();

        Assert.Throws<InvalidOperationException>(() => tx.CreateDbParameter("p", DbType.Int32, 1));
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
    public void ProcWrappingStyle_GetMatchesContext()
    {
        var context = (DatabaseContext)CreateContext(SupportedDatabase.Sqlite);
        using var tx = context.BeginTransaction();

        Assert.Equal(context.ProcWrappingStyle, tx.ProcWrappingStyle);
        tx.Dispose();
        Assert.Equal(context.ProcWrappingStyle, tx.ProcWrappingStyle);
    }

    [Fact]
    public void QuoteProperties_MatchContext()
    {
        var context = (DatabaseContext)CreateContext(SupportedDatabase.PostgreSql);
        using var tx = context.BeginTransaction();

        Assert.Equal(context.QuotePrefix, tx.QuotePrefix);
        Assert.Equal(context.QuoteSuffix, tx.QuoteSuffix);
        Assert.Equal(context.CompositeIdentifierSeparator, tx.CompositeIdentifierSeparator);
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

    [Fact]
    public async Task RollbackAsync_MarksAsRolledBack()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        var tx = context.BeginTransaction();
        var method = typeof(TransactionContext).GetMethod("RollbackAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        await (Task)method!.Invoke(tx, null)!;

        Assert.True(tx.WasRolledBack);
        Assert.True(tx.IsCompleted);
    }

    [Fact]
    public async Task RollbackAsync_Twice_Throws()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        var tx = context.BeginTransaction();
        var method = typeof(TransactionContext).GetMethod("RollbackAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        await (Task)method!.Invoke(tx, null)!;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await (Task)method.Invoke(tx, null)!);
    }

    [Fact]
    public void PropertyDelegates_MatchContext()
    {
        var context = CreateContext(SupportedDatabase.Sqlite);
        using var tx = (TransactionContext)context.BeginTransaction();
        var identity = (IContextIdentity)context;

        Assert.Equal(context.NumberOfOpenConnections, tx.NumberOfOpenConnections);
        Assert.NotEqual(0, tx.NumberOfOpenConnections);
        Assert.Equal(context.Product, tx.Product);
        Assert.Equal(context.MaxNumberOfConnections, tx.MaxNumberOfConnections);
        Assert.NotEqual(0, tx.MaxNumberOfConnections);
        Assert.Equal(context.IsReadOnlyConnection, tx.IsReadOnlyConnection);
        Assert.False(tx.IsReadOnlyConnection);
        Assert.Equal(context.RCSIEnabled, tx.RCSIEnabled);
        Assert.False(tx.RCSIEnabled);
        Assert.Equal(context.MaxParameterLimit, tx.MaxParameterLimit);
        Assert.NotEqual(0, tx.MaxParameterLimit);
        Assert.Equal(DbMode.SingleConnection, tx.ConnectionMode);
        Assert.NotEqual(DbMode.SingleWriter, tx.ConnectionMode);
        Assert.Equal(context.TypeMapRegistry, tx.TypeMapRegistry);
        Assert.NotNull(tx.TypeMapRegistry);
        Assert.Equal(context.DataSourceInfo, tx.DataSourceInfo);
        Assert.NotNull(tx.DataSourceInfo);
        Assert.Equal(context.SessionSettingsPreamble, tx.SessionSettingsPreamble);
        Assert.Equal(identity.RootId, tx.RootId);
        Assert.NotEqual(Guid.Empty, tx.TransactionId);
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
