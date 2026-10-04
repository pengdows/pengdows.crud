using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// WRT-001, confirmed live (Firebird 5.0.2, FirebirdClient 10.3.3): after writes, the engine's
/// garbage-collector attachment holds the table, and a DDL in FirebirdClient's implicit transaction
/// (always NO WAIT, as is every IsolationLevel transaction) fails at once with "lock conflict on no
/// wait transaction ... object TABLE is in use" — for over 90 s. In a WAIT transaction the engine
/// makes the collector release it and the DDL succeeds. FirebirdClient takes WAIT only through its
/// own FbTransactionOptions, so the dialect starts the DDL's transaction through it (by reflection).
/// </summary>
public sealed class FirebirdDdlLockWaitTests
{
    // FbConnection's BeginTransaction(FbTransactionOptions) shape, over fakeDb.
    private sealed class OptionsConnection : fakeDbConnection
    {
        public FbTransactionOptions? Options { get; private set; }

        public DbTransaction BeginTransaction(FbTransactionOptions options)
        {
            Options = options;
            return BeginTransaction();
        }
    }

    private static SqlDialect Dialect() =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.Firebird,
            new fakeDbFactory(SupportedDatabase.Firebird), NullLogger.Instance);

    [Fact]
    public void BeginDdlTransaction_StartsAReadCommittedWaitTransactionWithTheLockTimeout()
    {
        var connection = new OptionsConnection { EmulatedProduct = SupportedDatabase.Firebird };
        connection.Open();

        var transaction = Dialect().BeginDdlTransaction(connection, TimeSpan.FromSeconds(30));

        Assert.NotNull(transaction);
        Assert.True(connection.Options!.TransactionBehavior.HasFlag(FbTransactionBehavior.Wait));
        Assert.True(connection.Options.TransactionBehavior.HasFlag(FbTransactionBehavior.ReadCommitted));
        Assert.False(connection.Options.TransactionBehavior.HasFlag(FbTransactionBehavior.NoWait));
        Assert.Equal(TimeSpan.FromSeconds(30), connection.Options.WaitTimeout);
    }

    [Fact]
    public void BeginDdlTransaction_ProviderWithoutTransactionOptions_ReturnsNull()
    {
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Firebird };
        connection.Open();

        Assert.Null(Dialect().BeginDdlTransaction(connection, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void OtherDialects_HaveNoDdlTransaction()
    {
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.PostgreSql,
            new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);
        var connection = new OptionsConnection();
        connection.Open();

        Assert.Null(dialect.BeginDdlTransaction(connection, TimeSpan.FromSeconds(30)));
    }

    private static (DatabaseContext Context, fakeDbFactory Factory) FirebirdContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=fb;Database=test.fdb;EmulatedProduct=Firebird",
            DbMode = DbMode.Standard
        }, factory);
        // Every connection from here on has FbConnection's BeginTransaction(FbTransactionOptions); the
        // pool reset before a DDL also creates connections (to find ClearAllPools).
        for (var i = 0; i < 6; i++)
        {
            factory.Connections.Add(new OptionsConnection { EmulatedProduct = SupportedDatabase.Firebird });
        }

        return (context, factory);
    }

    private static OptionsConnection Ran(fakeDbFactory factory, string sql) =>
        (OptionsConnection)Assert.Single(factory.CreatedConnections, c => c.ExecutedNonQueryTexts.Contains(sql));

    [Fact]
    public async Task Ddl_OutsideATransaction_RunsInTheWaitTransactionAndCommitsIt()
    {
        var (context, factory) = FirebirdContext();
        await using var _ = context;

        await using var sc = context.CreateSqlContainer("CREATE TABLE t (id INTEGER)");
        await sc.ExecuteNonQueryAsync();

        var ddl = Ran(factory, "CREATE TABLE t (id INTEGER)");
        Assert.NotNull(ddl.Options);
        Assert.Equal(1, ddl.LastTransaction!.CommitCallCount);
    }

    [Fact]
    public async Task Ddl_Failing_RollsTheWaitTransactionBack()
    {
        var (context, factory) = FirebirdContext();
        await using var _ = context;
        factory.SetCommandFailure("DROP TABLE t", new InvalidOperationException("lock conflict"));

        await using var sc = context.CreateSqlContainer("DROP TABLE t");
        await Assert.ThrowsAnyAsync<Exception>(async () => await sc.ExecuteNonQueryAsync());

        var ddl = (OptionsConnection)Assert.Single(factory.CreatedConnections, c => c is OptionsConnection { Options: not null });
        Assert.NotNull(ddl.Options);
        Assert.Equal(0, ddl.LastTransaction!.CommitCallCount);
        Assert.Equal(1, ddl.LastTransaction.RollbackCallCount);
    }

    [Fact]
    public async Task Ddl_InsideACallersTransaction_UsesThatTransaction()
    {
        var (context, factory) = FirebirdContext();
        await using var _ = context;
        await using var tx = await context.BeginTransactionAsync();

        await using var sc = tx.CreateSqlContainer("CREATE TABLE t (id INTEGER)");
        await sc.ExecuteNonQueryAsync();

        Assert.Null(Ran(factory, "CREATE TABLE t (id INTEGER)").Options);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task DataStatement_IsUnchanged()
    {
        var (context, factory) = FirebirdContext();
        await using var _ = context;

        await using var sc = context.CreateSqlContainer("UPDATE t SET id = 1");
        await sc.ExecuteNonQueryAsync();

        Assert.Null(Ran(factory, "UPDATE t SET id = 1").Options);
    }
}
