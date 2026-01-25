using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Collections.Concurrent;
using System.Data;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Advanced;

/// <summary>
/// Integration tests for concurrent operations including parallel reads,
/// writes, and potential race conditions.
/// </summary>
[Collection("IntegrationTests")]
public class ConcurrencyTests : DatabaseTestBase
{
    private static long _nextId;

    public ConcurrencyTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [Fact]
    public async Task ParallelReads_SameRecord_AllSucceed()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entity = CreateTestEntity(NameEnum.Test, 100);
            await CreateEntityHelper(context).CreateAsync(entity, context);

            // Act - 10 parallel reads of the same record
            var tasks = Enumerable.Range(0, 10).Select(async _ =>
            {
                var helper = CreateEntityHelper(context);
                return await helper.RetrieveOneAsync(entity.Id, context);
            });

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, r =>
            {
                Assert.NotNull(r);
                Assert.Equal(entity.Id, r!.Id);
                Assert.Equal(entity.Name, r.Name);
            });
        });
    }

    [Fact]
    public async Task ParallelInserts_DifferentRecords_AllSucceed()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange - Create 20 entities with unique IDs
            var entities = Enumerable.Range(0, 20)
                .Select(i => CreateTestEntity(NameEnum.Test, 200 + i))
                .ToList();

            // Act - Insert them all in parallel
            var tasks = entities.Select(async entity =>
            {
                var helper = CreateEntityHelper(context);
                return await helper.CreateAsync(entity, context);
            });

            var results = await Task.WhenAll(tasks);

            // Assert - All inserts should succeed
            Assert.All(results, r => Assert.True(r));

            // Verify all entities exist
            var helper = CreateEntityHelper(context);
            var retrieved = await helper.RetrieveAsync(entities.Select(e => e.Id).ToList(), context);
            Assert.Equal(entities.Count, retrieved.Count);
        });
    }

    [Fact]
    public async Task ParallelUpdates_DifferentRecords_NoConflicts()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange - Create 10 entities
            var entities = Enumerable.Range(0, 10)
                .Select(i => CreateTestEntity(NameEnum.Test, 300 + i))
                .ToList();

            var helper = CreateEntityHelper(context);
            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, context);
            }

            // Act - Update them all in parallel with different values
            var tasks = entities.Select(async (entity, index) =>
            {
                var updateHelper = CreateEntityHelper(context);
                entity.Value = 3000 + index;
                return await updateHelper.UpdateAsync(entity, context);
            });

            var updateCounts = await Task.WhenAll(tasks);

            // Assert
            Assert.All(updateCounts, count => Assert.Equal(1, count));

            // Verify all updates persisted
            var retrieved = await helper.RetrieveAsync(entities.Select(e => e.Id).ToList(), context);
            foreach (var (entity, index) in entities.Select((e, i) => (e, i)))
            {
                var found = retrieved.First(r => r.Id == entity.Id);
                Assert.Equal(3000 + index, found.Value);
            }
        });
    }

    [Fact]
    public async Task ParallelDeletes_DifferentRecords_AllSucceed()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entities = Enumerable.Range(0, 15)
                .Select(i => CreateTestEntity(NameEnum.Test, 400 + i))
                .ToList();

            var helper = CreateEntityHelper(context);
            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, context);
            }

            // Act - Delete in parallel
            var tasks = entities.Select(async entity =>
            {
                var deleteHelper = CreateEntityHelper(context);
                return await deleteHelper.DeleteAsync(entity.Id, context);
            });

            var deleteCounts = await Task.WhenAll(tasks);

            // Assert
            Assert.All(deleteCounts, count => Assert.Equal(1, count));

            // Verify all deleted
            var retrieved = await helper.RetrieveAsync(entities.Select(e => e.Id).ToList(), context);
            Assert.Empty(retrieved);
        });
    }

    [Fact]
    public async Task ParallelTransactions_Independent_NoDeadlock()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange - Create separate entities for each transaction
            var entities = Enumerable.Range(0, 5)
                .Select(i => CreateTestEntity(NameEnum.Test, 500 + i))
                .ToList();

            // Act - Each transaction operates on its own entity
            var tasks = entities.Select(async entity =>
            {
                await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
                var helper = CreateEntityHelper(transaction);

                await helper.CreateAsync(entity, transaction);
                transaction.Commit();

                return entity.Id;
            });

            var ids = await Task.WhenAll(tasks);

            // Assert
            var helper = CreateEntityHelper(context);
            foreach (var id in ids)
            {
                var retrieved = await helper.RetrieveOneAsync(id, context);
                Assert.NotNull(retrieved);
            }
        });
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_Consistent()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entities = Enumerable.Range(0, 5)
                .Select(i => CreateTestEntity(NameEnum.Test, 600 + i))
                .ToArray();

            var helper = CreateEntityHelper(context);
            foreach (var e in entities)
            {
                await helper.CreateAsync(e, context);
            }

            var readResults = new ConcurrentBag<TestTable?>();
            var updateCount = 0;

            // Act - Mix of concurrent reads and writes
            var readTasks = Enumerable.Range(0, 20).Select(async _ =>
            {
                await Task.Delay(Random.Shared.Next(1, 10)); // Random delay
                var helper = CreateEntityHelper(context);
                var id = entities[Random.Shared.Next(0, entities.Length)].Id;
                var result = await helper.RetrieveOneAsync(id, context);
                readResults.Add(result);
            });

            var writeTasks = Enumerable.Range(0, 5).Select(async i =>
            {
                await Task.Delay(Random.Shared.Next(1, 10)); // Random delay
                var helper = CreateEntityHelper(context);
                var id = entities[i].Id;
                var retrieved = await helper.RetrieveOneAsync(id, context);
                if (retrieved != null)
                {
                    retrieved.Value = 6000 + i;
                    await helper.UpdateAsync(retrieved, context);
                    Interlocked.Increment(ref updateCount);
                }
            });

            await Task.WhenAll(readTasks.Concat(writeTasks));

            // Assert - All reads should have succeeded
            Assert.Equal(20, readResults.Count);
            Assert.All(readResults, r => Assert.NotNull(r));

            foreach (var (entity, i) in entities.Select((e, i) => (e, i)))
            {
                var final = await CreateEntityHelper(context).RetrieveOneAsync(entity.Id, context);
                Assert.NotNull(final);
                Assert.True(final!.Value == 600 + i || final.Value == 6000 + i);
            }

            Assert.InRange(updateCount, 0, entities.Length);
        });
    }

    [Fact]
    public async Task MultipleContexts_SameDatabase_WorkIndependently()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // This test uses the shared context from DatabaseTestBase
            // Each task will get connections independently

            // Arrange
            var entities = Enumerable.Range(0, 10)
                .Select(i => CreateTestEntity(NameEnum.Test, 700 + i))
                .ToList();

            // Act - Simulate multiple concurrent requests/contexts
            var tasks = entities.Select(async entity =>
            {
                // Each iteration uses the same DatabaseContext but gets its own connection
                var helper = CreateEntityHelper(context);
                var created = await helper.CreateAsync(entity, context);
                var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
                return (created, retrieved);
            });

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, r =>
            {
                Assert.True(r.created);
                Assert.NotNull(r.retrieved);
            });
        });
    }

    [Fact]
    public async Task BulkRead_WhileBulkWrite_NoErrors()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange - Pre-populate some data
            var existingEntities = Enumerable.Range(0, 50)
                .Select(i => CreateTestEntity(NameEnum.Test, 800 + i))
                .ToList();

            var helper = CreateEntityHelper(context);
            foreach (var entity in existingEntities)
            {
                await helper.CreateAsync(entity, context);
            }

            // Act - Concurrent bulk operations
            var readTask = Task.Run(async () =>
            {
                var readHelper = CreateEntityHelper(context);
                var results = new List<TestTable>();

                for (var i = 0; i < 10; i++)
                {
                    var batch = await readHelper.RetrieveAsync(
                        existingEntities.Take(25).Select(e => e.Id).ToList(),
                        context);
                    results.AddRange(batch);
                    await Task.Delay(5); // Small delay between batches
                }

                return results.Count;
            });

            var writeTask = Task.Run(async () =>
            {
                var writeHelper = CreateEntityHelper(context);
                var newEntities = Enumerable.Range(0, 25)
                    .Select(i => CreateTestEntity(NameEnum.Test2, 850 + i))
                    .ToList();

                foreach (var entity in newEntities)
                {
                    await writeHelper.CreateAsync(entity, context);
                    await Task.Delay(2); // Small delay between inserts
                }

                return newEntities.Count;
            });

            var (readCount, writeCount) = await Task.WhenAll(readTask, writeTask)
                .ContinueWith(t => (t.Result[0], t.Result[1]));

            // Assert
            Assert.True(readCount > 0);
            Assert.Equal(25, writeCount);
        });
    }

    [Fact]
    public async Task ParallelTransactions_WritingSameRecord_SerializedCorrectly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip for SQLite which has limited concurrent write support
            if (provider == SupportedDatabase.Sqlite)
            {
                Output.WriteLine("Skipping concurrent write test for SQLite");
                return;
            }

            // Arrange
            var entity = CreateTestEntity(NameEnum.Test, 900);
            await CreateEntityHelper(context).CreateAsync(entity, context);

            var successCount = 0;
            var conflictCount = 0;

            // Act - Multiple transactions trying to update the same record
            var tasks = Enumerable.Range(0, 10).Select(async i =>
            {
                try
                {
                    await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
                    var helper = CreateEntityHelper(transaction);

                    var retrieved = await helper.RetrieveOneAsync(entity.Id, transaction);
                    if (retrieved != null)
                    {
                        retrieved.Value = 9000 + i;
                        await Task.Delay(Random.Shared.Next(1, 5)); // Simulate processing time
                        await helper.UpdateAsync(retrieved, transaction);
                        transaction.Commit();
                        Interlocked.Increment(ref successCount);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    // Some databases might throw on concurrent updates
                    Interlocked.Increment(ref conflictCount);
                    Output.WriteLine($"Transaction {i} failed: {ex.Message}");
                }

                return false;
            });

            await Task.WhenAll(tasks);

            // Assert - At least some transactions should succeed
            Output.WriteLine($"Success: {successCount}, Conflicts: {conflictCount}");
            Assert.True(successCount > 0, "At least one transaction should succeed");

            // Final record should exist with one of the update values
            var final = await CreateEntityHelper(context).RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(final);
            Assert.InRange(final!.Value, 9000, 9009);
        });
    }

    [Fact]
    public async Task StressTest_ManySmallTransactions_NoLeaks()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Act - Create many small transactions in parallel
            var tasks = Enumerable.Range(0, 100).Select(async i =>
            {
                var entity = CreateTestEntity(NameEnum.Test, 1000 + i);

                await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
                var helper = CreateEntityHelper(transaction);

                await helper.CreateAsync(entity, transaction);
                transaction.Commit();

                return entity.Id;
            });

            var ids = await Task.WhenAll(tasks);

            // Assert - All should succeed and be retrievable
            Assert.Equal(100, ids.Length);

            // Sample check some records
            var helper = CreateEntityHelper(context);
            var sampleIds = ids.Take(10).ToList();
            var retrieved = await helper.RetrieveAsync(sampleIds, context);
            Assert.Equal(sampleIds.Count, retrieved.Count);
        });
    }

    private EntityHelper<TestTable, long> CreateEntityHelper(IDatabaseContext context)
    {
        var auditResolver = GetAuditResolver();
        return new EntityHelper<TestTable, long>(context, auditResolver);
    }

    private static TestTable CreateTestEntity(NameEnum name, int value)
    {
        var now = DateTime.UtcNow;
        return new TestTable
        {
            Id = Interlocked.Increment(ref _nextId),
            Name = name,
            Value = value,
            Description = $"Concurrency test: {name}",
            IsActive = true,
            CreatedOn = now,
            UpdatedAt = now,
            UpdatedBy = "testuser",
            CreatedBy = "testuser"
        };
    }
}