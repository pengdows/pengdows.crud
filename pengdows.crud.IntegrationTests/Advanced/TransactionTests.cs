using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.exceptions;
using System.Data;
using System.Runtime.CompilerServices;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Advanced;

/// <summary>
/// Integration tests for transaction management including isolation levels,
/// commit/rollback behavior, savepoints, and readonly transactions.
/// </summary>
[Collection("IntegrationTests")]
public class TransactionTests : DatabaseTestBase
{
    private static long _nextId;
    private readonly ConditionalWeakTable<IDatabaseContext, TableGateway<TestTable, long>> _gatewayCache = new();

    public TransactionTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [SkippableFact]
    public async Task Transaction_Commit_PersistsChanges()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entity = CreateTestEntity(NameEnum.Test, 100);

            // Act
            await using var transaction = context.BeginTransaction(GetReadCommittedCompatibleIsolationLevel(provider));
            var helper = CreateTableGateway(context);

            await helper.CreateAsync(entity, transaction);
            transaction.Commit();

            // Assert
            Assert.True(transaction.WasCommitted);
            Assert.False(transaction.WasRolledBack);
            Assert.True(transaction.IsCompleted);

            // Verify data persisted outside transaction
            var retrieved = await CreateTableGateway(context).RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal(entity.Name, retrieved.Name);
        });
    }

    [SkippableFact]
    public async Task Transaction_Rollback_DiscardsChanges()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entity = CreateTestEntity(NameEnum.Test, 200);

            // Act
            await using var transaction = context.BeginTransaction(GetReadCommittedCompatibleIsolationLevel(provider));
            var helper = CreateTableGateway(context);

            await helper.CreateAsync(entity, transaction);
            transaction.Rollback();

            // Assert
            Assert.False(transaction.WasCommitted);
            Assert.True(transaction.WasRolledBack);
            Assert.True(transaction.IsCompleted);

            // Verify data was not persisted
            var retrieved = await CreateTableGateway(context).RetrieveOneAsync(entity.Id, context);
            Assert.Null(retrieved);
        });
    }

    [SkippableFact]
    public async Task Transaction_ReadCommitted_AllowsReadOperations()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange - Create entity outside transaction
            var entity = CreateTestEntity(NameEnum.Test, 300);
            await CreateTableGateway(context).CreateAsync(entity, context);

            // Act - Read within transaction
            await using var transaction = context.BeginTransaction(GetReadCommittedCompatibleIsolationLevel(provider));
            var helper = CreateTableGateway(context);
            var retrieved = await helper.RetrieveOneAsync(entity.Id, transaction);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(entity.Id, retrieved.Id);
            var allowedIsolationLevels = new[]
            {
                GetReadCommittedCompatibleIsolationLevel(provider),
                IsolationLevel.Serializable
            };
            Assert.Contains(transaction.IsolationLevel, allowedIsolationLevels);
        });
    }

    [SkippableFact]
    public async Task Transaction_ReadOnly_AllowsReads_PreventsWrites()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip for databases that don't support read-only transactions properly
            if (!SupportsReadOnlyTransactions(provider))
            {
                Output.WriteLine($"Skipping read-only transaction test for {provider}");
                return;
            }

            // Arrange - Create entity outside transaction
            var existingEntity = CreateTestEntity(NameEnum.Test, 400);
            await CreateTableGateway(context).CreateAsync(existingEntity, context);

            // Act - Read-only transaction
            await using var readOnlyTransaction = context.BeginTransaction(
                GetReadCommittedCompatibleIsolationLevel(provider),
                ExecutionType.Read);

            var helper = CreateTableGateway(context);

            // Assert - Reads should work
            var retrieved = await helper.RetrieveOneAsync(existingEntity.Id, readOnlyTransaction);
            Assert.NotNull(retrieved);

            // Writes should fail (behavior varies by provider)
            var newEntity = CreateTestEntity(NameEnum.Test2, 401);
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await helper.CreateAsync(newEntity, readOnlyTransaction);
            });
        });
    }

    [SkippableFact]
    public async Task Transaction_Serializable_ProvidesStrictIsolation()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip for providers with limited isolation level support
            if (!SupportsSerializableIsolation(provider))
            {
                Output.WriteLine($"Skipping serializable test for {provider}");
                return;
            }

            // Arrange
            var entity = CreateTestEntity(NameEnum.Test, 500);
            await CreateTableGateway(context).CreateAsync(entity, context);

            // Act
            await using var transaction = context.BeginTransaction(IsolationLevel.Serializable);
            var helper = CreateTableGateway(context);

            var retrieved = await helper.RetrieveOneAsync(entity.Id, transaction);
            retrieved!.Value = 999;
            await helper.UpdateAsync(retrieved, transaction);

            transaction.Commit();

            // Assert
            Assert.Equal(IsolationLevel.Serializable, transaction.IsolationLevel);
            var updated = await CreateTableGateway(context).RetrieveOneAsync(entity.Id, context);
            Assert.Equal(999, updated!.Value);
        });
    }

    [SkippableFact]
    public async Task Transaction_Savepoint_RollsBackPartialWork()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip for providers without savepoint support
            if (!SupportsSavepoints(provider))
            {
                Output.WriteLine($"Skipping savepoint test for {provider}");
                return;
            }

            // Arrange
            var entity1 = CreateTestEntity(NameEnum.Test, 600);
            var entity2 = CreateTestEntity(NameEnum.Test2, 601);

            // Act
            await using var transaction = context.BeginTransaction(GetReadCommittedCompatibleIsolationLevel(provider));
            var helper = CreateTableGateway(context);

            // Create first entity
            await helper.CreateAsync(entity1, transaction);

            // Create savepoint
            await transaction.SavepointAsync("before_second_insert");

            // Create second entity
            await helper.CreateAsync(entity2, transaction);

            // Rollback to savepoint (should remove entity2 but keep entity1)
            await transaction.RollbackToSavepointAsync("before_second_insert");

            transaction.Commit();

            // Assert
            var retrieved1 = await CreateTableGateway(context).RetrieveOneAsync(entity1.Id, context);
            var retrieved2 = await CreateTableGateway(context).RetrieveOneAsync(entity2.Id, context);

            Assert.NotNull(retrieved1); // Should exist
            Assert.Null(retrieved2); // Should not exist
        });
    }

    [SkippableFact]
    public async Task Transaction_MultipleSavepoints_RollbackCorrectly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!SupportsSavepoints(provider))
            {
                Output.WriteLine($"Skipping multiple savepoints test for {provider}");
                return;
            }

            // Arrange
            var entity1 = CreateTestEntity(NameEnum.Test, 700);
            var entity2 = CreateTestEntity(NameEnum.Test2, 701);
            var entity3 = CreateTestEntity(NameEnum.Test, 702);

            // Act
            await using var transaction = context.BeginTransaction(GetReadCommittedCompatibleIsolationLevel(provider));
            var helper = CreateTableGateway(context);

            await helper.CreateAsync(entity1, transaction);
            await transaction.SavepointAsync("sp1");

            await helper.CreateAsync(entity2, transaction);
            await transaction.SavepointAsync("sp2");

            await helper.CreateAsync(entity3, transaction);

            // Rollback to sp1 (removes entity2 and entity3)
            await transaction.RollbackToSavepointAsync("sp1");

            transaction.Commit();

            // Assert
            var retrieved1 = await CreateTableGateway(context).RetrieveOneAsync(entity1.Id, context);
            var retrieved2 = await CreateTableGateway(context).RetrieveOneAsync(entity2.Id, context);
            var retrieved3 = await CreateTableGateway(context).RetrieveOneAsync(entity3.Id, context);

            Assert.NotNull(retrieved1);
            Assert.Null(retrieved2);
            Assert.Null(retrieved3);
        });
    }

    [SkippableFact]
    public async Task Transaction_DisposeWithoutCommit_RollsBack()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entity = CreateTestEntity(NameEnum.Test, 800);

            // Act
            {
                await using var transaction = context.BeginTransaction(GetReadCommittedCompatibleIsolationLevel(provider));
                var helper = CreateTableGateway(context);
                await helper.CreateAsync(entity, transaction);
                // Dispose without commit
            }

            // Assert - Changes should be rolled back
            var retrieved = await CreateTableGateway(context).RetrieveOneAsync(entity.Id, context);
            Assert.Null(retrieved);
        });
    }

    [SkippableFact]
    public async Task Transaction_MultipleOperations_AllOrNothing()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entities = new[]
            {
                CreateTestEntity(NameEnum.Test, 900),
                CreateTestEntity(NameEnum.Test2, 901),
                CreateTestEntity(NameEnum.Test, 902)
            };

            // Act
            await using var transaction = context.BeginTransaction(GetReadCommittedCompatibleIsolationLevel(provider));
            var helper = CreateTableGateway(context);

            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, transaction);
            }

            transaction.Commit();

            // Assert - All entities should be persisted
            var baseHelper = CreateTableGateway(context);
            foreach (var entity in entities)
            {
                var retrieved = await baseHelper.RetrieveOneAsync(entity.Id, context);
                Assert.NotNull(retrieved);
                Assert.Equal(entity.Value, retrieved.Value);
            }
        });
    }

    [SkippableFact]
    public async Task Transaction_UpdateWithinTransaction_Visible()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entity = CreateTestEntity(NameEnum.Test, 1000);
            await CreateTableGateway(context).CreateAsync(entity, context);

            // Act
            await using var transaction = context.BeginTransaction(GetReadCommittedCompatibleIsolationLevel(provider));
            var helper = CreateTableGateway(context);

            var retrieved = await helper.RetrieveOneAsync(entity.Id, transaction);
            retrieved!.Value = 2000;
            await helper.UpdateAsync(retrieved, transaction);

            // Read again within same transaction
            var reretrieved = await helper.RetrieveOneAsync(entity.Id, transaction);

            transaction.Commit();

            // Assert
            Assert.Equal(2000, reretrieved!.Value);

            var final = await CreateTableGateway(context).RetrieveOneAsync(entity.Id, context);
            Assert.Equal(2000, final!.Value);
        });
    }

    [SkippableFact]
    public async Task Transaction_DeleteWithinTransaction_NotVisibleOutside()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var entity = CreateTestEntity(NameEnum.Test, 1100);
            await CreateTableGateway(context).CreateAsync(entity, context);

            // Act
            await using var transaction = context.BeginTransaction(GetReadCommittedCompatibleIsolationLevel(provider));
            var helper = CreateTableGateway(context);

            await helper.DeleteAsync(entity.Id, transaction);
            transaction.Rollback();

            // Assert - Entity should still exist after rollback
            var retrieved = await CreateTableGateway(context).RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
        });
    }

    [SkippableFact]
    public async Task Transaction_IsolationProfile_SafeNonBlockingReads_Works()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider is SupportedDatabase.PostgreSql or SupportedDatabase.YugabyteDb)
            {
                await Assert.ThrowsAsync<TransactionModeNotSupportedException>(async () =>
                {
                    await using var _ = context.BeginTransaction(
                        IsolationProfile.SafeNonBlockingReads,
                        ExecutionType.Write);
                    await Task.CompletedTask;
                });
                return;
            }

            // Arrange
            var entity = CreateTestEntity(NameEnum.Test, 1200);

            // Act - Use IsolationProfile instead of IsolationLevel
            await using var transaction = context.BeginTransaction(
                IsolationProfile.SafeNonBlockingReads,
                ExecutionType.Write);

            var helper = CreateTableGateway(context);
            await helper.CreateAsync(entity, transaction);
            transaction.Commit();

            // Assert
            var retrieved = await CreateTableGateway(context).RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
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
            Description = $"Transaction test: {name}",
            IsActive = true,
            CreatedOn = DateTime.UtcNow
        };
    }

    private static bool SupportsSerializableIsolation(SupportedDatabase provider)
    {
        // TiDB only supports ReadCommitted and Snapshot; Serializable is rejected by the isolation resolver.
        return provider is not (SupportedDatabase.Sqlite or SupportedDatabase.Snowflake or SupportedDatabase.TiDb);
    }

    private static bool SupportsSavepoints(SupportedDatabase provider)
    {
        // Most modern databases support savepoints
        return provider is SupportedDatabase.PostgreSql or
            SupportedDatabase.SqlServer or
            SupportedDatabase.Oracle or
            SupportedDatabase.MySql or
            SupportedDatabase.MariaDb or
            SupportedDatabase.Sqlite or
            SupportedDatabase.Firebird;
    }

    private static IsolationLevel GetReadCommittedCompatibleIsolationLevel(SupportedDatabase provider)
    {
        return provider is SupportedDatabase.CockroachDb or SupportedDatabase.DuckDB
            ? IsolationLevel.Serializable
            : IsolationLevel.ReadCommitted;
    }
}
