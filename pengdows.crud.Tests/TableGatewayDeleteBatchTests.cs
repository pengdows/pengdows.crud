#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
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
        var ids = Enumerable.Range(1, 1000).ToList();

        // Act
        // MaxParameterLimit = 999. Headroom 10% -> 899.
        // 1000 IDs should result in 2 chunks (899 and 101)
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
        var entities = Enumerable.Range(1, 1000).Select(i => new TestDeleteBatchEntity { Id = i }).ToList();

        // Act
        // For single-ID entities, paramsPerRow = 1
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
        // 2 params per row. Max 899 usable. rowsPerChunk = 899 / 2 = 449.
        // 500 entities should result in 2 chunks (449 and 51)
        var entities = Enumerable.Range(1, 500).Select(i => new TestDeleteCompositeEntity { Key1 = i, Key2 = i })
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
        var entities = Enumerable.Range(1, 1000).Select(i => new TestDeleteBatchEntity { Id = i }).ToList();

        // Act
        var containers = gateway.BuildBatchDelete(entities);

        // Assert
        Assert.Equal(2, containers.Count);
    }
}