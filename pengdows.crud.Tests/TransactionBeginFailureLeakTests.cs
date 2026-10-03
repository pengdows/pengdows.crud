using System;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-026: BeginTransaction got its connection (holding a pool permit), then opened it and took
/// the single-connection gate outside any try. A failed open leaked the connection and its permit.
/// </summary>
public sealed class TransactionBeginFailureLeakTests
{
    private static (DatabaseContext Context, fakeDbConnection Failing) ContextWithFailingNextOpen()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Host=x;Database=d;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            EnableMetrics = true
        }, factory);
        var failing = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        failing.SetFailOnOpen();
        factory.Connections.Add(failing);
        return (context, failing);
    }

    [Fact]
    public void BeginTransaction_OpenFails_ReleasesTheConnection()
    {
        var (context, failing) = ContextWithFailingNextOpen();
        using var _ = context;

        Assert.ThrowsAny<Exception>(() => context.BeginTransaction());

        Assert.True(failing.DisposeCount > 0, "The connection whose open failed was never disposed.");
        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Fact]
    public async Task BeginTransactionAsync_OpenFails_ReleasesTheConnection()
    {
        var (context, failing) = ContextWithFailingNextOpen();
        await using var _ = context;

        await Assert.ThrowsAnyAsync<Exception>(async () => await context.BeginTransactionAsync());

        Assert.True(failing.DisposeCount > 0, "The connection whose open failed was never disposed.");
        Assert.Equal(0, context.NumberOfOpenConnections);
    }
}
