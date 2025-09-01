#region

using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ConnectionStrategyTests
{
    [Fact]
    public async Task SingleConnection_ReusesSameUnderlyingConnection()
    {
        var factory = new MultiConnectionFactory();
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var ctx = new DatabaseContext(cfg, factory, null, new TypeMapRegistry());
        var initialCreates = factory.CreatedCount;

        await using var c1 = ctx.CreateSqlContainer("SELECT 1");
        await c1.ExecuteNonQueryAsync(); // write path

        await using var c2 = ctx.CreateSqlContainer("SELECT 1");
        await c2.ExecuteReaderAsync(); // read path

        // No additional connections should have been created beyond the initial persistent one
        Assert.Equal(initialCreates, factory.CreatedCount);
    }

    [Fact]
    public async Task SingleWriter_ReadCreatesNew_WriteReusesPersistent()
    {
        var factory = new MultiConnectionFactory();
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var ctx = new DatabaseContext(cfg, factory, null, new TypeMapRegistry());
        var initialCreates = factory.CreatedCount;

        await using var write = ctx.CreateSqlContainer("SELECT 1");
        await write.ExecuteNonQueryAsync(); // should use persistent
        Assert.Equal(initialCreates, factory.CreatedCount);

        await using var read = ctx.CreateSqlContainer("SELECT 1");
        await read.ExecuteReaderAsync(); // should create a new standard connection
        Assert.True(factory.CreatedCount > initialCreates);
    }

    [Fact]
    public async Task StandardMode_NewConnectionPerOperation()
    {
        var factory = new MultiConnectionFactory();
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var ctx = new DatabaseContext(cfg, factory, null, new TypeMapRegistry());
        var initialCreates = factory.CreatedCount; // includes one from initialization (disposed later)

        await using var c1 = ctx.CreateSqlContainer("SELECT 1");
        await c1.ExecuteNonQueryAsync();

        await using var c2 = ctx.CreateSqlContainer("SELECT 1");
        await c2.ExecuteReaderAsync();

        // Expect at least two additional connections created for the two operations
        Assert.True(factory.CreatedCount >= initialCreates + 2);
    }

    private sealed class MultiConnectionFactory : DbProviderFactory
    {
        public int CreatedCount { get; private set; }

        public override DbConnection CreateConnection()
        {
            CreatedCount++;
            return new UniqueConnection();
        }

        public override DbCommand CreateCommand() => new fakeDbCommand();
        public override DbParameter CreateParameter() => new fakeDbParameter();
    }

    private sealed class UniqueConnection : fakeDbConnection
    {
        // Inherit behavior from fakeDbConnection; no extra tracking required here
    }
}

