using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.Tests.Logging;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-051: a read-intent transaction on Informix accepted a write (live). The dialect's
/// "SET TRANSACTION READ ONLY" ran on a command with no Transaction, which providers reject on a
/// connection with a pending local transaction, and the failure was swallowed at Debug level. A failure to enter read-only now throws.
/// </summary>
public sealed class ReadOnlyTransactionEntryTests
{
    private static (DatabaseContext Context, fakeDbConnection Connection, ListLoggerProvider Logs) Informix()
    {
        var logs = new ListLoggerProvider();
        var factory = new fakeDbFactory(SupportedDatabase.Informix);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix });
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix };
        factory.Connections.Add(connection);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;Database=d;EmulatedProduct=Informix",
            DbMode = DbMode.Standard
        }, factory, new LoggerFactory(new[] { logs }));
        return (context, connection, logs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOnlyStatement_RunsInsideTheTransaction(bool async)
    {
        var (context, connection, _) = Informix();
        await using var _ = context;

        await using var tx = async
            ? await context.BeginTransactionAsync(executionType: ExecutionType.Read)
            : context.BeginTransaction(executionType: ExecutionType.Read);

        var command = Assert.Single(connection.CreatedCommands, c => c.CommandText == "SET TRANSACTION READ ONLY");
        Assert.NotNull(command.Transaction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOnlyEntryFailure_IsLoggedAndThrown_AndReleasesTheConnection(bool async)
    {
        var (context, connection, logs) = Informix();
        await using var _ = context;
        var failure = new InvalidOperationException("cannot set");
        connection.SetCommandFailure("SET TRANSACTION READ ONLY", failure);

        var ex = await Assert.ThrowsAsync<TransactionException>(async () =>
        {
            await using var tx = async
                ? await context.BeginTransactionAsync(executionType: ExecutionType.Read)
                : context.BeginTransaction(executionType: ExecutionType.Read);
        });

        Assert.Same(failure, ex.InnerException);
        Assert.Contains(logs.Entries, e => e.Level >= LogLevel.Warning && e.Exception == failure);
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    // TiDB refuses "SET SESSION transaction_read_only = 1" ("function READ ONLY has only noop implementation in
    // tidb now"; live, TiDB v8.5.7) and cannot enforce read-only even with tidb_enable_noop_functions, so it sends
    // no statement: a read-intent transaction on TiDB began only while a failure here was swallowed.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TiDb_ReadIntentTransaction_SendsNoReadOnlyStatement(bool async)
    {
        var factory = new fakeDbFactory(SupportedDatabase.TiDb);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.TiDb });
        var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.TiDb };
        connection.SetCommandFailure("SET SESSION transaction_read_only = 1;",
            new InvalidOperationException("function READ ONLY has only noop implementation in tidb now"));
        factory.Connections.Add(connection);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;Database=d;EmulatedProduct=TiDb",
            DbMode = DbMode.Standard
        }, factory);

        await using var tx = async
            ? await context.BeginTransactionAsync(executionType: ExecutionType.Read)
            : context.BeginTransaction(executionType: ExecutionType.Read);

        Assert.DoesNotContain(connection.CreatedCommands.Select(c => c.CommandText), t => t.Contains("transaction_read_only = 1"));
    }
}
