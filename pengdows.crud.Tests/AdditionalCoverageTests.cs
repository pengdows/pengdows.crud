#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class AdditionalCoverageTests : SqlLiteContextTestBase
{
    [Table("test_coverage")]
    private class SimpleCoverageEntity
    {
        [Id]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = "";

        [Column("value", DbType.Int32)]
        public int Value { get; set; }
    }

    [Fact]
    public void DatabaseContext_ConnectionString_ReturnsValue()
    {
        // Act
        var connectionString = Context.ConnectionString;

        // Assert
        Assert.NotNull(connectionString);
    }

    [Fact]
    public void CreatedByAttribute_CanBeInstantiated()
    {
        // Act
        var attribute = new CreatedByAttribute();

        // Assert
        Assert.NotNull(attribute);
    }

    [Fact]
    public void CreatedOnAttribute_CanBeInstantiated()
    {
        // Act
        var attribute = new CreatedOnAttribute();

        // Assert
        Assert.NotNull(attribute);
    }

    [Fact]
    public void TrackedConnection_ChangeDatabase_ThrowsNotImplemented()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = factory.CreateConnection();
        var tracked = new TrackedConnection(connection);

        // Act & Assert - Should throw NotImplementedException
        Assert.Throws<NotImplementedException>(() => tracked.ChangeDatabase("testDb"));
    }

    [Fact]
    public async Task SqlContainer_Clone_CreatesIndependentCopy()
    {
        // Arrange
        var container = Context.CreateSqlContainer("SELECT 1");
        container.AddParameterWithValue("test", DbType.String, "value");

        // Act
        var clone = container.Clone();

        // Assert
        Assert.NotSame(container, clone);
        Assert.Equal(container.Query.ToString(), clone.Query.ToString());
        Assert.Equal(container.ParameterCount, clone.ParameterCount);
    }

    [Fact]
    public async Task EntityHelper_LoadOperations_ExerciseValueExtractors()
    {
        // This test exercises the compiled value extractor and coercer methods
        // Arrange
        TypeMap.Register<SimpleCoverageEntity>();
        var helper = new EntityHelper<SimpleCoverageEntity, long>(Context, AuditValueResolver);

        // Act - Create SQL that returns data requiring type coercion
        var container = Context.CreateSqlContainer();
        container.Query.Append("SELECT 1 as id, 'test' as name, 42 as value");

        var result = await helper.LoadSingleAsync(container);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1L, result.Id);
        Assert.Equal("test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void EntityHelper_BuildUpsert_ExercisesUpsertLogic()
    {
        // This exercises the upsert key resolution methods
        // Arrange
        var helper = new EntityHelper<SimpleCoverageEntity, long>(Context, AuditValueResolver);
        var entity = new SimpleCoverageEntity { Name = "Test", Value = 100 };

        // Act
        var container = helper.BuildUpsert(entity);

        // Assert
        Assert.NotNull(container);
        var sql = container.Query.ToString();
        Assert.Contains("INSERT", sql.ToUpperInvariant());
    }

    [Fact]
    public void DatabaseContext_CreateParameters_WorksCorrectly()
    {
        // Test parameter creation methods
        // Arrange & Act
        var param1 = Context.CreateDbParameter(DbType.String, "test");
        var param2 = Context.CreateDbParameter("named", DbType.Int32, 42);

        // Assert
        Assert.NotNull(param1);
        Assert.NotNull(param2);
        Assert.Equal(DbType.String, param1.DbType);
        Assert.Equal("test", param1.Value);
        Assert.Equal("named", param2.ParameterName);
        Assert.Equal(42, param2.Value);
    }

    [Fact]
    public void TransactionContext_Properties_ReturnCorrectValues()
    {
        // Test transaction context property access
        // Arrange
        using var transaction = Context.BeginTransaction();

        // Act & Assert
        Assert.Equal(Context.ConnectionString, transaction.ConnectionString);
        Assert.False(transaction.WasCommitted);
        Assert.False(transaction.WasRolledBack);
    }

    [Fact]
    public async Task SqlContainer_ExecuteScalarWriteAsync_WorksCorrectly()
    {
        // Test the ExecuteScalarWriteAsync method
        // Arrange
        var container = Context.CreateSqlContainer();
        container.Query.Append("SELECT 42");

        // Act
        var result = await container.ExecuteScalarWriteAsync<int>();

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void DatabaseContext_CreateSqlContainer_CreatesContainer()
    {
        // Test SqlContainer creation
        // Act
        var container = Context.CreateSqlContainer("SELECT 1");

        // Assert
        Assert.NotNull(container);
        Assert.Equal("SELECT 1", container.Query.ToString());
    }

    [Fact]
    public void WrappedConnection_GetLock_ReturnsLock()
    {
        // Test TrackedConnection lock functionality
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = factory.CreateConnection();
        var tracked = new TrackedConnection(connection);

        // Act
        var lockObj = tracked.GetLock();

        // Assert
        Assert.NotNull(lockObj);
    }
}
