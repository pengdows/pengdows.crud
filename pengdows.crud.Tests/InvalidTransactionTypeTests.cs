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

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql,  IsolationLevel.ReadUncommitted)]
    [InlineData(SupportedDatabase.Oracle,      IsolationLevel.ReadUncommitted)]
    [InlineData(SupportedDatabase.Oracle,      IsolationLevel.RepeatableRead)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationLevel.ReadCommitted)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationLevel.RepeatableRead)]
    [InlineData(SupportedDatabase.DuckDB,      IsolationLevel.ReadCommitted)]
    [InlineData(SupportedDatabase.DuckDB,      IsolationLevel.RepeatableRead)]
    [InlineData(SupportedDatabase.TiDb,        IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.Snowflake,   IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.Snowflake,   IsolationLevel.RepeatableRead)]
    [InlineData(SupportedDatabase.Sqlite,      IsolationLevel.ReadUncommitted)]
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
    public async Task BeginTransactionAsync_PostgreSql_SafeNonBlockingReads_ThrowsTransactionModeNotSupportedException()
    {
        var context = new DatabaseContext(
            $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}",
            new fakeDbFactory(SupportedDatabase.PostgreSql));

        await Assert.ThrowsAsync<TransactionModeNotSupportedException>(() =>
            context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads));
    }

    [Fact]
    public async Task BeginTransactionAsync_UnsupportedIsolationLevel_ThrowsInvalidOperationException()
    {
        var context = new DatabaseContext(
            $"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}",
            new fakeDbFactory(SupportedDatabase.PostgreSql));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.BeginTransactionAsync(IsolationLevel.ReadUncommitted));
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

        await Assert.ThrowsAsync<NotSupportedException>(() => context.BeginTransactionAsync());
    }
}
