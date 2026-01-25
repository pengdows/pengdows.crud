#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperIdPopulationTests
{
    private readonly TypeMapRegistry _typeMap;

    public EntityHelperIdPopulationTests()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<TestEntityWithAutoId>();
        _typeMap.Register<TestEntityWithWritableId>();
    }

    [Fact]
    public async Task CreateAsync_Should_Populate_Generated_Id_For_Auto_Increment_Column()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);

        // Set up ID population BEFORE creating DatabaseContext so the initialization 
        // connection is properly configured for database detection queries
        factory.SetIdPopulationResult(42, 1);

        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);
        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        Assert.Equal(42, entity.Id); // Verify ID was populated by PopulateGeneratedIdAsync
    }

    [Fact]
    public async Task CreateAsync_Should_Not_Populate_Id_For_Writable_Id_Column()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithWritableId, int>(context);

        factory.SetNonQueryResult(1);

        var entity = new TestEntityWithWritableId { Id = 100, Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        Assert.Equal(100, entity.Id); // ID should remain unchanged (not populated)
    }

    [Fact]
    public void CreateAsync_Should_Throw_For_Entity_Without_Id_Column()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("test", factory, _typeMap);

        // Assert: constructing helper should fail due to missing [Id]/[PrimaryKey]
        Assert.Throws<InvalidOperationException>(() => new EntityHelper<TestEntityWithoutId, int>(context));
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Dialect_With_Returning_Populates_Id()
    {
        // Arrange - Use a dialect with RETURNING to populate ID (Sqlite)
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        factory.SetNonQueryResult(1);
        factory.SetScalarResult(42);

        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        // ID should be populated via RETURNING
        Assert.Equal(42, entity.Id);
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Null_Generated_Id_Result()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        factory.SetNonQueryResult(1);
        factory.SetScalarResult(null); // Simulate null result from LASTVAL()

        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        Assert.Equal(0, entity.Id); // Should handle null gracefully
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Database_Exception_During_Id_Population()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetException(new InvalidOperationException("Database connection lost"));

        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        factory.SetNonQueryResult(1); // Insert succeeds

        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => helper.CreateAsync(entity));
    }

    [Fact]
    public async Task CreateAsync_Should_Not_Attempt_Id_Population_When_Insert_Fails()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Unknown", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        factory.SetNonQueryResult(0); // Insert fails - no rows affected

        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.False(result);
        Assert.Equal(0, entity.Id); // ID should not be populated when insert fails
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Multiple_Rows_Affected_Gracefully()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Unknown", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context);

        factory.SetNonQueryResult(2); // Multiple rows affected (unusual but possible)

        var entity = new TestEntityWithAutoId { Name = "Test Entity" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.False(result); // Should return false when not exactly 1 row affected
        Assert.Equal(0, entity.Id); // ID should not be populated
    }

    // Test entities for different ID scenarios
    [Table("test_auto_id")]
    public class TestEntityWithAutoId
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    [Table("test_writable_id")]
    public class TestEntityWithWritableId
    {
        [Id(true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    [Table("test_no_id")]
    public class TestEntityWithoutId
    {
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}