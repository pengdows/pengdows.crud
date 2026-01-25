using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Data;
using System.Diagnostics;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Advanced;

/// <summary>
/// Integration tests for bulk operations including large inserts, updates,
/// deletes, and batch processing scenarios.
/// </summary>
[Collection("IntegrationTests")]
public class BulkOperationTests : DatabaseTestBase
{
    private static long _nextId;

    public BulkOperationTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [Fact]
    public async Task BulkInsert_1000Records_AllPersisted()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entities = Enumerable.Range(0, 1000)
                .Select(i => CreateTestEntity(NameEnum.Test, 1000 + i))
                .ToList();

            var sw = Stopwatch.StartNew();

            // Act - Insert in transaction for better performance
            await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
            var helper = CreateEntityHelper(transaction);

            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, transaction);
            }

            transaction.Commit();
            sw.Stop();

            Output.WriteLine($"{provider}: Inserted {entities.Count} records in {sw.ElapsedMilliseconds}ms");

            // Assert
            var retrieved = await CreateEntityHelper(context).RetrieveAsync(
                entities.Select(e => e.Id).ToList(),
                context);

            Assert.Equal(entities.Count, retrieved.Count);
        });
    }

    [Fact]
    public async Task BulkUpdate_500Records_AllUpdated()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange - Create initial data
            var entities = Enumerable.Range(0, 500)
                .Select(i => CreateTestEntity(NameEnum.Test, 2000 + i))
                .ToList();

            var helper = CreateEntityHelper(context);
            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, context);
            }

            // Modify all entities
            foreach (var entity in entities)
            {
                entity.Name = NameEnum.Test2;
                entity.Value += 10000;
            }

            var sw = Stopwatch.StartNew();

            // Act - Bulk update in transaction
            await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
            var txHelper = CreateEntityHelper(transaction);

            var totalUpdated = 0;
            foreach (var entity in entities)
            {
                var count = await txHelper.UpdateAsync(entity, transaction);
                totalUpdated += count;
            }

            transaction.Commit();
            sw.Stop();

            Output.WriteLine($"{provider}: Updated {totalUpdated} records in {sw.ElapsedMilliseconds}ms");

            // Assert
            Assert.Equal(entities.Count, totalUpdated);

            // Verify updates persisted
            var retrieved = await helper.RetrieveAsync(
                entities.Take(10).Select(e => e.Id).ToList(),
                context);

            Assert.All(retrieved, r =>
            {
                Assert.Equal(NameEnum.Test2, r.Name);
                Assert.InRange(r.Value, 12000, 12500);
            });
        });
    }

    [Fact]
    public async Task BulkDelete_SingleCall_DeletesMultipleRecords()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entities = Enumerable.Range(0, 300)
                .Select(i => CreateTestEntity(NameEnum.Test, 3000 + i))
                .ToList();

            var helper = CreateEntityHelper(context);
            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, context);
            }

            var idsToDelete = entities.Select(e => e.Id).ToList();
            var sw = Stopwatch.StartNew();

            // Act - Bulk delete using the list overload
            var deleteCount = await helper.DeleteAsync(idsToDelete, context);
            sw.Stop();

            Output.WriteLine($"{provider}: Deleted {deleteCount} records in {sw.ElapsedMilliseconds}ms");

            // Assert
            Assert.Equal(entities.Count, deleteCount);

            var remaining = await helper.RetrieveAsync(idsToDelete, context);
            Assert.Empty(remaining);
        });
    }

    [Fact]
    public async Task BulkRetrieve_1000Ids_ReturnsAllMatching()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // SQLite has a parameter limit of 999, so use fewer records for it
            var recordCount = provider == SupportedDatabase.Sqlite ? 900 : 1000;

            // Arrange - Create records
            var entities = Enumerable.Range(0, recordCount)
                .Select(i => CreateTestEntity(NameEnum.Test, 4000 + i))
                .ToList();

            var helper = CreateEntityHelper(context);
            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, context);
            }

            var idsToRetrieve = entities.Select(e => e.Id).ToList();
            var sw = Stopwatch.StartNew();

            // Act - Bulk retrieve
            var retrieved = await helper.RetrieveAsync(idsToRetrieve, context);
            sw.Stop();

            Output.WriteLine($"{provider}: Retrieved {retrieved.Count} records in {sw.ElapsedMilliseconds}ms");

            // Assert
            Assert.Equal(entities.Count, retrieved.Count);
            Assert.All(retrieved, r => Assert.Contains(r.Id, idsToRetrieve));
        });
    }

    [Fact]
    public async Task BatchProcessing_ChunksOf100_ProcessesAllRecords()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange - Create 500 records
            var entities = Enumerable.Range(0, 500)
                .Select(i => CreateTestEntity(NameEnum.Test, 5000 + i))
                .ToList();

            var helper = CreateEntityHelper(context);
            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, context);
            }

            // Act - Process in batches of 100
            const int batchSize = 100;
            var totalProcessed = 0;
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < entities.Count; i += batchSize)
            {
                var batch = entities.Skip(i).Take(batchSize).ToList();
                var batchIds = batch.Select(e => e.Id).ToList();

                // Retrieve batch
                var retrieved = await helper.RetrieveAsync(batchIds, context);

                // Update batch
                foreach (var entity in retrieved)
                {
                    entity.Value += 1000;
                }

                await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
                var txHelper = CreateEntityHelper(transaction);

                foreach (var entity in retrieved)
                {
                    await txHelper.UpdateAsync(entity, transaction);
                }

                transaction.Commit();
                totalProcessed += retrieved.Count;

                Output.WriteLine($"{provider}: Processed batch {i / batchSize + 1} ({retrieved.Count} records)");
            }

            sw.Stop();
            Output.WriteLine($"{provider}: Processed {totalProcessed} records in {sw.ElapsedMilliseconds}ms");

            // Assert
            Assert.Equal(entities.Count, totalProcessed);

            // Verify some updates
            var sample = await helper.RetrieveAsync(
                entities.Take(10).Select(e => e.Id).ToList(),
                context);

            Assert.All(sample, s => Assert.InRange(s.Value, 6000, 6500));
        });
    }

    [Fact]
    public async Task LargeTransaction_5000Operations_Commits()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip for providers that might have transaction size limits
            if (provider == SupportedDatabase.Sqlite)
            {
                Output.WriteLine($"Skipping large transaction test for {provider}");
                return;
            }

            // Arrange
            var sw = Stopwatch.StartNew();

            // Act - Single large transaction with 5000 inserts
            await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
            var helper = CreateEntityHelper(transaction);

            var insertedIds = new List<long>();

            for (var i = 0; i < 5000; i++)
            {
                var entity = CreateTestEntity(NameEnum.Test, 6000 + i);
                await helper.CreateAsync(entity, transaction);
                insertedIds.Add(entity.Id);

                if (i % 1000 == 0)
                {
                    Output.WriteLine($"{provider}: Inserted {i} records...");
                }
            }

            transaction.Commit();
            sw.Stop();

            Output.WriteLine($"{provider}: Completed 5000 inserts in {sw.ElapsedMilliseconds}ms");

            // Assert - Verify sample of inserted records
            var sampleIds = insertedIds.Take(100).ToList();
            var retrieved = await CreateEntityHelper(context).RetrieveAsync(sampleIds, context);
            Assert.Equal(sampleIds.Count, retrieved.Count);
        });
    }

    [Fact]
    public async Task BulkUpsert_MixedNewAndExisting_HandlesCorrectly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip providers without native upsert support
            if (!SupportsUpsert(provider))
            {
                Output.WriteLine($"Skipping upsert test for {provider}");
                return;
            }

            // Arrange - Create 50 existing records
            var existingEntities = Enumerable.Range(0, 50)
                .Select(i => CreateTestEntity(NameEnum.Test, 7000 + i))
                .ToList();

            var helper = CreateEntityHelper(context);
            foreach (var entity in existingEntities)
            {
                await helper.CreateAsync(entity, context);
            }

            // Modify existing and create new entities
            foreach (var entity in existingEntities)
            {
                entity.Value += 100;
            }

            var newEntities = Enumerable.Range(0, 50)
                .Select(i => CreateTestEntity(NameEnum.Test2, 7100 + i))
                .ToList();

            var allEntities = existingEntities.Concat(newEntities).ToList();
            var sw = Stopwatch.StartNew();

            // Act - Upsert all (mix of updates and inserts)
            await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
            var txHelper = CreateEntityHelper(transaction);

            var totalUpserted = 0;
            foreach (var entity in allEntities)
            {
                var count = await txHelper.UpsertAsync(entity, transaction);
                totalUpserted += count;
            }

            transaction.Commit();
            sw.Stop();

            Output.WriteLine($"{provider}: Upserted {totalUpserted} records in {sw.ElapsedMilliseconds}ms");

            // Assert
            var allIds = allEntities.Select(e => e.Id).ToList();
            var retrieved = await helper.RetrieveAsync(allIds, context);

            Assert.Equal(allEntities.Count, retrieved.Count);

            // Verify updates applied
            var updatedExisting = retrieved.Where(r => r.Name == NameEnum.Test).ToList();
            Assert.All(updatedExisting, r => Assert.InRange(r.Value, 7100, 7150));
        });
    }

    [Fact]
    public async Task ParallelBulkOperations_NoDataCorruption()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Act - 5 parallel bulk operations on different data sets
            var tasks = Enumerable.Range(0, 5).Select(async batch =>
            {
                var entities = Enumerable.Range(0, 200)
                    .Select(i => CreateTestEntity(NameEnum.Test, 8000 + batch * 1000 + i))
                    .ToList();

                await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
                var helper = CreateEntityHelper(transaction);

                foreach (var entity in entities)
                {
                    await helper.CreateAsync(entity, transaction);
                }

                transaction.Commit();

                return entities.Select(e => e.Id).ToList();
            });

            var allIdLists = await Task.WhenAll(tasks);
            var allIds = allIdLists.SelectMany(ids => ids).ToList();

            // Assert
            Assert.Equal(1000, allIds.Count); // 5 batches * 200 entities

            var helper = CreateEntityHelper(context);
            var retrieved = await helper.RetrieveAsync(allIds, context);
            Assert.Equal(allIds.Count, retrieved.Count);
        });
    }

    [Fact]
    public async Task StreamProcessing_LargeResultSet_HandlesEfficiently()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange - Create 1000 records
            var entities = Enumerable.Range(0, 1000)
                .Select(i => CreateTestEntity(NameEnum.Test, 9000 + i))
                .ToList();

            var helper = CreateEntityHelper(context);
            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, context);
            }

            // Act - Retrieve all and process in memory
            var sw = Stopwatch.StartNew();

            await using var container = helper.BuildBaseRetrieve("t");
            container.Query.Append(" WHERE ")
                .Append(container.WrapObjectName("t"))
                .Append('.')
                .Append(container.WrapObjectName("value"))
                .Append(" >= ");
            container.Query.Append(container.MakeParameterName("minValue"));
            container.AddParameterWithValue("minValue", DbType.Int32, 9000);

            var results = await helper.LoadListAsync(container);
            sw.Stop();

            Output.WriteLine(
                $"{provider}: Retrieved and processed {results.Count} records in {sw.ElapsedMilliseconds}ms");

            // Assert
            Assert.Equal(entities.Count, results.Count);
            Assert.All(results, r => Assert.InRange(r.Value, 9000, 10000));
        });
    }

    [Fact]
    public async Task RollbackLargeBulkOperation_NoDataLeaked()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entities = Enumerable.Range(0, 500)
                .Select(i => CreateTestEntity(NameEnum.Test, 10000 + i))
                .ToList();

            // Act - Insert in transaction but rollback
            {
                await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
                var helper = CreateEntityHelper(transaction);

                foreach (var entity in entities)
                {
                    await helper.CreateAsync(entity, transaction);
                }

                // Don't commit - let it dispose and rollback
            }

            // Assert - No data should exist
            var helper2 = CreateEntityHelper(context);
            var retrieved = await helper2.RetrieveAsync(
                entities.Select(e => e.Id).ToList(),
                context);

            Assert.Empty(retrieved);
        });
    }

    private EntityHelper<TestTable, long> CreateEntityHelper(IDatabaseContext context)
    {
        var auditResolver = GetAuditResolver();
        return new EntityHelper<TestTable, long>(context, auditResolver);
    }

    private static TestTable CreateTestEntity(NameEnum name, int value)
    {
        return new TestTable
        {
            Id = Interlocked.Increment(ref _nextId),
            Name = name,
            Value = value,
            Description = $"Bulk test: {name}",
            IsActive = true,
            CreatedOn = DateTime.UtcNow
        };
    }

    private static bool SupportsUpsert(SupportedDatabase provider)
    {
        return provider is SupportedDatabase.SqlServer or
            SupportedDatabase.Oracle or
            SupportedDatabase.Firebird or
            SupportedDatabase.PostgreSql;
    }
}