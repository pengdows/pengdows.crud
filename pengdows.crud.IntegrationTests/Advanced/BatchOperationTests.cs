using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Advanced;

/// <summary>
/// Integration tests for batch operations including large inserts, updates,
/// deletes, and batch processing scenarios.
/// </summary>
[Collection("IntegrationTests")]
public class BatchOperationTests : DatabaseTestBase
{
    private static long _nextId;
    private readonly ConditionalWeakTable<IDatabaseContext, TableGateway<TestTable, long>> _gatewayCache = new();

    public BatchOperationTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [SkippableFact]
    public async Task BulkInsert_1000Records_AllPersisted()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateTableGateway(context);

            // Arrange
            var recordCount = provider == SupportedDatabase.Snowflake ? 100 : 1000;
            var entities = Enumerable.Range(0, recordCount)
                .Select(i => CreateTestEntity(NameEnum.Test, 1000 + i))
                .ToList();

            var sw = Stopwatch.StartNew();

            // Act - Insert in transaction using native batch pathway
            await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
            var insertedCount = await helper.CreateAsync(entities, transaction);

            transaction.Commit();
            sw.Stop();

            Output.WriteLine($"{provider}: Inserted {insertedCount} records in {sw.ElapsedMilliseconds}ms");
            Assert.Equal(entities.Count, insertedCount);

            // Assert
            var retrieved = await helper.RetrieveAsync(
                entities.Select(e => e.Id).ToList(),
                context);

            Assert.Equal(entities.Count, retrieved.Count);
        });
    }

    [SkippableFact]
    public async Task BulkUpdate_500Records_AllUpdated()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateTableGateway(context);

            // Arrange - Create initial data
            // Snowflake row-by-row UPDATE latency is significantly higher in CI; keep
            // workload lower to avoid false hang-detection timeouts.
            var recordCount = provider == SupportedDatabase.Snowflake ? 50 : 500;
            var entities = Enumerable.Range(0, recordCount)
                .Select(i => CreateTestEntity(NameEnum.Test, 2000 + i))
                .ToList();

            await using (var seedTransaction = await context.BeginTransactionAsync(IsolationLevel.ReadCommitted))
            {
                var seededCount = await helper.CreateAsync(entities, seedTransaction);
                Assert.Equal(entities.Count, seededCount);

                seedTransaction.Commit();
            }

            // Modify all entities
            foreach (var entity in entities)
            {
                entity.Name = NameEnum.Test2;
                entity.Value += 10000;
            }

            var sw = Stopwatch.StartNew();

            // Act - Batch update in transaction (uses native multi-row path where supported,
            // falls back to individual UPDATE statements for dialects without batch update support)
            await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);

            var totalUpdated = await helper.UpdateAsync(entities, transaction);

            transaction.Commit();
            sw.Stop();

            Output.WriteLine($"{provider}: BatchUpdated {totalUpdated} records in {sw.ElapsedMilliseconds}ms");

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

    [SkippableFact]
    public async Task BulkDelete_SingleCall_DeletesMultipleRecords()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var recordCount = provider == SupportedDatabase.Snowflake ? 100 : 300;
            var entities = Enumerable.Range(0, recordCount)
                .Select(i => CreateTestEntity(NameEnum.Test, 3000 + i))
                .ToList();

            var helper = CreateTableGateway(context);
            await using (var seedTransaction = context.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var seededCount = await helper.CreateAsync(entities, seedTransaction);
                Assert.Equal(entities.Count, seededCount);

                seedTransaction.Commit();
            }
            var idsToDelete = entities.Select(e => e.Id).ToList();
            var sw = Stopwatch.StartNew();

            // Act - Bulk delete by id list (built-in chunking path)
            var deleteCount = await helper.DeleteAsync(idsToDelete, context);
            sw.Stop();

            Output.WriteLine($"{provider}: Deleted {deleteCount} records in {sw.ElapsedMilliseconds}ms");

            // Assert
            Assert.Equal(entities.Count, deleteCount);

            var remaining = await helper.RetrieveAsync(idsToDelete, context);
            Assert.Empty(remaining);
        });
    }

    [SkippableFact]
    public async Task BulkRetrieve_1000Ids_ReturnsAllMatching()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateTableGateway(context);

            // SQLite has a parameter limit of 999, so use fewer records for it
            var recordCount = provider switch
            {
                SupportedDatabase.Sqlite => 900,
                SupportedDatabase.Snowflake => 100,
                _ => 1000
            };

            // Arrange - Create records
            var entities = Enumerable.Range(0, recordCount)
                .Select(i => CreateTestEntity(NameEnum.Test, 4000 + i))
                .ToList();

            await using (var seedTransaction = context.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var seededCount = await helper.CreateAsync(entities, seedTransaction);
                Assert.Equal(entities.Count, seededCount);

                seedTransaction.Commit();
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

    [SkippableFact]
    public async Task BatchProcessing_ChunksOf100_ProcessesAllRecords()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateTableGateway(context);

            // Arrange - Create records
            var recordCount = provider == SupportedDatabase.Snowflake ? 100 : 500;
            var entities = Enumerable.Range(0, recordCount)
                .Select(i => CreateTestEntity(NameEnum.Test, 5000 + i))
                .ToList();

            await using (var seedTransaction = context.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var seededCount = await helper.CreateAsync(entities, seedTransaction);
                Assert.Equal(entities.Count, seededCount);

                seedTransaction.Commit();
            }

            // Act - Process in batches of 100
            var batchSize = provider == SupportedDatabase.Snowflake ? 25 : 100;
            var totalProcessed = 0;
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < entities.Count; i += batchSize)
            {
                var batch = entities.Skip(i).Take(batchSize).ToList();
                var batchIds = batch.Select(e => e.Id).ToList();

                // Retrieve batch
                var retrieved = await helper.RetrieveAsync(batchIds, context);

                // Update batch values
                foreach (var entity in retrieved)
                {
                    entity.Value += 1000;
                }

                await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
                var updatedCount = await helper.UpdateAsync(retrieved, transaction);
                transaction.Commit();
                totalProcessed += updatedCount;

                Output.WriteLine($"{provider}: Processed batch {i / batchSize + 1} ({updatedCount} records)");
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

    [SkippableFact]
    public async Task LargeTransaction_5000Operations_Commits()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateTableGateway(context);

            // Skip for providers that might have transaction size limits
            if (provider == SupportedDatabase.Sqlite)
            {
                Output.WriteLine($"Skipping large transaction test for {provider}");
                return;
            }

            // Arrange
            var sw = Stopwatch.StartNew();

            var operationCount = provider == SupportedDatabase.Snowflake ? 500 : 5000;

            // Prepare all entities up front so IDs are known before the transaction
            var batchEntities = Enumerable.Range(0, operationCount)
                .Select(i => CreateTestEntity(NameEnum.Test, 6000 + i))
                .ToList();
            var insertedIds = batchEntities.Select(e => e.Id).ToList();

            // Act - Single large transaction via batch insert
            await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
            var insertedCount = await helper.CreateAsync(batchEntities, transaction);
            transaction.Commit();
            sw.Stop();

            Output.WriteLine($"{provider}: Inserted {insertedCount} records in {sw.ElapsedMilliseconds}ms");

            // Assert - Verify sample of inserted records
            var sampleIds = insertedIds.Take(100).ToList();
            var retrieved = await helper.RetrieveAsync(sampleIds, context);
            Assert.Equal(sampleIds.Count, retrieved.Count);
        });
    }

    [SkippableFact]
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

            var existingCount = provider == SupportedDatabase.Snowflake ? 20 : 50;

            // Arrange - Create existing records
            var existingEntities = Enumerable.Range(0, existingCount)
                .Select(i => CreateTestEntity(NameEnum.Test, 7000 + i))
                .ToList();

            var helper = CreateTableGateway(context);
            await using (var seedTransaction = context.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var seededCount = await helper.CreateAsync(existingEntities, seedTransaction);
                Assert.Equal(existingEntities.Count, seededCount);

                seedTransaction.Commit();
            }

            // Modify existing and create new entities
            foreach (var entity in existingEntities)
            {
                entity.Value += 100;
            }

            var newEntities = Enumerable.Range(0, existingCount)
                .Select(i => CreateTestEntity(NameEnum.Test2, 7100 + i))
                .ToList();

            var allEntities = existingEntities.Concat(newEntities).ToList();
            var sw = Stopwatch.StartNew();

            // Act - Upsert all (mix of updates and inserts)
            await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);

            var totalUpserted = await helper.UpsertAsync(allEntities, transaction);

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

    [SkippableFact]
    public async Task ParallelBatchOperations_NoDataCorruption()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateTableGateway(context);

            var batchCount = provider == SupportedDatabase.Snowflake ? 3 : 5;
            var recordsPerBatch = provider == SupportedDatabase.Snowflake ? 50 : 200;

            // Act - 5 parallel batch operations on different data sets
            var tasks = Enumerable.Range(0, batchCount).Select(async batch =>
            {
                var entities = Enumerable.Range(0, recordsPerBatch)
                    .Select(i => CreateTestEntity(NameEnum.Test, 8000 + batch * 1000 + i))
                    .ToList();

                await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);
                var insertedCount = await helper.CreateAsync(entities, transaction);
                Assert.Equal(entities.Count, insertedCount);

                transaction.Commit();

                return entities.Select(e => e.Id).ToList();
            });

            var allIdLists = await Task.WhenAll(tasks);
            var allIds = allIdLists.SelectMany(ids => ids).ToList();

            // Assert
            Assert.Equal(batchCount * recordsPerBatch, allIds.Count);

            var retrieved = await helper.RetrieveAsync(allIds, context);
            Assert.Equal(allIds.Count, retrieved.Count);
        });
    }

    [SkippableFact]
    public async Task StreamProcessing_LargeResultSet_HandlesEfficiently()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateTableGateway(context);

            // Arrange - Create records
            var recordCount = provider == SupportedDatabase.Snowflake ? 100 : 1000;
            var entities = Enumerable.Range(0, recordCount)
                .Select(i => CreateTestEntity(NameEnum.Test, 9000 + i))
                .ToList();

            await using (var seedTransaction = context.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var seededCount = await helper.CreateAsync(entities, seedTransaction);
                Assert.Equal(entities.Count, seededCount);

                seedTransaction.Commit();
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

    [SkippableFact]
    public async Task RollbackLargeBatchOperation_NoDataLeaked()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = CreateTableGateway(context);

            // Arrange
            var recordCount = provider == SupportedDatabase.Snowflake ? 100 : 500;
            var entities = Enumerable.Range(0, recordCount)
                .Select(i => CreateTestEntity(NameEnum.Test, 10000 + i))
                .ToList();

            // Act - Insert in transaction but rollback
            {
                await using var transaction = context.BeginTransaction(IsolationLevel.ReadCommitted);

                var insertedCount = await helper.CreateAsync(entities, transaction);
                Assert.Equal(entities.Count, insertedCount);

                // Don't commit - let it dispose and rollback
            }

            // Assert - No data should exist
            var retrieved = await helper.RetrieveAsync(
                entities.Select(e => e.Id).ToList(),
                context);

            Assert.Empty(retrieved);
        });
    }

    private TableGateway<TestTable, long> CreateTableGateway(IDatabaseContext context)
    {
        return _gatewayCache.GetValue(context, ctx =>
        {
            var auditResolver = GetAuditResolver();
            return new TableGateway<TestTable, long>(ctx, auditResolver);
        });
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

    private static void TraceProgress(SupportedDatabase provider, string phase, int current, int total)
    {
        if (!IntegrationTraceLog.IsEnabled(provider))
        {
            return;
        }

        if (current == 1 || current == total || current % 100 == 0)
        {
            IntegrationTraceLog.Write(provider, $"{phase} progress={current}/{total}");
        }
    }

    private static bool SupportsUpsert(SupportedDatabase provider)
    {
        // All providers support batch upsert, using their native mechanism:
        //   ON CONFLICT DO UPDATE — PostgreSQL, CockroachDB, SQLite, DuckDB
        //   ON DUPLICATE KEY UPDATE — MySQL, MariaDB
        //   individual MERGE per entity (fallback) — SQL Server, Oracle, Firebird, Snowflake
        return true;
    }
}
