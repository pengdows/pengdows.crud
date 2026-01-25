using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Integration tests for CancellationToken support across EntityHelper, SqlContainer, and TrackedReader.
/// Uses FakeDb with data persistence enabled.
/// </summary>
public class CancellationTokenIntegrationTests : IAsyncLifetime
{
    private EntityHelper<TestEntity, int> _helper = null!;
    private IDatabaseContext _context = null!;
    private TypeMapRegistry _typeMap = null!;

    public Task InitializeAsync()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<TestEntity>();

        // Create factory with data persistence enabled
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { EnableDataPersistence = true };
        _context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, _typeMap);
        _helper = new EntityHelper<TestEntity, int>(_context);

        // Create test table
        using var createTable = _context.CreateSqlContainer(@"
            CREATE TABLE TestEntity (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Value INTEGER
            )
        ");
        createTable.ExecuteNonQueryAsync().Wait();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_context is IAsyncDisposable asyncDisp)
        {
            await asyncDisp.DisposeAsync();
        }
        else if (_context is IDisposable disp)
        {
            disp.Dispose();
        }
    }

    #region EntityHelper CancellationToken Tests

    [Fact]
    public async Task EntityHelper_CreateAsync_WithValidToken_Succeeds()
    {
        // Arrange
        var entity = new TestEntity { Id = 1, Name = "Test", Value = 100 };
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _helper.CreateAsync(entity, _context, cts.Token);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task EntityHelper_CreateAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        // Arrange
        var entity = new TestEntity { Id = 2, Name = "Test", Value = 200 };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => _helper.CreateAsync(entity, _context, cts.Token));
    }

    [Fact]
    public async Task EntityHelper_RetrieveAsync_WithValidToken_Succeeds()
    {
        // Arrange
        var entity = new TestEntity { Id = 3, Name = "Retrieve", Value = 300 };
        await _helper.CreateAsync(entity, _context);
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _helper.RetrieveAsync(new[] { 3 }, _context, cts.Token);

        // Assert
        Assert.Single(result);
        Assert.Equal("Retrieve", result[0].Name);
    }

    [Fact]
    public async Task EntityHelper_RetrieveAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        // Arrange
        var entity = new TestEntity { Id = 4, Name = "Test", Value = 400 };
        await _helper.CreateAsync(entity, _context);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => _helper.RetrieveAsync(new[] { 4 }, _context, cts.Token));
    }

    [Fact]
    public async Task EntityHelper_UpdateAsync_WithValidToken_Succeeds()
    {
        // Arrange
        var entity = new TestEntity { Id = 5, Name = "Original", Value = 500 };
        await _helper.CreateAsync(entity, _context);
        entity.Name = "Updated";
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _helper.UpdateAsync(entity, _context, cts.Token);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task EntityHelper_UpdateAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        // Arrange
        var entity = new TestEntity { Id = 6, Name = "Original", Value = 600 };
        await _helper.CreateAsync(entity, _context);
        entity.Name = "Updated";
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => _helper.UpdateAsync(entity, _context, cts.Token));
    }

    [Fact]
    public async Task EntityHelper_DeleteAsync_WithValidToken_Succeeds()
    {
        // Arrange
        var entity = new TestEntity { Id = 7, Name = "ToDelete", Value = 700 };
        await _helper.CreateAsync(entity, _context);
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _helper.DeleteAsync(7, _context, cts.Token);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task EntityHelper_DeleteAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        // Arrange
        var entity = new TestEntity { Id = 8, Name = "ToDelete", Value = 800 };
        await _helper.CreateAsync(entity, _context);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => _helper.DeleteAsync(8, _context, cts.Token));
    }

    [Fact]
    public async Task EntityHelper_LoadListAsync_WithValidToken_Succeeds()
    {
        // Arrange
        await _helper.CreateAsync(new TestEntity { Id = 9, Name = "Item1", Value = 900 }, _context);
        await _helper.CreateAsync(new TestEntity { Id = 10, Name = "Item2", Value = 1000 }, _context);
        using var container = _context.CreateSqlContainer("SELECT * FROM TestEntity WHERE Id >= 9");
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _helper.LoadListAsync(container, cts.Token);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task EntityHelper_LoadListAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        // Arrange
        using var container = _context.CreateSqlContainer("SELECT * FROM TestEntity");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => _helper.LoadListAsync(container, cts.Token));
    }

    #endregion

    #region SqlContainer CancellationToken Tests

    [Fact]
    public async Task SqlContainer_ExecuteNonQueryAsync_WithValidToken_Succeeds()
    {
        // Arrange
        using var container =
            _context.CreateSqlContainer("INSERT INTO TestEntity (Id, Name, Value) VALUES (11, 'NonQuery', 1100)");
        using var cts = new CancellationTokenSource();

        // Act
        var result = await container.ExecuteNonQueryAsync(CommandType.Text, cts.Token);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task SqlContainer_ExecuteNonQueryAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        // Arrange
        using var container =
            _context.CreateSqlContainer("INSERT INTO TestEntity (Id, Name, Value) VALUES (12, 'Test', 1200)");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            container.ExecuteNonQueryAsync(CommandType.Text, cts.Token));
    }

    [Fact]
    public async Task SqlContainer_ExecuteScalarAsync_WithValidToken_ReturnsValue()
    {
        // Arrange
        await _helper.CreateAsync(new TestEntity { Id = 13, Name = "Scalar", Value = 1300 }, _context);
        using var container = _context.CreateSqlContainer("SELECT Value FROM TestEntity WHERE Id = 13");
        using var cts = new CancellationTokenSource();

        // Act
        var result = await container.ExecuteScalarAsync<int>(CommandType.Text, cts.Token);

        // Assert
        Assert.Equal(1300, result);
    }

    [Fact]
    public async Task SqlContainer_ExecuteScalarAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        // Arrange
        using var container = _context.CreateSqlContainer("SELECT 1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            container.ExecuteScalarAsync<int>(CommandType.Text, cts.Token));
    }

    [Fact]
    public async Task SqlContainer_ExecuteReaderAsync_WithValidToken_ReturnsReader()
    {
        // Arrange
        await _helper.CreateAsync(new TestEntity { Id = 14, Name = "Reader", Value = 1400 }, _context);
        using var container = _context.CreateSqlContainer("SELECT * FROM TestEntity WHERE Id = 14");
        using var cts = new CancellationTokenSource();

        // Act
        await using var reader = await container.ExecuteReaderAsync(CommandType.Text, cts.Token);
        var hasRows = await reader.ReadAsync(cts.Token);

        // Assert
        Assert.True(hasRows);
        Assert.Equal(14, reader.GetInt32(0));
    }

    [Fact]
    public async Task SqlContainer_ExecuteReaderAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        // Arrange
        using var container = _context.CreateSqlContainer("SELECT * FROM TestEntity");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            container.ExecuteReaderAsync(CommandType.Text, cts.Token));
    }

    #endregion

    #region TrackedReader CancellationToken Tests

    [Fact]
    public async Task TrackedReader_ReadAsync_WithValidToken_Succeeds()
    {
        // Arrange
        await _helper.CreateAsync(new TestEntity { Id = 15, Name = "TrackedReader", Value = 1500 }, _context);
        using var container = _context.CreateSqlContainer("SELECT * FROM TestEntity WHERE Id = 15");
        using var cts = new CancellationTokenSource();

        // Act
        await using var reader = await container.ExecuteReaderAsync(CommandType.Text);
        var hasRows = await reader.ReadAsync(cts.Token);

        // Assert
        Assert.True(hasRows);
    }

    [Fact]
    public async Task TrackedReader_MultipleReads_WithValidToken_Succeeds()
    {
        // Arrange
        await _helper.CreateAsync(new TestEntity { Id = 17, Name = "Row1", Value = 1700 }, _context);
        await _helper.CreateAsync(new TestEntity { Id = 18, Name = "Row2", Value = 1800 }, _context);
        await _helper.CreateAsync(new TestEntity { Id = 19, Name = "Row3", Value = 1900 }, _context);
        using var container = _context.CreateSqlContainer("SELECT * FROM TestEntity WHERE Id >= 17 ORDER BY Id");
        using var cts = new CancellationTokenSource();

        // Act
        await using var reader = await container.ExecuteReaderAsync(CommandType.Text);
        var read1 = await reader.ReadAsync(cts.Token);
        var read2 = await reader.ReadAsync(cts.Token);
        var read3 = await reader.ReadAsync(cts.Token);
        var read4 = await reader.ReadAsync(cts.Token); // Should return false

        // Assert
        Assert.True(read1);
        Assert.True(read2);
        Assert.True(read3);
        Assert.False(read4);
    }

    #endregion

    [Table("TestEntity")]
    private class TestEntity
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Column("Value", DbType.Int32)] public int Value { get; set; }
    }
}