using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A server that refuses a connection at its limit must surface as <see cref="TooManyConnectionsException"/> from
/// every entry point, not as a generic <see cref="DatabaseOperationException"/>: it is the signal a caller (and any
/// adaptive sizing) keys on, and it must not depend on which operation happened to be the one refused. The
/// translator maps SQLSTATE 53300 correctly in isolation; these push the same failure through the real call paths.
/// </summary>
public class ConnectionLimitRefusalSurfacingTests
{
    private sealed class RefusedAtLimit : DbException
    {
        public RefusedAtLimit() : base("53300: sorry, too many clients already")
        {
        }

        public override string? SqlState => "53300";
    }

    private static (DatabaseContext Context, fakeDbConnection Refusing) ContextWhoseNextConnectionIsRefused()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Host=h;Database=d;EmulatedProduct=PostgreSql",
                DbMode = DbMode.Standard
            }, factory);

        var refusing = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        refusing.SetCustomFailureException(new RefusedAtLimit());
        refusing.SetFailOnOpen();
        factory.Connections.Add(refusing);
        return (context, refusing);
    }

    [Fact]
    public async Task ExecuteNonQuery_WhenTheOpenIsRefusedAtTheLimit_ThrowsTooManyConnections()
    {
        var (context, _) = ContextWhoseNextConnectionIsRefused();
        using var _ = context;
        await using var sc = context.CreateSqlContainer("UPDATE t SET a = 1");

        var ex = await Assert.ThrowsAsync<TooManyConnectionsException>(async () => await sc.ExecuteNonQueryAsync());

        Assert.Equal("53300", ex.SqlState);
        Assert.IsAssignableFrom<DbException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteScalar_WhenTheOpenIsRefusedAtTheLimit_ThrowsTooManyConnections()
    {
        var (context, _) = ContextWhoseNextConnectionIsRefused();
        using var _ = context;
        await using var sc = context.CreateSqlContainer("SELECT 1");

        await Assert.ThrowsAsync<TooManyConnectionsException>(async () => await sc.ExecuteScalarRequiredAsync<int>());
    }

    [Fact]
    public async Task ExecuteReader_WhenTheOpenIsRefusedAtTheLimit_ThrowsTooManyConnections()
    {
        var (context, _) = ContextWhoseNextConnectionIsRefused();
        using var _ = context;
        await using var sc = context.CreateSqlContainer("SELECT 1");

        await Assert.ThrowsAsync<TooManyConnectionsException>(async () =>
        {
            await using var reader = await sc.ExecuteReaderAsync();
        });
    }

    [Fact]
    public async Task BeginTransaction_WhenTheOpenIsRefusedAtTheLimit_ThrowsTooManyConnections()
    {
        var (context, _) = ContextWhoseNextConnectionIsRefused();
        using var _ = context;

        await Assert.ThrowsAsync<TooManyConnectionsException>(async () =>
        {
            await using var tx = await context.BeginTransactionAsync();
        });
    }

    [Fact]
    public void BeginTransaction_Sync_WhenTheOpenIsRefusedAtTheLimit_ThrowsTooManyConnections()
    {
        var (context, _) = ContextWhoseNextConnectionIsRefused();
        using var _ = context;

        Assert.Throws<TooManyConnectionsException>(() =>
        {
            using var tx = context.BeginTransaction();
        });
    }

    [Fact]
    public async Task ExecuteNonQuery_WhenTheCommandItselfIsRefusedAtTheLimit_ThrowsTooManyConnections()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Host=h;Database=d;EmulatedProduct=PostgreSql",
                DbMode = DbMode.Standard
            }, factory);
        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn.SetNonQueryExecuteException(new RefusedAtLimit());
        factory.Connections.Add(conn);
        await using var sc = context.CreateSqlContainer("UPDATE t SET a = 1");

        await Assert.ThrowsAsync<TooManyConnectionsException>(async () => await sc.ExecuteNonQueryAsync());
    }

    private static DatabaseContext ContextWhoseNextTransactionBeginIsRefused()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Host=h;Database=d;EmulatedProduct=PostgreSql",
                DbMode = DbMode.Standard
            }, factory);
        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn.SetCustomFailureException(new RefusedAtLimit());
        conn.SetFailOnBeginTransaction();
        factory.Connections.Add(conn);
        return context;
    }

    [Fact]
    public async Task BeginTransaction_WhenTheBeginIsRefusedAtTheLimit_ThrowsTooManyConnections()
    {
        using var context = ContextWhoseNextTransactionBeginIsRefused();

        var ex = await Assert.ThrowsAsync<TooManyConnectionsException>(async () =>
        {
            await using var tx = await context.BeginTransactionAsync();
        });

        Assert.Equal("53300", ex.SqlState);
    }

    [Fact]
    public void BeginTransaction_Sync_WhenTheBeginIsRefusedAtTheLimit_ThrowsTooManyConnections()
    {
        using var context = ContextWhoseNextTransactionBeginIsRefused();

        Assert.Throws<TooManyConnectionsException>(() =>
        {
            using var tx = context.BeginTransaction();
        });
    }

    // A failure to begin that is not a connection problem keeps its established type.
    [Fact]
    public async Task BeginTransaction_WhenTheBeginFailsForAnotherReason_StillThrowsTransactionException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Host=h;Database=d;EmulatedProduct=PostgreSql",
                DbMode = DbMode.Standard
            }, factory);
        var conn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        conn.SetFailOnBeginTransaction();
        factory.Connections.Add(conn);

        await Assert.ThrowsAsync<TransactionException>(async () =>
        {
            await using var tx = await context.BeginTransactionAsync();
        });
    }
}
