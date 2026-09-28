using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CONFIRMED by decompiling AdoNetCore.AseClient 0.19.2: AseConnection's StateChange add/remove
/// accessors throw ObjectDisposedException once the connection is disposed. pengdows detaches its
/// StateChange handlers after disposing the connection, so on Sybase ASE disposing a connection threw
/// out of Dispose, which also skipped releasing the connection's pool slot.
/// </summary>
public sealed class DisposedConnectionRefusesStateChangeAccessTests
{
    private static DatabaseContext CreateContext(bool enableMetrics) =>
        new(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=db;Database=test;EmulatedProduct=SybaseASE;Max Pool Size=1",
            DbMode = DbMode.Standard,
            EnableMetrics = enableMetrics
        }, new fakeDbFactory(SupportedDatabase.SybaseASE) { ThrowOnStateChangeAccessAfterDispose = true });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Commands_DisposeTheirConnections_AndReleaseTheirPoolSlots(bool enableMetrics)
    {
        using var context = CreateContext(enableMetrics);

        for (var i = 0; i < 3; i++)
        {
            await using var sc = context.CreateSqlContainer("UPDATE t SET x = 1");
            await sc.ExecuteNonQueryAsync();
        }

        Assert.Equal(0, context.NumberOfOpenConnections);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Transactions_DisposeTheirConnections_AndReleaseTheirPoolSlots(bool enableMetrics)
    {
        using var context = CreateContext(enableMetrics);

        for (var i = 0; i < 3; i++)
        {
            using var transaction = context.BeginTransaction();
            transaction.Commit();
        }

        Assert.Equal(0, context.NumberOfOpenConnections);
    }
}
