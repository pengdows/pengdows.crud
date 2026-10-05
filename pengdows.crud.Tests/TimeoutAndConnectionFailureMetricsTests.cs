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
/// DRY-017: three timeout checks had drifted. SqlContainer's (which decides the CommandsTimedOut
/// metric) looked only at the outer exception, so a provider timeout wrapped the way Npgsql wraps a
/// client-side CommandTimeout was thrown as CommandTimeoutException (the translators walk the inner
/// exceptions) but counted as a plain failure. DatabaseContext.TotalConnectionFailures and
/// TotalConnectionTimeoutFailures were never incremented outside tests: nothing called
/// TrackConnectionFailure.
/// </summary>
public class TimeoutAndConnectionFailureMetricsTests
{
    private sealed class ProviderException : DbException
    {
        public ProviderException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    [Fact]
    public async Task WrappedProviderTimeout_IsThrownAndCountedAsATimeout()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = new fakeDbConnection();
        factory.Connections.Add(connection);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true
        }, factory);
        connection.SetCommandFailure("SELECT 1", new ProviderException("Exception while reading from stream",
            new TimeoutException("Timeout during reading attempt")));

        await using var container = context.CreateSqlContainer("SELECT 1");
        await Assert.ThrowsAsync<CommandTimeoutException>(async () => await container.ExecuteScalarOrNullAsync<int>());

        Assert.Equal(1, context.Metrics.CommandsTimedOut);
    }

    private static (DatabaseContext Context, fakeDbConnection Failing) ContextWithFailingNextOpen(Exception? failure)
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
        if (failure != null)
        {
            failing.SetCustomFailureException(failure);
        }

        factory.Connections.Add(failing);
        return (context, failing);
    }

    [Fact]
    public async Task FailedOpen_IsCountedAsAConnectionFailure()
    {
        var (context, _) = ContextWithFailingNextOpen(null);
        await using var _ = context;

        await using var container = context.CreateSqlContainer("SELECT 1");
        await Assert.ThrowsAnyAsync<Exception>(async () => await container.ExecuteScalarOrNullAsync<int>());

        Assert.Equal(1, context.TotalConnectionFailures);
        Assert.Equal(0, context.TotalConnectionTimeoutFailures);
    }

    [Fact]
    public async Task OpenTimeout_IsCountedAsAConnectionTimeoutFailure()
    {
        var (context, _) = ContextWithFailingNextOpen(new ProviderException("Exception while connecting",
            new TimeoutException("Timeout during connection attempt")));
        await using var _ = context;

        await using var container = context.CreateSqlContainer("SELECT 1");
        await Assert.ThrowsAnyAsync<Exception>(async () => await container.ExecuteScalarOrNullAsync<int>());

        Assert.Equal(1, context.TotalConnectionFailures);
        Assert.Equal(1, context.TotalConnectionTimeoutFailures);
    }

    [Fact]
    public void SyncOpenFailure_IsCountedToo()
    {
        var (context, _) = ContextWithFailingNextOpen(null);
        using var _ = context;

        Assert.ThrowsAny<Exception>(() => context.BeginTransaction());

        Assert.Equal(1, context.TotalConnectionFailures);
    }
}
