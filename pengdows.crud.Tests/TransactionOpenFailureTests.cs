using System;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Opening the connection for a transaction translates only a connection problem (a refusal at the limit, a
/// lost connection), as beginning it does. Any other provider failure keeps the type it had in 2.0.5, and
/// every failure gives the pool permit back.
/// </summary>
public class TransactionOpenFailureTests
{
    private sealed class SerializationFailure : DbException
    {
        public SerializationFailure() : base("40001: could not serialize access")
        {
        }

        public override string? SqlState => "40001";
    }

    private sealed class RefusedAtLimit : DbException
    {
        public RefusedAtLimit() : base("53300: sorry, too many clients already")
        {
        }

        public override string? SqlState => "53300";
    }

    private static DatabaseContext ContextWhoseNextOpenFails(DbException failure)
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Host=h;Database=d;EmulatedProduct=PostgreSql",
                DbMode = DbMode.Standard
            }, factory);
        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn.SetCustomFailureException(failure);
        conn.SetFailOnOpen();
        factory.Connections.Add(conn);
        return context;
    }

    [Fact]
    public void BeginTransaction_Sync_WhenTheOpenFailsForAnotherReason_ThrowsTheProviderException()
    {
        using var context = ContextWhoseNextOpenFails(new SerializationFailure());

        Assert.Throws<SerializationFailure>(() =>
        {
            using var tx = context.BeginTransaction();
        });
    }

    [Fact]
    public async Task BeginTransaction_Async_WhenTheOpenFailsForAnotherReason_ThrowsTheProviderException()
    {
        using var context = ContextWhoseNextOpenFails(new SerializationFailure());

        await Assert.ThrowsAsync<SerializationFailure>(async () =>
        {
            await using var tx = await context.BeginTransactionAsync();
        });
    }

    [Fact]
    public void BeginTransaction_Sync_WhenTheOpenFailsForAnotherReason_ReleasesTheConnection()
    {
        using var context = ContextWhoseNextOpenFails(new SerializationFailure());

        Assert.ThrowsAny<Exception>(() => context.BeginTransaction());

        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public async Task BeginTransaction_Async_WhenTheOpenIsRefused_ReleasesTheConnection()
    {
        using var context = ContextWhoseNextOpenFails(new RefusedAtLimit());

        await Assert.ThrowsAsync<TooManyConnectionsException>(async () => await context.BeginTransactionAsync());

        Assert.Equal(0, context.NumberOfOpenConnections);
    }
}
