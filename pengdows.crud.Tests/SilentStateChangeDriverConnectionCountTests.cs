using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// pengdows counts open connections (NumberOfOpenConnections/PeakOpenConnections and the connection
/// metrics) from the driver's StateChange events. CONFIRMED by decompiling Snowflake.Data 4.8.0:
/// SnowflakeDbConnection never raises StateChange, so on Snowflake the counts stayed at zero (an
/// integration test skipped its connection-count assertion there, blaming "lazy" opening). Opens and
/// closes that the driver doesn't report must still be counted, and drivers that do report them
/// must not be counted twice.
/// </summary>
public sealed class SilentStateChangeDriverConnectionCountTests
{
    private static DatabaseContext CreateContext(bool driverRaisesStateChange) =>
        new(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=db;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard
        }, new fakeDbFactory(SupportedDatabase.SqlServer) { RaiseConnectionStateChangeEvents = driverRaisesStateChange });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Transaction_CountsItsConnectionOnce_WhetherOrNotTheDriverReportsIt(bool driverRaisesStateChange)
    {
        using var context = CreateContext(driverRaisesStateChange);

        await using (var transaction = context.BeginTransaction())
        {
            Assert.Equal(1, transaction.NumberOfOpenConnections);
            transaction.Commit();
        }

        Assert.Equal(0, context.NumberOfOpenConnections);
        Assert.Equal(1, context.PeakOpenConnections);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Command_OpensAndClosesItsConnection_WhetherOrNotTheDriverReportsIt(bool driverRaisesStateChange)
    {
        using var context = CreateContext(driverRaisesStateChange);

        await using (var sc = context.CreateSqlContainer("UPDATE t SET x = 1"))
        {
            await sc.ExecuteNonQueryAsync();
        }

        Assert.Equal(0, context.NumberOfOpenConnections);
        Assert.Equal(1, context.PeakOpenConnections);
    }
}
