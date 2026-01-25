using System;
using System.Data;
using System.Threading.Tasks;
using Xunit;
using pengdows.crud.fakeDb;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.attributes;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests to improve coverage of EntityHelper edge cases.
/// Targets uncovered paths in CreateAsync, PopulateGeneratedIdAsync, and other low-coverage methods.
/// </summary>
public class EntityHelperEdgeCaseTests
{
    #region Test Entities

    [Table("test_entities")]
    public class TestEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("non_writable_entities")]
    public class NonWritableIdEntity
    {
        [Id(writable: false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("writable_id_entities")]
    public class WritableIdEntity
    {
        [Id(writable: true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    #endregion

    #region CreateAsync Edge Cases

    [Fact]
    public async Task CreateAsync_WithNullEntity_ThrowsArgumentNullException()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => helper.CreateAsync(null!));
    }

    [Fact]
    public async Task CreateAsync_WithNonWritableId_PopulatesGeneratedId()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var primaryConnection = new fakeDbConnection();

        // Set up ID retrieval - SQLite uses SELECT last_insert_rowid()
        primaryConnection.EnqueueNonQueryResult(1); // INSERT returns 1 row affected
        primaryConnection.ScalarResultsByCommand["SELECT last_insert_rowid()"] = 42;
        primaryConnection.EnqueueScalarResult(42); // Fallback scalar result

        factory.Connections.Add(primaryConnection);

        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<NonWritableIdEntity, int>(context);
        var entity = new NonWritableIdEntity { Name = "Test" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        Assert.Equal(42, entity.Id);
    }

    [Fact]
    public async Task CreateAsync_WithWritableId_PreservesProvidedId()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var primaryConnection = new fakeDbConnection();
        primaryConnection.EnqueueNonQueryResult(1); // INSERT returns 1 row affected

        factory.Connections.Add(primaryConnection);

        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<WritableIdEntity, int>(context);
        var entity = new WritableIdEntity { Id = 100, Name = "Test" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.True(result);
        Assert.Equal(100, entity.Id); // ID should remain unchanged
    }

    [Fact]
    public async Task CreateAsync_WithZeroRowsAffected_ReturnsFalse()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var primaryConnection = new fakeDbConnection();
        primaryConnection.EnqueueNonQueryResult(0); // INSERT returns 0 rows affected

        factory.Connections.Add(primaryConnection);

        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);
        var entity = new TestEntity { Name = "Test" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CreateAsync_WithMultipleRowsAffected_ReturnsFalse()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var primaryConnection = new fakeDbConnection();
        primaryConnection.EnqueueNonQueryResult(2); // INSERT returns 2 rows affected (unexpected)

        factory.Connections.Add(primaryConnection);

        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);
        var entity = new TestEntity { Name = "Test" };

        // Act
        var result = await helper.CreateAsync(entity);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Transaction Context Edge Cases

    [Fact]
    public void TransactionContext_Rollback_AfterCommit_ThrowsInvalidOperationException()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        using var conn = context.GetConnection(ExecutionType.Write);
        var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);

        // Act
        transaction.Commit();

        // Assert
        Assert.Throws<InvalidOperationException>(() => transaction.Rollback());
    }

    [Fact]
    public void TransactionContext_Commit_AfterRollback_ThrowsInvalidOperationException()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        using var conn = context.GetConnection(ExecutionType.Write);
        var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);

        // Act
        transaction.Rollback();

        // Assert
        Assert.Throws<InvalidOperationException>(() => transaction.Commit());
    }

    [Fact]
    public async Task TransactionContext_DisposeAsync_WithoutCommitOrRollback_RollsBack()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        using var conn = context.GetConnection(ExecutionType.Write);
        ITransactionContext transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);

        // Act
        await transaction.DisposeAsync();

        // Assert
        Assert.True(transaction.WasRolledBack);
        Assert.False(transaction.WasCommitted);
    }

    [Fact]
    public async Task TransactionContext_DisposeAsync_AfterCommit_DoesNotRollback()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        using var conn = context.GetConnection(ExecutionType.Write);
        ITransactionContext transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);

        // Act
        transaction.Commit();
        await transaction.DisposeAsync();

        // Assert
        Assert.True(transaction.WasCommitted);
        Assert.False(transaction.WasRolledBack);
    }

    [Fact]
    public async Task TransactionContext_SavepointAsync_CreatesValidSavepoint()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        using var conn = context.GetConnection(ExecutionType.Write);
        var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);

        // Act & Assert - No exception means savepoint was created successfully
        await transaction.SavepointAsync("sp1");
        transaction.Rollback();
    }

    [Fact]
    public async Task TransactionContext_RollbackToSavepointAsync_WorksCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);

        using var conn = context.GetConnection(ExecutionType.Write);
        var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);

        // Act & Assert - No exception means rollback to savepoint worked
        await transaction.SavepointAsync("sp1");
        await transaction.RollbackToSavepointAsync("sp1");
        transaction.Rollback();
    }

    #endregion
}
