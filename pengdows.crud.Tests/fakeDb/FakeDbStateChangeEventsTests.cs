using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// Some real drivers never raise <see cref="System.Data.Common.DbConnection.StateChange"/>
/// (Snowflake.Data 4.8.0's SnowflakeDbConnection has no OnStateChange call at all). fakeDb can
/// emulate that so code relying on the event can be tested against such a driver.
/// </summary>
public class FakeDbStateChangeEventsTests
{
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    public void RaiseConnectionStateChangeEvents_ControlsWhetherOpenAndCloseRaiseStateChange(bool raise, int expectedEvents)
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer) { RaiseConnectionStateChangeEvents = raise };
        using var connection = factory.CreateConnection();
        connection.ConnectionString = "Server=db;Database=test;EmulatedProduct=SqlServer";
        var events = 0;
        connection.StateChange += (_, _) => events++;

        connection.Open();
        connection.Close();

        Assert.Equal(expectedEvents, events);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }
}
