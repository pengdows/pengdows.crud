#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
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

    // Isolation fails up: an unsupported explicit level runs at the weakest supported level that
    // guarantees at least as much.
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql, IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted)]
    [InlineData(SupportedDatabase.Oracle, IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted)]
    [InlineData(SupportedDatabase.Oracle, IsolationLevel.RepeatableRead, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationLevel.ReadCommitted, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationLevel.RepeatableRead, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.DuckDB, IsolationLevel.ReadCommitted, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.DuckDB, IsolationLevel.RepeatableRead, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.Sqlite, IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted)]
    public void BeginTransaction_UnsupportedIsolationLevel_FailsUp(
        SupportedDatabase product, IsolationLevel level, IsolationLevel expected)
    {
        var context = new DatabaseContext(
            $"Data Source=test;EmulatedProduct={product}",
            new fakeDbFactory(product));

        using var tx = context.BeginTransaction(level);
        Assert.Equal(expected, tx.IsolationLevel);
    }

    // Nothing at or above the requested level exists: throw rather than run weaker.
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
    public void BeginTransaction_ReadOnlyContext_DefaultIsWrite_ThrowsReadOnlyContextException()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        // ReadOnlyContextException extends NotSupportedException and additionally implements
        // IReadOnlyViolation — see docs/planning/3.0-architectural-review-backlog.md's P0 item.
        var ex = Assert.Throws<pengdows.crud.exceptions.ReadOnlyContextException>(() => context.BeginTransaction());
        Assert.IsAssignableFrom<pengdows.crud.exceptions.IReadOnlyViolation>(ex);
    }

    [Fact]
    public void BeginTransaction_ReadOnlyContext_ExplicitWriteExecutionType_ThrowsReadOnlyContextException()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        Assert.Throws<pengdows.crud.exceptions.ReadOnlyContextException>(() =>
            context.BeginTransaction(executionType: ExecutionType.Write));
    }

    // -------------------------------------------------------------------------
    // Async path mirrors sync path for the same validations
    // -------------------------------------------------------------------------

    // Regression: PostgreSQL's REPEATABLE READ is a correct, non-blocking exact match for
    // SafeNonBlockingReads (MVCC snapshot, no phantom reads within the transaction) — see
    // PostgreSqlDialect.GetIsolationProfileMapping/GetIsolationGuarantees. This must not throw.
    [Fact]
    public async Task BeginTransactionAsync_PostgreSql_SafeNonBlockingReads_ResolvesToRepeatableRead()
    {
        var context = new DatabaseContext(
            $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}",
            new fakeDbFactory(SupportedDatabase.PostgreSql));

        await using var tx = await context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads);
        Assert.Equal(IsolationLevel.RepeatableRead, tx.IsolationLevel);
    }

    [Fact]
    public async Task BeginTransactionAsync_UnsupportedIsolationLevel_FailsUp()
    {
        var context = new DatabaseContext(
            $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}",
            new fakeDbFactory(SupportedDatabase.PostgreSql));

        await using var tx = await context.BeginTransactionAsync(IsolationLevel.ReadUncommitted);
        Assert.Equal(IsolationLevel.ReadCommitted, tx.IsolationLevel);
    }

    [Fact]
    public async Task BeginTransactionAsync_NothingAtOrAboveRequestedLevel_ThrowsInvalidOperationException()
    {
        var context = new DatabaseContext(
            $"Data Source=test;EmulatedProduct={SupportedDatabase.TiDb}",
            new fakeDbFactory(SupportedDatabase.TiDb));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.BeginTransactionAsync(IsolationLevel.Serializable));
    }

    [Fact]
    public async Task BeginTransactionAsync_ReadOnlyContext_ThrowsReadOnlyContextException()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        // ReadOnlyContextException extends NotSupportedException and additionally implements
        // IReadOnlyViolation — see docs/planning/3.0-architectural-review-backlog.md's P0 item.
        await Assert.ThrowsAsync<pengdows.crud.exceptions.ReadOnlyContextException>(
            async () => await context.BeginTransactionAsync());
    }
}
