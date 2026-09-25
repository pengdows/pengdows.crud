#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class InvalidTransactionTypeTests
{
    // -------------------------------------------------------------------------
    // Unsupported isolation levels per database via the full BeginTransaction path
    // -------------------------------------------------------------------------

    // An unsupported level resolves up to the weakest stronger supported level — never down.
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql, IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted)]
    [InlineData(SupportedDatabase.Oracle, IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted)]
    [InlineData(SupportedDatabase.Oracle, IsolationLevel.RepeatableRead, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationLevel.ReadCommitted, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationLevel.RepeatableRead, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.DuckDB, IsolationLevel.ReadCommitted, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.DuckDB, IsolationLevel.RepeatableRead, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.Sqlite, IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted)]
    public void BeginTransaction_UnsupportedIsolationLevel_ResolvesUp(
        SupportedDatabase product, IsolationLevel level, IsolationLevel expected)
    {
        var context = new DatabaseContext(
            $"Data Source=test;EmulatedProduct={product}",
            new fakeDbFactory(product));

        using var tx = context.BeginTransaction(level);
        Assert.Equal(expected, tx.IsolationLevel);
    }

    // Nothing at or above the requested level exists, so it throws rather than run weaker.
    [Theory]
    [InlineData(SupportedDatabase.TiDb, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.Snowflake, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.Snowflake, IsolationLevel.RepeatableRead)]
    public void BeginTransaction_UnsupportedIsolationLevel_Throws(
        SupportedDatabase product, IsolationLevel level)
    {
        var context = new DatabaseContext(
            $"Data Source=test;EmulatedProduct={product}",
            new fakeDbFactory(product));

        Assert.Throws<InvalidOperationException>(() => context.BeginTransaction(level));
    }

    // -------------------------------------------------------------------------
    // IsolationLevel.Chaos is universally invalid across all databases
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.DuckDB)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.Firebird)]
    public void BeginTransaction_ChaosLevel_AlwaysThrows(SupportedDatabase product)
    {
        var context = new DatabaseContext(
            $"Data Source=test;EmulatedProduct={product}",
            new fakeDbFactory(product));

        Assert.Throws<InvalidOperationException>(() => context.BeginTransaction(IsolationLevel.Chaos));
    }

    // -------------------------------------------------------------------------
    // Read-only context must reject write-mode transaction requests
    // -------------------------------------------------------------------------

    [Fact]
    public void BeginTransaction_ReadOnlyContext_DefaultIsWrite_ThrowsNotSupportedException()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        Assert.Throws<NotSupportedException>(() => context.BeginTransaction());
    }

    [Fact]
    public void BeginTransaction_ReadOnlyContext_ExplicitWriteExecutionType_ThrowsNotSupportedException()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        Assert.Throws<NotSupportedException>(() =>
            context.BeginTransaction(executionType: ExecutionType.Write));
    }

    // -------------------------------------------------------------------------
    // Async path mirrors sync path for the same validations
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeginTransactionAsync_PostgreSql_SafeNonBlockingReads_UsesRepeatableRead()
    {
        var context = new DatabaseContext(
            $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}",
            new fakeDbFactory(SupportedDatabase.PostgreSql));

        await using var tx = await context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads);
        Assert.Equal(System.Data.IsolationLevel.RepeatableRead, tx.IsolationLevel);
    }

    [Fact]
    public async Task BeginTransactionAsync_UnsupportedIsolationLevel_ThrowsInvalidOperationException()
    {
        var context = new DatabaseContext(
            $"Data Source=test;EmulatedProduct={SupportedDatabase.Snowflake}",
            new fakeDbFactory(SupportedDatabase.Snowflake));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.BeginTransactionAsync(IsolationLevel.Serializable));
    }

    [Fact]
    public async Task BeginTransactionAsync_ReadOnlyContext_ThrowsNotSupportedException()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        await Assert.ThrowsAsync<NotSupportedException>(async () => await context.BeginTransactionAsync());
    }
}
