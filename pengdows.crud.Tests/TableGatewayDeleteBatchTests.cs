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
}