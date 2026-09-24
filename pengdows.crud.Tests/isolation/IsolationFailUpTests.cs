#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests.isolation;

/// <summary>
/// Isolation resolution guarantees at least what the caller asked for: a stronger level is fine
/// ("fail up"), a weaker one never is. When nothing at or above the request exists, beginning the
/// transaction throws instead of silently running weaker.
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
    [InlineData(SupportedDatabase.SqlServer, IsolationLevel.Snapshot, IsolationLevel.Serializable)]
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
    public void BeginTransaction_ExplicitLevel_NothingAtOrAbove_Throws(
        SupportedDatabase product, IsolationLevel requested)
    {
        var context = CreateContext(product);

        Assert.Throws<InvalidOperationException>(() => context.BeginTransaction(requested));
    }

    [Theory]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.Snowflake)]
    [InlineData(SupportedDatabase.Access)]
    public void BeginTransaction_StrictConsistency_WithoutSerializable_Throws(SupportedDatabase product)
    {
        var context = CreateContext(product);

        Assert.Throws<TransactionModeNotSupportedException>(() =>
            context.BeginTransaction(IsolationProfile.StrictConsistency));
    }

    [Theory]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.Snowflake)]
    [InlineData(SupportedDatabase.Access)]
    public async Task BeginTransactionAsync_StrictConsistency_WithoutSerializable_Throws(SupportedDatabase product)
    {
        var context = CreateContext(product);

        await Assert.ThrowsAsync<TransactionModeNotSupportedException>(async () =>
            await context.BeginTransactionAsync(IsolationProfile.StrictConsistency));
    }

    [Fact]
    public void BeginTransaction_SafeNonBlockingReads_SqlServerWithoutSnapshot_Throws()
    {
        var context = CreateContext(SupportedDatabase.SqlServer);

        Assert.Throws<TransactionModeNotSupportedException>(() =>
            context.BeginTransaction(IsolationProfile.SafeNonBlockingReads));
    }

    /// <summary>
    /// A read-only transaction with no level and no profile asked for no minimum, so it keeps
    /// the context's default instead of throwing.
    /// </summary>
    [Fact]
    public void BeginTransaction_ReadOnlyNoLevel_SqlServerWithoutSnapshot_StillSucceeds()
    {
        var context = CreateContext(SupportedDatabase.SqlServer);

        using var tx = context.BeginTransaction(executionType: ExecutionType.Read);

        Assert.Equal(IsolationLevel.ReadCommitted, tx.IsolationLevel);
    }
}
