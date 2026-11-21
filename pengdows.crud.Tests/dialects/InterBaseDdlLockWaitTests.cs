using System;
using System.Data.Common;
using System.Threading.Tasks;
using InterBaseSql.Data.InterBaseClient;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// InterBase is a Firebird-family server and has the same DDL-versus-attachment conflict: after writes, a DDL in the
/// driver's implicit transaction (NO WAIT) fails at once with "object ... is in use" while another attachment still holds
/// the object. The Firebird dialect answers that with a pool reset before DDL and a WAIT transaction for the DDL; the
/// InterBase dialect must do the same through the InterBase driver's own types (IBConnection.ClearAllPools and
/// IBTransactionOptions). Without it, the DDL in an integration run raced a pooled attachment and failed with
/// "unsuccessful metadata update / object INDEX is in use".
/// </summary>
public sealed class InterBaseDdlLockWaitTests
{
    // IBConnection's BeginTransaction(IBTransactionOptions) shape, over fakeDb.
    private sealed class OptionsConnection : fakeDbConnection
    {
        public IBTransactionOptions? Options { get; private set; }

        public DbTransaction BeginTransaction(IBTransactionOptions options)
        {
            Options = options;
            return BeginTransaction();
        }
    }

    private static SqlDialect Dialect() =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.InterBase,
            new fakeDbFactory(SupportedDatabase.InterBase), NullLogger.Instance);

    [Fact]
    public void RequiresConnectionPoolResetForDdl_IsTrue_ForInterBase()
    {
        Assert.True(Dialect().RequiresConnectionPoolResetForDdl);
    }

    [Fact]
    public void BeginDdlTransaction_StartsAWaitTransactionWithTheLockTimeout()
    {
        var connection = new OptionsConnection { EmulatedProduct = SupportedDatabase.InterBase };
        connection.Open();

        var transaction = Dialect().BeginDdlTransaction(connection, TimeSpan.FromSeconds(30));

        Assert.NotNull(transaction);
        Assert.True(connection.Options!.TransactionBehavior.HasFlag(IBTransactionBehavior.Wait));
        Assert.False(connection.Options.TransactionBehavior.HasFlag(IBTransactionBehavior.NoWait));
        Assert.Equal(TimeSpan.FromSeconds(30), connection.Options.WaitTimeout);
    }

    [Fact]
    public void BeginDdlTransaction_ProviderWithoutTransactionOptions_ReturnsNull()
    {
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.InterBase };
        connection.Open();

        Assert.Null(Dialect().BeginDdlTransaction(connection, TimeSpan.FromSeconds(30)));
    }

    private static (DatabaseContext Context, fakeDbFactory Factory) InterBaseContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.InterBase);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=ib;Database=test.gdb;EmulatedProduct=InterBase",
            DbMode = DbMode.Standard
        }, factory);
        for (var i = 0; i < 6; i++)
        {
            factory.Connections.Add(new OptionsConnection { EmulatedProduct = SupportedDatabase.InterBase });
        }

        return (context, factory);
    }

    [Fact]
    public async Task Ddl_OutsideATransaction_RunsInTheWaitTransaction()
    {
        var (context, factory) = InterBaseContext();
        await using var _ = context;

        const string sql = "CREATE INDEX ix_t_id ON t (id)";
        await using var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();

        var ddl = (OptionsConnection)Assert.Single(factory.CreatedConnections, c => c.ExecutedNonQueryTexts.Contains(sql));
        Assert.NotNull(ddl.Options);
        Assert.True(ddl.Options!.TransactionBehavior.HasFlag(IBTransactionBehavior.Wait));
    }
}
