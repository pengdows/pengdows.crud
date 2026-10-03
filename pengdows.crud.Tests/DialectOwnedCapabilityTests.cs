using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-039: behavior that DatabaseContext and TransactionContext chose by comparing the product
/// with a specific database is now a dialect capability, answered by the dialect that has it.
/// </summary>
public sealed class DialectOwnedCapabilityTests
{
    private static SqlDialect Dialect(SupportedDatabase database) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database), NullLogger.Instance);

    [Theory]
    [InlineData(SupportedDatabase.DuckDB, true)]
    [InlineData(SupportedDatabase.Sqlite, false)]
    [InlineData(SupportedDatabase.PostgreSql, false)]
    [InlineData(SupportedDatabase.SqlServer, false)]
    public void DuckDbCapabilities_BelongToTheDuckDbDialect(SupportedDatabase database, bool duckDb)
    {
        var dialect = Dialect(database);

        Assert.Equal(duckDb, dialect.RequiresSerializedConnectionOpen);
        Assert.Equal(duckDb, dialect.ReadOnlyConnectionsCanBlockConcurrentWriters);
        Assert.Equal(duckDb, dialect.RejectsExplicitIsolationLevelOnBeginTransaction);
    }

    private static fakeDbFactory SqlServerWithIsolationState(int rcsi, int snapshot)
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetIdPopulationResult(null);
        foreach (var connection in factory.Connections)
        {
            connection.ScalarResultsByCommand[
                "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()"] = rcsi;
            connection.ScalarResultsByCommand[
                "SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()"] = snapshot;
        }

        return factory;
    }

    // The prefetch now runs the SQL Server dialect's own queries (no second, hand-copied version).
    [Fact]
    public void SqlServerIsolationPrefetch_UsesTheDialectsQueries()
    {
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer",
            SqlServerWithIsolationState(1, 1));

        Assert.True(context.RCSIEnabled);
        Assert.True(context.SnapshotIsolationEnabled);
    }

    [Fact]
    public async Task SqlServerIsolationPrefetch_CreateAsync_UsesTheDialectsQueries()
    {
        await using var context = await DatabaseContext.CreateAsync(
            new DatabaseContextConfiguration { ConnectionString = "Data Source=test;EmulatedProduct=SqlServer" },
            SqlServerWithIsolationState(1, 0));

        Assert.True(context.RCSIEnabled);
        Assert.False(context.SnapshotIsolationEnabled);
    }
}
