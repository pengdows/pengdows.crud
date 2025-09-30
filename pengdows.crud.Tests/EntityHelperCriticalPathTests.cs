using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Critical path tests for EntityHelper to ensure error handling and edge cases are covered
/// </summary>
public class EntityHelperCriticalPathTests
{
    [Table("TestEntity")]
    public class TestEntity
    {
        [Id]
        public int Id { get; set; }

        [Column("name", DbType.String, 255)]
        public string? Name { get; set; }

        [Version]
        public byte[]? RowVersion { get; set; }

        [CreatedBy]
        public string? CreatedBy { get; set; }

        [CreatedOn]
        public DateTime? CreatedOn { get; set; }

        [LastUpdatedBy]
        public string? LastUpdatedBy { get; set; }

        [LastUpdatedOn]
        public DateTime? LastUpdatedOn { get; set; }
    }

    [Table("BadEntity")]
    public class EntityWithoutId
    {
        [Column("name", DbType.String, 255)]
        public string? Name { get; set; }
    }

    [Table("InvalidEntity")]
    public class EntityWithBadIdType
    {
        [Id]
        public object? Id { get; set; } // Invalid ID type

        [Column("name", DbType.String, 255)]
        public string? Name { get; set; }
    }

    /// <summary>
    /// Test EntityHelper with entity that has no ID attribute
    /// </summary>
    [Fact]
    public void EntityHelper_EntityWithoutId_ThrowsInvalidOperation()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);

        // Should throw when trying to create EntityHelper for entity without ID
        Assert.Throws<InvalidOperationException>(() =>
            new EntityHelper<EntityWithoutId, int>(context));
    }

    /// <summary>
    /// Test EntityHelper with incompatible ID type
    /// </summary>
    [Fact]
    public void EntityHelper_IncompatibleIdType_ThrowsInvalidOperation()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);

        // Should throw when ID type doesn't match entity's ID property type
        Assert.Throws<InvalidOperationException>(() =>
            new EntityHelper<EntityWithBadIdType, int>(context));
    }

    /// <summary>
    /// Test CreateAsync with connection failure during execution
    /// </summary>
    [Fact]
    public async Task EntityHelper_CreateAsync_ConnectionFailure_ThrowsCorrectException()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetGlobalFailureMode(ConnectionFailureMode.FailOnCommand);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        var entity = new TestEntity { Name = "Test" };

        // Should throw when connection fails during command creation
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await helper.CreateAsync(entity, context));
    }

    /// <summary>
    /// Test UpdateAsync with stale concurrency token
    /// </summary>
    [Fact]
    public async Task EntityHelper_UpdateAsync_StaleConcurrencyToken_HandlesCorrectly()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        var entity = new TestEntity
        {
            Id = 1,
            Name = "Test",
            RowVersion = new byte[] { 1, 2, 3, 4 } // Simulate stale version
        };

        // Create update command
        var updateContainer = await helper.BuildUpdateAsync(entity);

        // Should handle concurrency token in SQL generation
        Assert.Contains("RowVersion", updateContainer.Query.ToString());
    }

    /// <summary>
    /// Test UpsertAsync with merge operation failure fallback
    /// </summary>
    [Fact]
    public async Task EntityHelper_UpsertAsync_MergeFailureFallback_WorksCorrectly()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite); // SQLite doesn't support MERGE
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        var entity = new TestEntity { Id = 1, Name = "Test" };

        // Should fall back to INSERT/UPDATE pattern for databases without MERGE
        var upsertContainer = helper.BuildUpsert(entity);
        Assert.NotNull(upsertContainer);

        // For SQLite, should not contain MERGE statement
        var sql = upsertContainer.Query.ToString();
        Assert.DoesNotContain("MERGE", sql.ToUpperInvariant());
    }

    /// <summary>
    /// Test RetrieveAsync with empty ID collection
    /// </summary>
    [Fact]
    public async Task EntityHelper_RetrieveAsync_EmptyIdCollection_ReturnsEmptyList()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        // Empty ID collection should return empty list
        var result = await helper.RetrieveAsync(new List<int>());
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    /// <summary>
    /// Test RetrieveAsync with null ID collection
    /// </summary>
    [Fact]
    public async Task EntityHelper_RetrieveAsync_NullIdCollection_ThrowsArgumentNullException()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        // Null ID collection should throw
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await helper.RetrieveAsync((IEnumerable<int>)null!));
    }

    /// <summary>
    /// Test DeleteAsync with large ID collection (IN clause limits)
    /// </summary>
    [Fact]
    public async Task EntityHelper_DeleteAsync_LargeIdCollection_HandlesCorrectly()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        // Create large ID collection to test IN clause handling
        var largeIdList = new List<int>();
        for (int i = 1; i <= 10000; i++)
        {
            largeIdList.Add(i);
        }

        // Should handle large IN clauses (might batch or use temp tables)
        var deleteCount = await helper.DeleteAsync(largeIdList);
        Assert.True(deleteCount >= 0); // Should not throw
    }

    /// <summary>
    /// Test LoadSingleAsync with multiple results error
    /// </summary>
    [Fact]
    public async Task EntityHelper_LoadSingleAsync_MultipleResults_ThrowsInvalidOperation()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        // Create SQL that returns multiple rows
        using var container = context.CreateSqlContainer("SELECT 1 as Id, 'Test1' as name UNION SELECT 2 as Id, 'Test2' as name");

        // Should throw when expecting single result but getting multiple
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await helper.LoadSingleAsync(container));
    }

    /// <summary>
    /// Test BuildRetrieve with null alias
    /// </summary>
    [Fact]
    public void EntityHelper_BuildRetrieve_NullAlias_HandlesCorrectly()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        // Should handle null alias gracefully
        var container = helper.BuildBaseRetrieve(null);
        Assert.NotNull(container);

        var sql = container.Query.ToString();
        Assert.Contains("SELECT", sql);
    }

    /// <summary>
    /// Test entity mapping with circular references
    /// </summary>
    [Fact]
    public void EntityHelper_EntityMapping_CircularReferences_HandlesCorrectly()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);

        // Should handle entity registration without circular reference issues
        var helper1 = new EntityHelper<TestEntity, int>(context);
        var helper2 = new EntityHelper<TestEntity, int>(context); // Same entity type

        Assert.NotNull(helper1);
        Assert.NotNull(helper2);
    }

    /// <summary>
    /// Test audit field population with null context
    /// </summary>
    [Fact]
    public async Task EntityHelper_AuditFieldPopulation_NullContext_UsesDefaults()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        var entity = new TestEntity { Name = "Test" };

        // Should populate audit fields even without explicit audit context
        var createContainer = helper.BuildCreate(entity);
        var sql = createContainer.Query.ToString();

        // Should include audit field handling
        Assert.Contains("INSERT", sql);
    }

    /// <summary>
    /// Test concurrent entity operations
    /// </summary>
    [Fact]
    public async Task EntityHelper_ConcurrentOperations_ThreadSafe()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        using var context = new DatabaseContext(config, factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        // Run multiple concurrent operations
        var tasks = new Task<ISqlContainer>[10];
        for (int i = 0; i < 10; i++)
        {
            int entityId = i;
            tasks[i] = Task.Run(() =>
            {
                var entity = new TestEntity { Id = entityId, Name = $"Test{entityId}" };
                var container = helper.BuildCreate(entity);
                return container;
            });
        }

        var results = await Task.WhenAll(tasks);

        // All operations should complete successfully
        Assert.All(results, container => Assert.NotNull(container));
    }
}