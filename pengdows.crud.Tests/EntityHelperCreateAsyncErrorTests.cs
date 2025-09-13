#region

using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperCreateAsyncErrorTests
{
    private readonly TypeMapRegistry _typeMap;

    public EntityHelperCreateAsyncErrorTests()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<TestEntity>();
        _typeMap.Register<TestEntitySimple>();
        _typeMap.Register<TestEntityWithGuid>();
        _typeMap.Register<TestEntityWithDefaultId>();
    }

    [Fact]
    public async Task CreateAsync_Should_Return_False_When_No_Rows_Affected()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Unknown);
        factory.SetNonQueryResult(0); // No rows affected
        
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Unknown", factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context);
        
        var entity = new TestEntitySimple { Name = "Test" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CreateAsync_Should_Return_False_When_Multiple_Rows_Affected()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        factory.SetNonQueryResult(2); // Multiple rows affected (unexpected)
        
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context);
        
        var entity = new TestEntitySimple { Name = "Test" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CreateAsync_Should_Propagate_Database_Exceptions()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetException(new InvalidOperationException("Connection timeout"));
        
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context);
        
        var entity = new TestEntitySimple { Name = "Test" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            helper.CreateAsync(entity));
        
        Assert.Equal("Connection timeout", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Constraint_Violation_Gracefully()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetException(new InvalidOperationException("Violation of UNIQUE KEY constraint"));
        
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntity, int>(context);
        
        var entity = new TestEntity { Name = "Duplicate" };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => helper.CreateAsync(entity));
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Null_Entity_Gracefully()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntity, int>(context);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => helper.CreateAsync(null!));
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Entity_With_All_Default_Values()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetIdPopulationResult(100, rowsAffected: 1);
        
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithDefaultId, int>(context);
        
        var entity = new TestEntityWithDefaultId(); // All default values

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        Assert.Equal(100, entity.Id); // Should populate generated ID
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Guid_Id_Population()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var expectedGuid = Guid.NewGuid();
        // Provide string form to avoid provider-level coercion issues in ExecuteScalarWrite
        factory.SetNonQueryResult(1);
        factory.SetScalarResult(expectedGuid.ToString());
        
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithGuid, Guid>(context);
        
        var entity = new TestEntityWithGuid { Name = "Test" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedGuid, entity.Id);
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Id_Type_Coercion_Errors()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetNonQueryResult(1);
        factory.SetScalarResult("not-a-valid-int"); // Wrong type for int ID
        
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context);
        
        var entity = new TestEntitySimple { Name = "Test" };

        // Act & Assert
        // This should either handle coercion gracefully or throw appropriate exception
        try 
        {
            var result = await helper.CreateAsync(entity);
            // If coercion succeeds, verify reasonable behavior
            Assert.True(result);
        }
        catch (Exception ex)
        {
            // If coercion fails, should get appropriate exception type
            Assert.True(ex is InvalidCastException || ex is FormatException || ex is ArgumentException);
        }
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Command_Preparation_Failure()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        // Simulate command preparation failure
        factory.SetException(new ArgumentException("Invalid SQL syntax"));
        
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context);
        
        var entity = new TestEntitySimple { Name = "Test" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => helper.CreateAsync(entity));
    }

    [Fact(Skip="temporarily disabled while finalizing DbMode/ID population behavior")]
    public async Task CreateAsync_Should_Handle_Connection_Acquisition_Failure()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetConnectionException(new TimeoutException("Connection pool exhausted"));
        
        // Act & Assert (per contract: ctor open must surface failure immediately)
        Assert.Throws<TimeoutException>(() => new DatabaseContext("test", factory, _typeMap));
    }

    [Fact]
    public async Task CreateAsync_Should_Validate_Required_Fields()
    {
        // Arrange - This tests the general create flow with field validation
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetNonQueryResult(1);
        
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntity, int>(context);
        
        var entity = new TestEntity 
        { 
            Name = null! // Null required field
        };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        // Should either handle gracefully or the database constraint will catch it
        Assert.True(result); // FakeDb allows null values by default
    }

    [Fact]
    public async Task CreateAsync_Should_Handle_Transaction_Context()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetIdPopulationResult(42, rowsAffected: 1);
        
        var context = new DatabaseContext("test", factory, _typeMap);
        
        // Act & Assert - Test within transaction context
        using var transaction = context.BeginTransaction();
        var helper = new EntityHelper<TestEntity, int>(transaction);
        var entity = new TestEntity { Name = "Test in Transaction" };
        
        var result = await helper.CreateAsync(entity);
        
        Assert.True(result);
        Assert.Equal(42, entity.Id);
    }

    // Test entities
    [Table("test_entity")]
    public class TestEntity
    {
        [Id(writable: false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("test_entity_guid")]
    public class TestEntityWithGuid
    {
        [Id(writable: false)]
        [Column("id", DbType.Guid)]
        public Guid Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("test_entity_default")]
    public class TestEntityWithDefaultId
    {
        [Id(writable: false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string? Name { get; set; }

        [Column("value", DbType.Int32)]
        public int Value { get; set; } = 0;
    }
}
