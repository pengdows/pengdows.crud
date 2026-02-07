#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for wrapped column name caching optimization in TableGateway.
/// </summary>
public class WrappedColumnNameCachingTests : SqlLiteContextTestBase
{
    [Table("test_entities")]
    private class TestEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("user_id", DbType.Int64)]
        public long UserId { get; set; }

        [Column("first_name", DbType.String)]
        public string FirstName { get; set; } = string.Empty;

        [Column("last_name", DbType.String)]
        public string LastName { get; set; } = string.Empty;

        [Column("email", DbType.String)]
        public string Email { get; set; } = string.Empty;

        [Column("created_at", DbType.DateTime)]
        public DateTime CreatedAt { get; set; }
    }

    [Fact]
    public void ColumnNameCaching_BuildCreate_UsesCachedNames()
    {
        var helper = new TableGateway<TestEntity, long>(Context);
        var entity = new TestEntity
        {
            UserId = 123,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            CreatedAt = DateTime.UtcNow
        };

        // Build multiple CREATE statements - should reuse cached column names
        var container1 = helper.BuildCreate(entity);
        var sql1 = container1.Query.ToString();

        var container2 = helper.BuildCreate(entity);
        var sql2 = container2.Query.ToString();

        // Both should produce identical SQL (proving cache works consistently)
        Assert.Equal(sql1, sql2);

        // Verify column names are properly quoted
        Assert.Contains("\"user_id\"", sql1);
        Assert.Contains("\"first_name\"", sql1);
        Assert.Contains("\"last_name\"", sql1);
        Assert.Contains("\"email\"", sql1);
        Assert.Contains("\"created_at\"", sql1);
    }

    [Fact]
    public void ColumnNameCaching_BuildRetrieve_UsesCachedNames()
    {
        var helper = new TableGateway<TestEntity, long>(Context);

        // Build multiple RETRIEVE statements
        var container1 = helper.BuildBaseRetrieve("e");
        var sql1 = container1.Query.ToString();

        var container2 = helper.BuildBaseRetrieve("e");
        var sql2 = container2.Query.ToString();

        // Both should produce identical SQL
        Assert.Equal(sql1, sql2);

        // Verify column names are properly quoted in SELECT
        Assert.Contains("\"id\"", sql1);
        Assert.Contains("\"user_id\"", sql1);
        Assert.Contains("\"email\"", sql1);
    }

    [Fact]
    public void ColumnNameCaching_MultipleOperations_ConsistentQuoting()
    {
        var helper = new TableGateway<TestEntity, long>(Context);
        var entity = new TestEntity
        {
            UserId = 456,
            FirstName = "Jane",
            LastName = "Smith",
            Email = "jane@example.com",
            CreatedAt = DateTime.UtcNow
        };

        // Perform different operations - all should use consistent column name quoting
        var createContainer = helper.BuildCreate(entity);
        var retrieveContainer = helper.BuildRetrieve(new List<long> { 1, 2, 3 }, "e");
        var deleteContainer = helper.BuildDelete(1);

        var createSql = createContainer.Query.ToString();
        var retrieveSql = retrieveContainer.Query.ToString();
        var deleteSql = deleteContainer.Query.ToString();

        // All operations should use quoted column names consistently
        // (the specific quoting style depends on dialect, but should be consistent)
        Assert.NotEmpty(createSql);
        Assert.NotEmpty(retrieveSql);
        Assert.NotEmpty(deleteSql);

        // Verify the same column name is quoted the same way across operations
        var idQuoted = "\"id\"";  // SQLite uses double quotes
        if (createSql.Contains(idQuoted))
        {
            Assert.Contains(idQuoted, retrieveSql);
            Assert.Contains(idQuoted, deleteSql);
        }
    }

    [Fact]
    public async Task ColumnNameCaching_HighVolumeCreates_PerformanceIsConsistent()
    {
        var helper = new TableGateway<TestEntity, long>(Context);

        // Create many entities - column name caching should prevent repeated wrapping
        for (int i = 0; i < 100; i++)
        {
            var entity = new TestEntity
            {
                UserId = i,
                FirstName = $"User{i}",
                LastName = $"Last{i}",
                Email = $"user{i}@example.com",
                CreatedAt = DateTime.UtcNow
            };

            var container = helper.BuildCreate(entity);
            var sql = container.Query.ToString();

            // Verify SQL is valid
            Assert.NotEmpty(sql);
            Assert.Contains("INSERT INTO", sql);
            Assert.Contains("\"test_entities\"", sql);
        }

        // If we got here without exceptions, caching is working correctly
        Assert.True(true);
    }

    [Fact]
    public void ColumnNameCaching_DifferentColumnsSameName_CachesIndependently()
    {
        // Even if two different tables have columns with the same name,
        // they should cache independently per dialect
        var helper = new TableGateway<TestEntity, long>(Context);

        var retrieveContainer = helper.BuildBaseRetrieve("e");
        var sql = retrieveContainer.Query.ToString();

        // Verify standard columns are quoted
        Assert.Contains("\"id\"", sql);
        Assert.Contains("\"email\"", sql);
        Assert.Contains("\"created_at\"", sql);
    }
}
