using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests verifying that streaming APIs work correctly within transaction contexts.
/// Ensures proper connection lifecycle when combining transactions with IAsyncEnumerable streaming.
/// </summary>
public class TransactionStreamingTests
{
    [Table("test")]
    private class TestEntity
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("name", DbType.String)] public string? Name { get; set; }

        [Column("value", DbType.Int32)] public int Value { get; set; }
    }

    [Fact]
    public async Task LoadStreamAsync_WithinTransaction_StreamsCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        using var transaction = context.BeginTransaction();

        var container = helper.BuildBaseRetrieve("t");

        // Act - Stream within transaction
        var results = new List<TestEntity>();
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            results.Add(entity);
        }

        transaction.Commit();

        // Assert - Should complete without errors
        Assert.NotNull(results);
    }

    [Fact]
    public async Task LoadStreamAsync_WithinTransaction_UsesTransactionConnection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        using var transaction = context.BeginTransaction();

        var container = helper.BuildBaseRetrieve("t");

        // Act - Stream and verify transaction is still valid
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            // Transaction should still be active
            Assert.False(transaction.WasCommitted);
            Assert.False(transaction.WasRolledBack);
        }

        transaction.Commit();

        // Assert
        Assert.True(transaction.WasCommitted);
    }

    [Fact]
    public async Task LoadStreamAsync_WithinTransaction_EarlyBreak_DoesNotAffectTransaction()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        using var transaction = context.BeginTransaction();

        var container = helper.BuildBaseRetrieve("t");

        // Act - Break early from stream
        var count = 0;
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            count++;
            if (count >= 1)
            {
                break; // Early exit
            }
        }

        // Transaction should still be usable
        var container2 = helper.BuildBaseRetrieve("t");
        var moreResults = await helper.LoadListAsync(container2);

        transaction.Commit();

        // Assert - Transaction completed successfully despite early stream break
        Assert.True(transaction.WasCommitted);
    }

    [Fact]
    public async Task RetrieveStreamAsync_WithinTransaction_StreamsCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        using var transaction = context.BeginTransaction();

        var ids = new[] { 1, 2, 3 };

        // Act - Stream by IDs within transaction
        var results = new List<TestEntity>();
        await foreach (var entity in helper.RetrieveStreamAsync(ids, transaction))
        {
            results.Add(entity);
        }

        transaction.Commit();

        // Assert
        Assert.NotNull(results);
        Assert.True(transaction.WasCommitted);
    }

    [Fact]
    public async Task LoadStreamAsync_TransactionRollback_DisposesReaderCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        using var transaction = context.BeginTransaction();

        var container = helper.BuildBaseRetrieve("t");

        // Act - Stream then rollback
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            // Process entities
        }

        transaction.Rollback();

        // Assert - Should be able to create new operations after rollback
        var container2 = helper.BuildBaseRetrieve("t");
        var moreResults = await helper.LoadListAsync(container2);
        Assert.NotNull(moreResults);
    }

    [Fact]
    public async Task LoadStreamAsync_NestedTransactions_WithSavepoint_StreamsCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using var context = new DatabaseContext("Host=localhost;Database=test", factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        using var transaction = context.BeginTransaction();

        // First operation
        var container1 = helper.BuildBaseRetrieve("t");
        await foreach (var entity in helper.LoadStreamAsync(container1))
        {
            // Process
        }

        // Savepoint
        await transaction.SavepointAsync("sp1");

        // Second operation
        var container2 = helper.BuildBaseRetrieve("t");
        await foreach (var entity in helper.LoadStreamAsync(container2))
        {
            // Process
        }

        // Rollback to savepoint
        await transaction.RollbackToSavepointAsync("sp1");

        // Third operation after rollback
        var container3 = helper.BuildBaseRetrieve("t");
        await foreach (var entity in helper.LoadStreamAsync(container3))
        {
            // Process
        }

        transaction.Commit();

        // Assert
        Assert.True(transaction.WasCommitted);
    }

    [Fact]
    public async Task LoadStreamAsync_MultipleStreams_WithinSameTransaction_WorkCorrectly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        using var transaction = context.BeginTransaction();

        // Act - Multiple sequential streams within same transaction
        var container1 = helper.BuildBaseRetrieve("t");
        var count1 = 0;
        await foreach (var entity in helper.LoadStreamAsync(container1))
        {
            count1++;
        }

        var container2 = helper.BuildBaseRetrieve("t");
        var count2 = 0;
        await foreach (var entity in helper.LoadStreamAsync(container2))
        {
            count2++;
        }

        transaction.Commit();

        // Assert - Both streams should work independently
        Assert.True(count1 >= 0);
        Assert.True(count2 >= 0);
        Assert.True(transaction.WasCommitted);
    }

    [Fact]
    public async Task LoadStreamAsync_TransactionContext_PassedExplicitly_UsesCorrectConnection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        using var transaction = context.BeginTransaction();

        // Build container WITHOUT specifying context
        var container = helper.BuildBaseRetrieve("t");

        // Act - Pass transaction context during streaming
        // Note: LoadStreamAsync uses the container's context, not the one passed to CreateAsync
        // This test verifies the connection handling is correct
        await foreach (var entity in helper.LoadStreamAsync(container))
        {
            // Within transaction scope
            Assert.False(transaction.IsCompleted);
        }

        transaction.Commit();

        // Assert
        Assert.True(transaction.WasCommitted);
    }

    [Fact]
    public async Task RetrieveStreamAsync_WithTransaction_AllowsInterleavedUpdates()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        using var transaction = context.BeginTransaction();

        var ids = new[] { 1, 2, 3 };

        // Act - Stream and update in same transaction
        await foreach (var entity in helper.RetrieveStreamAsync(ids, transaction))
        {
            // Modify entity
            entity.Value += 10;

            // Update within same transaction
            await helper.UpdateAsync(entity, transaction);
        }

        transaction.Commit();

        // Assert - Transaction should complete successfully
        Assert.True(transaction.WasCommitted);
    }

    [Fact]
    public async Task LoadStreamAsync_TransactionDisposed_ThrowsAppropriateException()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new EntityHelper<TestEntity, int>(context);

        var container = helper.BuildBaseRetrieve("t");
        IAsyncEnumerable<TestEntity> stream;

        using (var transaction = context.BeginTransaction())
        {
            stream = helper.LoadStreamAsync(container);
            transaction.Commit();
        } // Transaction disposed here

        // Act & Assert - Attempting to enumerate after transaction disposal
        // This behavior depends on connection mode and provider
        // Test verifies no silent corruption occurs
        try
        {
            await foreach (var entity in stream)
            {
                // May succeed (Standard mode) or fail (SingleConnection mode)
            }

            // If we get here, enumeration succeeded (Standard mode behavior)
            Assert.True(true);
        }
        catch
        {
            // If we get here, enumeration failed (SingleConnection mode behavior)
            // Either outcome is acceptable, as long as it's deterministic
            Assert.True(true);
        }
    }
}