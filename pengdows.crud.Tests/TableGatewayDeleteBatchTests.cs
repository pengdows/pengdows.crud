#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class TableGatewayDeleteBatchTests : IAsyncLifetime
{
    private IDatabaseContext _sqliteContext = null!;
    private fakeDbFactory _sqliteFactory = null!;
    private readonly TypeMapRegistry _typeMap;

    public TableGatewayDeleteBatchTests()
    {
        _typeMap = new TypeMapRegistry();
    }

    private void ResetFactory()
    {
        _sqliteFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        _sqliteFactory.EnableDataPersistence = true;
        _sqliteContext = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", _sqliteFactory, _typeMap);
    }

    public Task InitializeAsync()
    {
        ResetFactory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Table("test_delete_batch")]
    public class TestDeleteBatchEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }

        [PrimaryKey]
        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("test_delete_composite")]
    public class TestDeleteCompositeEntity
    {
        [PrimaryKey]
        [Column("key1", DbType.Int32)]
        public int Key1 { get; set; }

        [PrimaryKey]
        [Column("key2", DbType.Int32)]
        public int Key2 { get; set; }
    }

    [Table("test_delete_id_only")]
    public class TestDeleteIdOnlyEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
    }

    [Table("test_delete_no_id")]
    public class TestDeleteNoIdEntity
    {
        [PrimaryKey(1)]
        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task BatchDeleteAsync_NullIdList_Throws()
    {
        ResetFactory();
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await gateway.BatchDeleteAsync((IEnumerable<int>)null!));
    }

    [Fact]
    public async Task BatchDeleteAsync_NullEntityList_Throws()
    {
        ResetFactory();
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await gateway.BatchDeleteAsync((IReadOnlyCollection<TestDeleteBatchEntity>)null!));
    }

    [Fact]
    public async Task BatchDeleteAsync_EmptyEntityList_ReturnsZero()
    {
        // Entity-based overload: empty = no work, returns 0
        ResetFactory();
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        var result = await gateway.BatchDeleteAsync(Array.Empty<TestDeleteBatchEntity>());
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task BatchDeleteAsync_EmptyIdList_Throws()
    {
        // ID-based overload: empty = likely caller bug, throws ArgumentException
        ResetFactory();
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await gateway.BatchDeleteAsync(Array.Empty<int>()));
    }

    [Fact]
    public void BuildBatchDelete_EmptyEntityList_ReturnsEmptyContainerList()
    {
        ResetFactory();
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        var containers = gateway.BuildBatchDelete(Array.Empty<TestDeleteBatchEntity>());
        Assert.Empty(containers);
    }

    [Fact]
    public async Task DeleteAsync_LargeIdList_ChunksCorrectly()
    {
        // Arrange
        ResetFactory();
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        var ids = Enumerable.Range(1, 33000).ToList();

        // Act
        // SQLite 3.32+ MaxParameterLimit = 32766. Headroom 10% -> 29489.
        // 33000 IDs should result in 2 chunks (29489 and 3511)
        var affected = await gateway.DeleteAsync(ids);

        // Assert
        var connections = _sqliteFactory.CreatedConnections;
        var totalCommands = connections.SelectMany(c => c.ExecutedNonQueryTexts)
            .Count(text => text.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, totalCommands);
    }

    [Fact]
    public async Task BatchDeleteAsync_LargeIdList_ChunksCorrectly()
    {
        // Arrange
        ResetFactory();
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        var ids = Enumerable.Range(1, 33000).ToList();

        // Act
        // SQLite 3.32+ MaxParameterLimit = 32766. Headroom 10% -> 29489.
        // 33000 IDs should result in 2 chunks (29489 and 3511)
        await gateway.BatchDeleteAsync(ids);

        // Assert
        var totalCommands = _sqliteFactory.CreatedConnections
            .SelectMany(c => c.ExecutedNonQueryTexts)
            .Count(text => text.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, totalCommands);
    }

    [Fact]
    public async Task BatchDeleteAsync_IdList_DoesNotRequirePrimaryKey()
    {
        // Arrange
        ResetFactory();
        var gateway = new TableGateway<TestDeleteIdOnlyEntity, int>(_sqliteContext);

        // Act
        var exception = await Record.ExceptionAsync(() => gateway.BatchDeleteAsync(new[] { 1, 2, 3 }).AsTask());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void BuildBatchDelete_IdList_ReturnsMultipleContainers()
    {
        // Arrange
        ResetFactory();
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        var ids = Enumerable.Range(1, 33000).ToList();

        // Act
        var containers = gateway.BuildBatchDelete(ids);

        // Assert
        Assert.Equal(2, containers.Count);
    }

    [Fact]
    public async Task DeleteBatchAsync_LargeEntityList_ChunksCorrectly()
    {
        // Arrange
        ResetFactory();
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        var entities = Enumerable.Range(1, 33000).Select(i => new TestDeleteBatchEntity { Id = i }).ToList();

        // Act
        // SQLite 3.32+ MaxParameterLimit = 32766. Headroom 10% -> 29489.
        // 33000 entities with 1 param each -> 2 chunks (29489 and 3511)
        var affected = await gateway.BatchDeleteAsync(entities);

        // Assert
        var totalCommands = _sqliteFactory.CreatedConnections
            .SelectMany(c => c.ExecutedNonQueryTexts)
            .Count(text => text.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, totalCommands);
    }

    [Fact]
    public async Task BatchDeleteAsync_CompositeKey_LargeList_ChunksCorrectly()
    {
        // Arrange
        ResetFactory();
        var gateway = new TableGateway<TestDeleteCompositeEntity, string>(_sqliteContext);
        // 2 params per row. SQLite 3.32+ usableParams = 29489. rowsPerChunk = 29489 / 2 = 14744.
        // 17000 entities should result in 2 chunks (14744 and 2256)
        var entities = Enumerable.Range(1, 17000).Select(i => new TestDeleteCompositeEntity { Key1 = i, Key2 = i })
            .ToList();

        // Act
        await gateway.BatchDeleteAsync(entities);

        // Assert
        var totalCommands = _sqliteFactory.CreatedConnections
            .SelectMany(c => c.ExecutedNonQueryTexts)
            .Count(text => text.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, totalCommands);
    }

    [Fact]
    public void BuildBatchDelete_ReturnsMultipleContainers()
    {
        // Arrange
        ResetFactory();
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        // SQLite 3.32+ usableParams = 29489, rowsPerChunk = 29489.
        // 33000 entities -> 2 containers (29489 and 3511)
        var entities = Enumerable.Range(1, 33000).Select(i => new TestDeleteBatchEntity { Id = i }).ToList();

        // Act
        var containers = gateway.BuildBatchDelete(entities);

        // Assert
        Assert.Equal(2, containers.Count);
    }

    // =========================================================================
    // DeleteAsync(IReadOnlyCollection<TEntity>) — entity list overload
    // =========================================================================

    [Fact]
    public async Task DeleteAsync_EntityList_NullList_Throws()
    {
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await gateway.DeleteAsync((IReadOnlyCollection<TestDeleteBatchEntity>)null!));
    }

    [Fact]
    public async Task DeleteAsync_EntityList_EmptyList_ReturnsZero()
    {
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        var result = await gateway.DeleteAsync(Array.Empty<TestDeleteBatchEntity>());
        Assert.Equal(0, result);
    }

    // -------------------------------------------------------------------------
    // BuildBatchDelete(IEnumerable<TRowID>) — _idColumn == null guard (Core.cs lines 854-855)
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildBatchDelete_IdList_EntityWithoutIdColumn_Throws()
    {
        ResetFactory();
        var gateway = new TableGateway<TestDeleteNoIdEntity, string>(_sqliteContext);

        // _idColumn is null because the entity has no [Id] attribute
        // The id-list overload of BuildBatchDelete checks this and throws
        Assert.Throws<InvalidOperationException>(
            () => gateway.BuildBatchDelete(new[] { "key1" }));
    }

    [Fact]
    public void DeleteAsync_EntityList_ProducesDeleteSql()
    {
        var gateway = new TableGateway<TestDeleteBatchEntity, int>(_sqliteContext);
        var entities = new[]
        {
            new TestDeleteBatchEntity { Id = 1 },
            new TestDeleteBatchEntity { Id = 2 }
        };
        // Routes through BuildBatchDelete(entities) — verifies overload resolves
        var containers = gateway.BuildBatchDelete((IReadOnlyCollection<TestDeleteBatchEntity>)entities);
        Assert.Single(containers);
        Assert.Contains("DELETE", containers[0].Query.ToString());
    }
}
