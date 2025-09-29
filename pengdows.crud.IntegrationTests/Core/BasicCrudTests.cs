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
            var entity = CreateTestEntity($"Test-{provider}-{Guid.NewGuid()}", 42);

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
            var entity = CreateTestEntity($"Retrieve-{provider}", 99);
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
                CreateTestEntity($"Multi1-{provider}", 10),
                CreateTestEntity($"Multi2-{provider}", 20),
                CreateTestEntity($"Multi3-{provider}", 30)
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
            var entity = CreateTestEntity($"Update-{provider}", 100);
            await helper.CreateAsync(entity, context);

            // Act - Update the entity
            entity.Name = $"Updated-{provider}";
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
            var entity = CreateTestEntity($"NonExistent-{provider}", 500);
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
            var entity = CreateTestEntity($"Delete-{provider}", 200);
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
                CreateTestEntity($"BulkDelete1-{provider}", 301),
                CreateTestEntity($"BulkDelete2-{provider}", 302),
                CreateTestEntity($"BulkDelete3-{provider}", 303)
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
            var entity = CreateTestEntity($"UpsertNew-{provider}", 400);

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
            var entity = CreateTestEntity($"UpsertExisting-{provider}", 500);
            await helper.CreateAsync(entity, context);

            // Act - Upsert with changes
            entity.Name = $"UpsertUpdated-{provider}";
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
        var auditResolver = Host.Services.GetService<IAuditValueResolver>() ??
                           new StringAuditContextProvider();
        return new EntityHelper<TestTable, long>(context, auditValueResolver: auditResolver);
    }

    private static TestTable CreateTestEntity(string name, int value)
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

/// <summary>
/// Helper class to create test tables across different database providers
/// </summary>
internal class TestTableCreator
{
    private readonly IDatabaseContext _context;

    public TestTableCreator(IDatabaseContext context)
    {
        _context = context;
    }

    public async Task CreateTestTableAsync()
    {
        var sql = _context.Product switch
        {
            SupportedDatabase.Sqlite => CreateSqliteTableSql(),
            SupportedDatabase.PostgreSql => CreatePostgreSqlTableSql(),
            SupportedDatabase.SqlServer => CreateSqlServerTableSql(),
            SupportedDatabase.MySQL => CreateMySqlTableSql(),
            SupportedDatabase.MariaDb => CreateMariaDbTableSql(),
            SupportedDatabase.Oracle => CreateOracleTableSql(),
            _ => throw new NotSupportedException($"Database {_context.Product} not supported")
        };

        using var container = _context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    private string CreateSqliteTableSql() => @"
        CREATE TABLE IF NOT EXISTS TestTable (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Value INTEGER NOT NULL,
            Description TEXT,
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedOn TEXT NOT NULL,
            CreatedBy TEXT,
            LastUpdatedOn TEXT,
            LastUpdatedBy TEXT,
            Version INTEGER NOT NULL DEFAULT 1
        )";

    private string CreatePostgreSqlTableSql() => @"
        CREATE TABLE IF NOT EXISTS TestTable (
            Id BIGSERIAL PRIMARY KEY,
            Name VARCHAR(255) NOT NULL,
            Value INTEGER NOT NULL,
            Description TEXT,
            IsActive BOOLEAN NOT NULL DEFAULT TRUE,
            CreatedOn TIMESTAMP NOT NULL DEFAULT NOW(),
            CreatedBy VARCHAR(100),
            LastUpdatedOn TIMESTAMP,
            LastUpdatedBy VARCHAR(100),
            Version INTEGER NOT NULL DEFAULT 1
        )";

    private string CreateSqlServerTableSql() => @"
        IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TestTable]') AND type in (N'U'))
        CREATE TABLE [dbo].[TestTable] (
            [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
            [Name] NVARCHAR(255) NOT NULL,
            [Value] INT NOT NULL,
            [Description] NVARCHAR(MAX),
            [IsActive] BIT NOT NULL DEFAULT 1,
            [CreatedOn] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
            [CreatedBy] NVARCHAR(100),
            [LastUpdatedOn] DATETIME2,
            [LastUpdatedBy] NVARCHAR(100),
            [Version] INT NOT NULL DEFAULT 1
        )";

    private string CreateMySqlTableSql() => @"
        CREATE TABLE IF NOT EXISTS TestTable (
            Id BIGINT AUTO_INCREMENT PRIMARY KEY,
            Name VARCHAR(255) NOT NULL,
            Value INT NOT NULL,
            Description TEXT,
            IsActive BOOLEAN NOT NULL DEFAULT TRUE,
            CreatedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CreatedBy VARCHAR(100),
            LastUpdatedOn TIMESTAMP NULL,
            LastUpdatedBy VARCHAR(100),
            Version INT NOT NULL DEFAULT 1
        )";

    private string CreateMariaDbTableSql() => CreateMySqlTableSql(); // Same as MySQL

    private string CreateOracleTableSql() => @"
        DECLARE
            table_exists NUMBER;
        BEGIN
            SELECT COUNT(*) INTO table_exists FROM user_tables WHERE table_name = 'TESTTABLE';
            IF table_exists = 0 THEN
                EXECUTE IMMEDIATE '
                    CREATE TABLE TestTable (
                        Id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        Name VARCHAR2(255) NOT NULL,
                        Value NUMBER NOT NULL,
                        Description CLOB,
                        IsActive NUMBER(1) DEFAULT 1 NOT NULL,
                        CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
                        CreatedBy VARCHAR2(100),
                        LastUpdatedOn TIMESTAMP,
                        LastUpdatedBy VARCHAR2(100),
                        Version NUMBER DEFAULT 1 NOT NULL
                    )';
            END IF;
        END;";
}