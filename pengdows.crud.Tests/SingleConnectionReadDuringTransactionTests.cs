using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// D06: in <see cref="DbMode.SingleConnection"/> every command shares one physical connection. A
/// read through the plain context while a transaction is open on that connection used to reach
/// the provider without the transaction: real Microsoft.Data.Sqlite either silently enlists the
/// command in that transaction (a command created while it is open) or rejects it ("Execute
/// requires the command to have a transaction object…", one created before), and fakeDb did neither. Such a read is now
/// rejected up front with a clear exception; read through the transaction instead. A plain read
/// holds the connection for its lifetime, so a transaction can't start underneath an open reader.
/// </summary>
[Collection("SqliteSerial")]
public class SingleConnectionReadDuringTransactionTests
{
    private static DatabaseContext FakeSingleConnectionContext() =>
        new(new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
                DbMode = DbMode.SingleConnection
            },
            new fakeDbFactory(SupportedDatabase.Sqlite), NullLoggerFactory.Instance);

    [Fact]
    public async Task PlainRead_WhileTransactionOpen_IsRejected()
    {
        await using var context = FakeSingleConnectionContext();
        await using var tx = await context.BeginTransactionAsync();

        await using var sc = context.CreateSqlContainer("SELECT 1");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var reader = await sc.ExecuteReaderAsync();
        });
        Assert.Contains("transaction", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlainScalar_WhileTransactionOpen_IsRejected()
    {
        await using var context = FakeSingleConnectionContext();
        await using var tx = await context.BeginTransactionAsync();

        await using var sc = context.CreateSqlContainer("SELECT 1");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sc.ExecuteScalarOrNullAsync<int>());
    }

    [Fact]
    public async Task ReadThroughTheTransaction_Works()
    {
        await using var context = FakeSingleConnectionContext();
        await using var tx = await context.BeginTransactionAsync();

        await using var sc = tx.CreateSqlContainer("SELECT 1");
        await using (var reader = await sc.ExecuteReaderAsync())
        {
        }

        await tx.CommitAsync();
    }

    [Fact]
    public async Task PlainRead_AfterTransactionCompletes_Works()
    {
        await using var context = FakeSingleConnectionContext();
        await using (var tx = await context.BeginTransactionAsync())
        {
            await tx.RollbackAsync();
        }

        await using var sc = context.CreateSqlContainer("SELECT 1");
        await using (var reader = await sc.ExecuteReaderAsync())
        {
        }
    }

    [Fact]
    public async Task OpenPlainReader_TransactionWaitsUntilReaderDisposed()
    {
        await using var context = FakeSingleConnectionContext();
        await using var sc = context.CreateSqlContainer("SELECT 1");
        var reader = await sc.ExecuteReaderAsync();

        var begin = Task.Run(async () => await context.BeginTransactionAsync());
        var startedEarly = await Task.WhenAny(begin, Task.Delay(200)) == begin;

        await reader.DisposeAsync();
        await using var tx = await begin.WaitAsync(TimeSpan.FromSeconds(10));
        await tx.RollbackAsync();
        Assert.False(startedEarly, "A transaction must not start while a plain reader holds the connection.");
    }

    [Fact]
    public async Task StandardMode_PlainRead_WhileTransactionOpen_IsUnaffected()
    {
        await using var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Host=localhost;EmulatedProduct=PostgreSql",
                DbMode = DbMode.Standard
            },
            new fakeDbFactory(SupportedDatabase.PostgreSql), NullLoggerFactory.Instance);
        await using var tx = await context.BeginTransactionAsync();

        await using var sc = context.CreateSqlContainer("SELECT 1");
        await using (var reader = await sc.ExecuteReaderAsync())
        {
        }
    }

    // Real SQLite: the rejection replaces the provider's own "Execute requires the command to have
    // a transaction object" error, deterministically.
    [Fact]
    public async Task RealSqlite_PlainRead_WhileTransactionOpen_IsRejectedByPengdows()
    {
        await using var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=:memory:",
                DbMode = DbMode.SingleConnection
            },
            SqliteFactory.Instance, NullLoggerFactory.Instance);
        await using var tx = await context.BeginTransactionAsync();

        await using var sc = context.CreateSqlContainer("SELECT 1");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sc.ExecuteScalarOrNullAsync<long>());
        Assert.DoesNotContain("Execute requires the command to have a transaction object", ex.Message);
    }
}
