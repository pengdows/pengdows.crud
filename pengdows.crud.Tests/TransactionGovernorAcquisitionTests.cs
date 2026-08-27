using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Locks in the product-thesis.md principle-2 claim: a transaction acquires its governed
/// connection exactly once, at BeginTransaction, not once per command executed inside it.
/// </summary>
public class TransactionGovernorAcquisitionTests
{
    [Fact]
    public async Task Transaction_AcquiresGovernedConnectionOnce_NotPerCommand()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        var before = context.GetPoolStatisticsSnapshot(PoolLabel.Writer).TotalAcquired;

        await using (var tx = context.BeginTransaction())
        {
            for (var i = 0; i < 5; i++)
            {
                await using var container = tx.CreateSqlContainer("SELECT 1");
                await container.ExecuteNonQueryAsync();
            }

            tx.Commit();
        }

        var after = context.GetPoolStatisticsSnapshot(PoolLabel.Writer).TotalAcquired;

        Assert.Equal(1, after - before);
    }

    [Fact]
    public async Task Sequential_NonTransactional_Commands_EachAcquireSeparately()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var context = new DatabaseContext(config, factory);

        var before = context.GetPoolStatisticsSnapshot(PoolLabel.Writer).TotalAcquired;

        for (var i = 0; i < 5; i++)
        {
            await using var container = context.CreateSqlContainer("SELECT 1");
            await container.ExecuteNonQueryAsync();
        }

        var after = context.GetPoolStatisticsSnapshot(PoolLabel.Writer).TotalAcquired;

        Assert.Equal(5, after - before);
    }
}
