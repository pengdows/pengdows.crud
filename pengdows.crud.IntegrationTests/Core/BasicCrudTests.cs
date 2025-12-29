using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Comprehensive integration tests for basic CRUD operations across all database providers.
/// Each test method focuses on a specific CRUD operation and runs against all supported databases.
/// </summary>
public class BasicCrudTests : DatabaseTestBase
{
    public BasicCrudTests(ITestOutputHelper output) : base(output) { }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        // Create test table for each provider
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [Fact]
    public async Task CreateAsync_WithValidEntity_InsertsRecordSuccessfully()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var entity = CreateTestEntity(NameEnum.Test, 42);

            // Act
            var result = await helper.CreateAsync(entity, context);

            // Assert
            Assert.True(result);
            Assert.True(entity.Id > 0); // Should have auto-generated ID

            // Verify it was actually inserted
            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal(entity.Name, retrieved.Name);
            Assert.Equal(entity.Value, retrieved.Value);
        });
    }

    [Fact]
    public async Task RetrieveOneAsync_WithExistingId_ReturnsCorrectEntity()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var entity = CreateTestEntity(NameEnum.Test, 99);
            await helper.CreateAsync(entity, context);

            // Act
            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(entity.Id, retrieved.Id);
            Assert.Equal(entity.Name, retrieved.Name);
            Assert.Equal(entity.Value, retrieved.Value);
        });
    }

    [Fact]
    public async Task RetrieveOneAsync_WithNonExistentId_ReturnsNull()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var nonExistentId = -999999L;

            // Act
            var result = await helper.RetrieveOneAsync(nonExistentId, context);

            // Assert
            Assert.Null(result);
        });
    }

    [Fact]
    public async Task RetrieveAsync_WithMultipleIds_ReturnsMatchingEntities()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var entities = new[]
            {
                CreateTestEntity(NameEnum.Test, 10),
                CreateTestEntity(NameEnum.Test, 20),
                CreateTestEntity(NameEnum.Test, 30)
            };

            // Insert test data
            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, context);
            }

            var idsToRetrieve = entities.Take(2).Select(e => e.Id).ToList();

            // Act
            var retrieved = await helper.RetrieveAsync(idsToRetrieve, context);

            // Assert
            Assert.Equal(2, retrieved.Count);
            Assert.All(retrieved, r => Assert.Contains(r.Id, idsToRetrieve));
        });
    }

    [Fact]
    public async Task UpdateAsync_WithValidChanges_UpdatesRecordSuccessfully()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var entity = CreateTestEntity(NameEnum.Test, 100);
            await helper.CreateAsync(entity, context);

            // Act - Update the entity
            entity.Name = NameEnum.Test2;
            entity.Value = 999;
            var updateCount = await helper.UpdateAsync(entity, context);

            // Assert
            Assert.Equal(1, updateCount);

            // Verify the update
            var updated = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(updated);
            Assert.Equal(entity.Name, updated.Name);
            Assert.Equal(entity.Value, updated.Value);
        });
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentEntity_ReturnsZero()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var entity = CreateTestEntity(NameEnum.Test, 500);
            entity.Id = -999999; // Non-existent ID

            // Act
            var updateCount = await helper.UpdateAsync(entity, context);

            // Assert
            Assert.Equal(0, updateCount);
        });
    }

    [Fact]
    public async Task DeleteAsync_WithExistingId_DeletesRecordSuccessfully()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var entity = CreateTestEntity(NameEnum.Test, 200);
            await helper.CreateAsync(entity, context);

            // Act
            var deleteCount = await helper.DeleteAsync(entity.Id, context);

            // Assert
            Assert.Equal(1, deleteCount);

            // Verify it's actually deleted
            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.Null(retrieved);
        });
    }

    [Fact]
    public async Task DeleteAsync_WithMultipleIds_DeletesAllMatching()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var entities = new[]
            {
                CreateTestEntity(NameEnum.Test, 301),
                CreateTestEntity(NameEnum.Test, 302),
                CreateTestEntity(NameEnum.Test, 303)
            };

            foreach (var entity in entities)
            {
                await helper.CreateAsync(entity, context);
            }

            var idsToDelete = entities.Select(e => e.Id).ToList();

            // Act
            var deleteCount = await helper.DeleteAsync(idsToDelete, context);

            // Assert
            Assert.Equal(3, deleteCount);

            // Verify they're all deleted
            var remaining = await helper.RetrieveAsync(idsToDelete, context);
            Assert.Empty(remaining);
        });
    }

    [Fact]
    public async Task UpsertAsync_WithNewEntity_InsertsRecord()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = CreateEntityHelper(context);
            var entity = CreateTestEntity(NameEnum.Test, 400);

            // Act
            var upsertCount = await helper.UpsertAsync(entity, context);

            // Assert
            Assert.Equal(1, upsertCount);
            Assert.True(entity.Id > 0);

            // Verify it was inserted
            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal(entity.Name, retrieved.Name);
        });
    }

    [Fact]
    public async Task UpsertAsync_WithExistingEntity_UpdatesRecord()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Skip databases that don't support native upsert
            if (!SupportsMergeUpsert(provider))
            {
                Output.WriteLine($"Skipping upsert test for {provider} - no native MERGE support");
                return;
            }

            // Arrange
            var helper = CreateEntityHelper(context);
            var entity = CreateTestEntity(NameEnum.Test, 500);
            await helper.CreateAsync(entity, context);

            // Act - Upsert with changes
            entity.Name = NameEnum.Test2;
            entity.Value = 999;
            var upsertCount = await helper.UpsertAsync(entity, context);

            // Assert
            Assert.Equal(1, upsertCount);

            // Verify it was updated
            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal(entity.Name, retrieved.Name);
            Assert.Equal(entity.Value, retrieved.Value);
        });
    }

    private EntityHelper<TestTable, long> CreateEntityHelper(IDatabaseContext context)
    {
        var auditResolver = Host.Services.GetService(typeof(IAuditValueResolver)) as IAuditValueResolver ??
                           new StringAuditContextProvider();
        return new EntityHelper<TestTable, long>(context, auditValueResolver: auditResolver);
    }

    private static TestTable CreateTestEntity(NameEnum name, int value)
    {
        return new TestTable
        {
            Name = name,
            Value = value,
            Description = $"Test description for {name}",
            IsActive = true,
            CreatedOn = DateTime.UtcNow
        };
    }

    private static bool SupportsMergeUpsert(SupportedDatabase provider)
    {
        return provider is SupportedDatabase.SqlServer or
                          SupportedDatabase.Oracle or
                          SupportedDatabase.Firebird or
                          SupportedDatabase.PostgreSql; // PostgreSQL 15+
    }
}