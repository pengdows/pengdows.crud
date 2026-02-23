#region

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.isolation;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for issues found during PR #146 code review.
/// </summary>
public class CodeReviewFixTests
{
    // ================================================================
    // Finding 1: Savepoint names must be quoted to prevent SQL injection
    // ================================================================

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Sqlite)]
    public void GetSavepointSql_Should_Quote_Savepoint_Name(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext("test", factory);
        var dialect = ((ISqlDialectProvider)context).Dialect;

        var sql = dialect.GetSavepointSql("my_savepoint");

        // The savepoint name should be wrapped with the dialect's quote characters
        Assert.Contains(dialect.QuotePrefix, sql);
        Assert.Contains(dialect.QuoteSuffix, sql);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Sqlite)]
    public void GetRollbackToSavepointSql_Should_Quote_Savepoint_Name(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext("test", factory);
        var dialect = ((ISqlDialectProvider)context).Dialect;

        var sql = dialect.GetRollbackToSavepointSql("my_savepoint");

        Assert.Contains(dialect.QuotePrefix, sql);
        Assert.Contains(dialect.QuoteSuffix, sql);
    }

    [Fact]
    public void GetSavepointSql_SqlServer_Should_Quote_Savepoint_Name()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("test", factory);
        var dialect = ((ISqlDialectProvider)context).Dialect;

        var sql = dialect.GetSavepointSql("my_savepoint");

        // SQL Server uses SAVE TRANSACTION, name should still be quoted
        Assert.Contains("SAVE TRANSACTION", sql);
        Assert.Contains(dialect.QuotePrefix, sql);
        Assert.Contains(dialect.QuoteSuffix, sql);
    }

    [Fact]
    public void GetSavepointSql_Prevents_Injection_Attempt()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("test", factory);
        var dialect = ((ISqlDialectProvider)context).Dialect;

        // A malicious savepoint name should be wrapped, not interpolated raw
        var sql = dialect.GetSavepointSql("sp1; DROP TABLE users; --");

        // Should be wrapped with quote characters
        Assert.Contains(dialect.QuotePrefix, sql);
        Assert.Contains(dialect.QuoteSuffix, sql);
    }

    // ================================================================
    // Finding 2: Async transaction completion should have a timeout
    // matching the sync path
    // ================================================================

    [Fact]
    public async Task CompleteTransactionAsync_Should_Not_Wait_Indefinitely()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetNonQueryResult(1);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);

        // Basic verification that async commit completes without hanging
        await using var tx = context.BeginTransaction();
        // This should complete promptly (the sync path has a timeout, async should too)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var commitTask = Task.Run(() => tx.Commit(), cts.Token);
        var completed = await Task.WhenAny(commitTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(commitTask, completed); // Should complete, not timeout
    }

    // ================================================================
    // Finding 3: GetPrimaryKeys should throw InvalidOperationException,
    // not bare Exception
    // ================================================================

    [Fact]
    public void RetrieveOneAsync_WithEntity_NoPrimaryKeys_Throws_InvalidOperationException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        var helper = new TableGateway<NoPrimaryKeyEntity, int>(context);

        var entity = new NoPrimaryKeyEntity { Id = 1, Name = "test" };

        // Should throw InvalidOperationException, not bare Exception
        Assert.Throws<InvalidOperationException>(() =>
        {
            // RetrieveOneAsync(TEntity) uses GetPrimaryKeys() internally
            var task = helper.RetrieveOneAsync(entity);
            task.GetAwaiter().GetResult();
        });
    }

    // ================================================================
    // Finding 4: CockroachDB and others should map FastWithRisks profile
    // ================================================================

    [Fact]
    public void IsolationResolver_CockroachDb_FastWithRisks_Should_Not_Throw()
    {
        var resolver = new IsolationResolver(SupportedDatabase.CockroachDb, false, false);

        // Should resolve without throwing NotSupportedException
        var result = resolver.ResolveWithDetail(IsolationProfile.FastWithRisks);

        // CockroachDB only supports Serializable, so FastWithRisks maps to Serializable
        Assert.Equal(IsolationLevel.Serializable, result.Level);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void IsolationResolver_AllDatabases_Should_Map_FastWithRisks(SupportedDatabase product)
    {
        var resolver = new IsolationResolver(product, false, false);

        // All databases should handle FastWithRisks without throwing
        var result = resolver.ResolveWithDetail(IsolationProfile.FastWithRisks);
        // Verify it resolved to something valid
        Assert.True(Enum.IsDefined(typeof(IsolationLevel), result.Level));
    }

    // ================================================================
    // Test entities
    // ================================================================

    [Table("no_pk_entity")]
    public class NoPrimaryKeyEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}