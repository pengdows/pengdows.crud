using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;

namespace pengdows.crud.IntegrationTests.Infrastructure;

public class TestTableCreatorSnowflakeTests
{
    [Fact]
    public async Task CreateTestTableAsync_Snowflake_UsesSnowflakeTypes()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        using var context = new DatabaseContext("Data Source=fake", factory);
        var creator = new TestTableCreator(context);

        await creator.CreateTestTableAsync();

        var sql = GetLastNonQuery(factory);
        Assert.Contains("\"test_table\"", sql, StringComparison.Ordinal);
        Assert.Contains("TIMESTAMP_NTZ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BOOLEAN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateRoundTripTableAsync_Snowflake_UsesSnowflakeSpecificTypes()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        using var context = new DatabaseContext("Data Source=fake", factory);
        var creator = new TestTableCreator(context);

        await creator.CreateRoundTripTableAsync();

        var sql = GetLastNonQuery(factory);
        Assert.Contains("TIMESTAMP_NTZ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VARCHAR(36)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VARBINARY(256)", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLastNonQuery(fakeDbFactory factory)
    {
        return factory.CreatedConnections.SelectMany(c => c.ExecutedNonQueryTexts).Last();
    }
}
