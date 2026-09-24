#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests.isolation;

/// <summary>
/// An explicitly requested isolation level is a minimum: a supported level at least as strong is
/// fine ("fail up"), a weaker one never is. When nothing at or above the request exists, beginning
/// the transaction throws instead of silently running weaker.
/// </summary>
public class IsolationFailUpTests
{
    private static DatabaseContext CreateContext(SupportedDatabase product)
    {
        return new DatabaseContext($"Data Source=test;EmulatedProduct={product}",
            new fakeDbFactory(product.ToString()));
    }

    [Theory]
    [InlineData(SupportedDatabase.DuckDB, IsolationLevel.ReadCommitted, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationLevel.ReadCommitted, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationLevel.RepeatableRead, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.Oracle, IsolationLevel.RepeatableRead, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.PostgreSql, IsolationLevel.RepeatableRead, IsolationLevel.RepeatableRead)]
    public void BeginTransaction_ExplicitLevel_ResolvesToRequestedOrNextStronger(
        SupportedDatabase product, IsolationLevel requested, IsolationLevel expected)
    {
        var context = CreateContext(product);

        using var tx = context.BeginTransaction(requested);

        Assert.Equal(expected, tx.IsolationLevel);
    }

    [Theory]
    [InlineData(SupportedDatabase.DuckDB, IsolationLevel.ReadCommitted, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationLevel.ReadCommitted, IsolationLevel.Serializable)]
    public async Task BeginTransactionAsync_ExplicitReadOnlyLevel_ResolvesToNextStronger(
        SupportedDatabase product, IsolationLevel requested, IsolationLevel expected)
    {
        var context = CreateContext(product);

        await using var tx = await context.BeginTransactionAsync(requested, ExecutionType.Read);

        Assert.Equal(expected, tx.IsolationLevel);
    }

    [Theory]
    [InlineData(SupportedDatabase.Snowflake, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.TiDb, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.Access, IsolationLevel.RepeatableRead)]
    // Snapshot guarantees non-blocking reads, which Serializable does not, so without snapshot
    // isolation enabled nothing satisfies it. (2.0.x ranks Serializable above Snapshot instead.)
    [InlineData(SupportedDatabase.SqlServer, IsolationLevel.Snapshot)]
    public void BeginTransaction_ExplicitLevel_NothingAtOrAbove_Throws(
        SupportedDatabase product, IsolationLevel requested)
    {
        var context = CreateContext(product);

        Assert.Throws<InvalidOperationException>(() => context.BeginTransaction(requested));
    }
}
