#region

using System;
using System.Data;
using pengdows.crud.enums;
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

    [Fact]
    public void BeginTransaction_ProfileRequiresRcsi_Throws()
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}",
            new fakeDbFactory(SupportedDatabase.PostgreSql.ToString()));
        Assert.Throws<NotSupportedException>(() => context.BeginTransaction(IsolationProfile.SafeNonBlockingReads));
    }

    [Fact]
    public void BeginTransaction_ProfileUnsupported_Throws()
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.CockroachDb}",
            new fakeDbFactory(SupportedDatabase.CockroachDb.ToString()));
        Assert.Throws<NotSupportedException>(() => context.BeginTransaction(IsolationProfile.FastWithRisks));

        context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.DuckDB}",
            new fakeDbFactory(SupportedDatabase.DuckDB.ToString()));
        Assert.Throws<NotSupportedException>(() => context.BeginTransaction(IsolationProfile.FastWithRisks));
    }

    [Fact]
    public void BeginTransaction_UnknownProduct_UsesSerializable()
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Unknown}",
            new fakeDbFactory(SupportedDatabase.Unknown.ToString()));

        using var tx = context.BeginTransaction(IsolationProfile.StrictConsistency);
        Assert.Equal(IsolationLevel.Serializable, tx.IsolationLevel);
    }
}
