using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.Tests.Logging;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-051: a read-intent transaction on Informix accepted a write (live). The dialect's
/// "SET TRANSACTION READ ONLY" ran on a command with no Transaction, which providers reject on a
/// connection with a pending local transaction, and the failure was swallowed at Debug level.
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

    [Fact]
    public async Task ReadOnlyEntryFailure_IsLoggedAsAWarning()
    {
        var (context, connection, logs) = Informix();
        await using var _ = context;
        connection.SetCommandFailure("SET TRANSACTION READ ONLY", new InvalidOperationException("cannot set"));

        await using var tx = await context.BeginTransactionAsync(executionType: ExecutionType.Read);

        Assert.Contains(logs.Entries, e => e.Level >= LogLevel.Warning && e.Exception?.Message == "cannot set");
    }
}
