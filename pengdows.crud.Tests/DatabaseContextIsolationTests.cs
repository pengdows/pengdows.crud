#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DatabaseContextIsolationTests
{
    [Theory]
    [InlineData(SupportedDatabase.SqlServer, IsolationProfile.SafeNonBlockingReads, IsolationLevel.Snapshot)]
    [InlineData(SupportedDatabase.SqlServer, IsolationProfile.StrictConsistency, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.SqlServer, IsolationProfile.FastWithRisks, IsolationLevel.ReadUncommitted)]
    [InlineData(SupportedDatabase.PostgreSql, IsolationProfile.StrictConsistency, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.PostgreSql, IsolationProfile.FastWithRisks, IsolationLevel.ReadCommitted)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationProfile.StrictConsistency, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationProfile.SafeNonBlockingReads, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.DuckDB, IsolationProfile.SafeNonBlockingReads, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.DuckDB, IsolationProfile.StrictConsistency, IsolationLevel.Serializable)]
    public void BeginTransaction_ResolvesIsolationLevel(SupportedDatabase product, IsolationProfile profile,
        IsolationLevel expected)
    {
        var factory = new fakeDbFactory(product.ToString());
        if (product == SupportedDatabase.SqlServer)
        {
            var connection = new fakeDbConnection();
            connection.SetScalarResultForCommand(
                "SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()",
                1);
            connection.SetScalarResultForCommand(
                "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()",
                1);
            factory.Connections.Add(connection);
        }

        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        using var tx = context.BeginTransaction(profile);

        Assert.Equal(expected, tx.IsolationLevel);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void BeginTransaction_SafeNonBlockingReads_UsesRepeatableReadForPostgresCompatibleDatabases(
        SupportedDatabase product)
    {
        // BP-209: PostgreSQL/YugabyteDB RepeatableRead is an MVCC snapshot, so the profile's
        // non-blocking, consistent-read guarantee is met - it used to throw.
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}",
            new fakeDbFactory(product.ToString()));
        using var tx = context.BeginTransaction(IsolationProfile.SafeNonBlockingReads);
        Assert.Equal(IsolationLevel.RepeatableRead, tx.IsolationLevel);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public async Task BeginTransactionAsync_SafeNonBlockingReads_UsesRepeatableReadForPostgresCompatibleDatabases(
        SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}",
            new fakeDbFactory(product.ToString()));
        await using var tx = await context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads);
        Assert.Equal(IsolationLevel.RepeatableRead, tx.IsolationLevel);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.Oracle)]
    public void BeginTransaction_ProfileRequiresRcsi_DoesNotThrowForOtherProviders(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product.ToString());
        if (product == SupportedDatabase.SqlServer)
        {
            var connection = new fakeDbConnection();
            connection.SetScalarResultForCommand(
                "SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()",
                1);
            connection.SetScalarResultForCommand(
                "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()",
                1);
            factory.Connections.Add(connection);
        }

        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);

        using var tx = context.BeginTransaction(IsolationProfile.SafeNonBlockingReads);
        Assert.NotNull(tx);
    }

    [Fact]
    public void BeginTransaction_ProfileSupported_CockroachDb_And_DuckDB()
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.CockroachDb}",
            new fakeDbFactory(SupportedDatabase.CockroachDb.ToString()));
        using var tx1 = context.BeginTransaction(IsolationProfile.FastWithRisks);
        Assert.NotNull(tx1);

        context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.DuckDB}",
            new fakeDbFactory(SupportedDatabase.DuckDB.ToString()));
        using var tx2 = context.BeginTransaction(IsolationProfile.FastWithRisks);
        Assert.NotNull(tx2);
    }

    [Fact]
    public void BeginTransaction_UnknownProduct_UsesSerializable()
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Unknown}",
            new fakeDbFactory(SupportedDatabase.Unknown.ToString()));

        using var tx = context.BeginTransaction(IsolationProfile.StrictConsistency);
        Assert.Equal(IsolationLevel.Serializable, tx.IsolationLevel);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer, IsolationLevel.Serializable, true)]
    [InlineData(SupportedDatabase.PostgreSql, IsolationLevel.Serializable, true)]
    [InlineData(SupportedDatabase.MySql, IsolationLevel.Serializable, true)]
    [InlineData(SupportedDatabase.Sqlite, IsolationLevel.Serializable, true)]
    [InlineData(SupportedDatabase.TiDb, IsolationLevel.Serializable, false)]
    [InlineData(SupportedDatabase.TiDb, IsolationLevel.ReadCommitted, true)]
    [InlineData(SupportedDatabase.Snowflake, IsolationLevel.Serializable, false)]
    [InlineData(SupportedDatabase.Snowflake, IsolationLevel.ReadCommitted, true)]
    public void GetSupportedIsolationLevels_ReflectsProviderCapabilities(
        SupportedDatabase product, IsolationLevel level, bool expectedSupported)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}",
            new fakeDbFactory(product.ToString()));

        var supported = context.GetSupportedIsolationLevels();

        Assert.Equal(expectedSupported, supported.Contains(level));
    }
}