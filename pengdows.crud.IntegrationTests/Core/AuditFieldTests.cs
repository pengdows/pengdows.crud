using System.Data;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Integration tests for audit field auto-population across supported database providers.
/// </summary>
[Collection("IntegrationTests")]
public class AuditFieldTests : DatabaseTestBase
{
    private static long _nextId;

    public AuditFieldTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return base.GetSupportedProviders()
            .ToArray();
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<AuditedEntity>();

        if (provider == SupportedDatabase.Firebird)
        {
            await EnsureFirebirdAuditTableAsync(context);
            return;
        }

        await DropTableIfExistsAsync(context, "audited_entity");

        var createSql = BuildAuditTableSql(provider, context);
        await using var container = context.CreateSqlContainer(createSql);
        await container.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public Task CreateAsync_PopulatesCreatedByAndCreatedOn()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<AuditedEntity, long>(context, GetAuditResolver());
            var entity = new AuditedEntity
            {
                Id = Interlocked.Increment(ref _nextId),
                Name = "Test Entity"
            };

            var beforeCreate = DateTime.UtcNow.AddSeconds(-1);

            var result = await helper.CreateAsync(entity, context);
            Assert.True(result);

            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal("testuser", retrieved.CreatedBy);
            Assert.NotEqual(default, retrieved.CreatedAt);
            Assert.True(retrieved.CreatedAt.Year >= 2024,
                $"CreatedAt {retrieved.CreatedAt} should be a reasonable date");

            Output.WriteLine($"{provider}: CreatedBy: {retrieved.CreatedBy}");
            Output.WriteLine($"{provider}: CreatedAt: {retrieved.CreatedAt}");
        });
    }

    [SkippableFact]
    public Task CreateAsync_AlsoPopulatesLastUpdatedFields()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<AuditedEntity, long>(context, GetAuditResolver());
            var entity = new AuditedEntity
            {
                Id = Interlocked.Increment(ref _nextId),
                Name = "Test Entity"
            };

            var beforeCreate = DateTime.UtcNow.AddSeconds(-1);

            await helper.CreateAsync(entity, context);

            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);

            Assert.Equal("testuser", retrieved.UpdatedBy);
            Assert.True(retrieved.UpdatedAt >= beforeCreate,
                $"UpdatedAt {retrieved.UpdatedAt} should be >= {beforeCreate}");

            Output.WriteLine($"{provider}: UpdatedBy on create: '{retrieved.UpdatedBy}'");
            Output.WriteLine($"{provider}: UpdatedAt on create: {retrieved.UpdatedAt}");
        });
    }

    [SkippableFact]
    public Task UpdateAsync_PopulatesLastUpdatedByAndLastUpdatedOn()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<AuditedEntity, long>(context, GetAuditResolver());
            var entity = new AuditedEntity
            {
                Id = Interlocked.Increment(ref _nextId),
                Name = "Original Name"
            };

            await helper.CreateAsync(entity, context);

            await Task.Delay(10);
            var beforeUpdate = DateTime.UtcNow.AddSeconds(-1);

            entity.Name = "Updated Name";
            var updateCount = await helper.UpdateAsync(entity, context);

            Assert.Equal(1, updateCount);

            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);

            Assert.Equal("testuser", retrieved.UpdatedBy);
            Assert.True(retrieved.UpdatedAt >= beforeUpdate,
                $"UpdatedAt {retrieved.UpdatedAt} should be >= {beforeUpdate}");

            Output.WriteLine($"{provider}: UpdatedBy: {retrieved.UpdatedBy}");
            Output.WriteLine($"{provider}: UpdatedAt: {retrieved.UpdatedAt}");
        });
    }

    [SkippableFact]
    public Task UpdateAsync_DoesNotModifyCreatedFields()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<AuditedEntity, long>(context, GetAuditResolver());
            var entity = new AuditedEntity
            {
                Id = Interlocked.Increment(ref _nextId),
                Name = "Original Name"
            };

            await helper.CreateAsync(entity, context);

            var created = await helper.RetrieveOneAsync(entity.Id, context);
            var originalCreatedBy = created!.CreatedBy;
            var originalCreatedAt = created.CreatedAt;

            await Task.Delay(10);

            entity.Name = "Updated Name";
            await helper.UpdateAsync(entity, context);

            var updated = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(updated);
            Assert.Equal(originalCreatedBy, updated.CreatedBy);
            Assert.Equal(originalCreatedAt, updated.CreatedAt);

            Output.WriteLine($"{provider}: CreatedBy before/after update: {originalCreatedBy}/{updated.CreatedBy}");
            Output.WriteLine($"{provider}: CreatedAt before/after update: {originalCreatedAt}/{updated.CreatedAt}");
        });
    }

    [SkippableFact]
    public Task MultipleUpdates_UpdatesLastUpdatedFieldsEachTime()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<AuditedEntity, long>(context, GetAuditResolver());
            var entity = new AuditedEntity
            {
                Id = Interlocked.Increment(ref _nextId),
                Name = "Original"
            };

            await helper.CreateAsync(entity, context);

            entity.Name = "Update 1";
            await helper.UpdateAsync(entity, context);
            var afterFirst = await helper.RetrieveOneAsync(entity.Id, context);
            var firstUpdateTime = afterFirst!.UpdatedAt;

            await Task.Delay(20);

            entity.Name = "Update 2";
            await helper.UpdateAsync(entity, context);

            var afterSecond = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(afterSecond);
            Assert.True(afterSecond.UpdatedAt >= firstUpdateTime,
                $"Second UpdatedAt {afterSecond.UpdatedAt} should be >= first {firstUpdateTime}");

            Output.WriteLine($"{provider}: First update time: {firstUpdateTime}");
            Output.WriteLine($"{provider}: Second update time: {afterSecond.UpdatedAt}");
        });
    }

    [SkippableFact]
    public Task CreateAsync_WithoutAuditResolver_ThrowsForUserAuditFields()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<AuditedEntity, long>(context);
            var entity = new AuditedEntity
            {
                Id = Interlocked.Increment(ref _nextId),
                Name = "No Audit"
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await helper.CreateAsync(entity, context);
            });

            Assert.Contains("AuditValues resolver is required", ex.Message);
            Output.WriteLine($"{provider}: Expected exception: {ex.Message}");
        });
    }

    [SkippableFact]
    public Task UpsertAsync_NewEntity_PopulatesCreatedFields()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<AuditedEntity, long>(context, GetAuditResolver());
            var entity = new AuditedEntity
            {
                Id = Interlocked.Increment(ref _nextId),
                Name = "Upserted New"
            };

            var beforeUpsert = DateTime.UtcNow.AddSeconds(-1);
            var count = await helper.UpsertAsync(entity, context);

            Assert.Equal(1, count);

            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(retrieved);
            Assert.Equal("testuser", retrieved.CreatedBy);
            Assert.True(retrieved.CreatedAt >= beforeUpsert);

            Output.WriteLine($"{provider}: Upsert (new) CreatedBy: {retrieved.CreatedBy}");
        });
    }

    /// <summary>
    /// Proves, against a REAL SQLite database (not fakeDb), that when <c>CreateAsync</c> fails
    /// because the database genuinely rejects the write (a real unique-constraint violation on
    /// the primary key), the audit fields that were mutated in-memory while building the INSERT
    /// — before the failing execution — are restored to their pre-attempt values rather than left
    /// showing a fake "success" timestamp/user for a row that was never persisted.
    /// <para>
    /// This exercises <c>BaseTableGateway.Audit.cs</c>'s <c>SnapshotAuditFields</c>/
    /// <c>RestoreAuditFields</c> machinery through the real
    /// <c>catch { RestoreAuditFields(...); throw; }</c> path in
    /// <c>TableGateway.Core.cs</c>'s <c>CreateAsync</c> — previously validated only via
    /// fakeDb-injected exceptions, never a genuine provider-thrown constraint violation.
    /// </para>
    /// </summary>
    [SkippableFact]
    public Task CreateAsync_RealUniqueConstraintViolation_RestoresAuditFieldsToPreAttemptValues()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider != SupportedDatabase.Sqlite)
            {
                Output.WriteLine(
                    $"{provider}: skipped — this test targets SQLite specifically to validate the " +
                    "audit-field-restore-on-real-failure path with a fast, dependency-free real engine.");
                return;
            }

            var helper = new TableGateway<AuditedEntity, long>(context, GetAuditResolver());
            var sharedId = Interlocked.Increment(ref _nextId);

            var first = new AuditedEntity { Id = sharedId, Name = "First" };
            var firstCreated = await helper.CreateAsync(first, context);
            Assert.True(firstCreated);

            // Second entity reuses the same primary key — CreateAsync will mutate its audit
            // fields while building the INSERT, then the database will genuinely reject the
            // write with a real unique-constraint (primary key) violation.
            var duplicate = new AuditedEntity { Id = sharedId, Name = "Duplicate" };
            Assert.Equal(default, duplicate.CreatedAt);
            Assert.Equal(string.Empty, duplicate.CreatedBy);
            Assert.Equal(default, duplicate.UpdatedAt);
            Assert.Equal(string.Empty, duplicate.UpdatedBy);

            await Assert.ThrowsAsync<UniqueConstraintViolationException>(async () =>
                await helper.CreateAsync(duplicate, context));

            // The write never persisted — audit fields must be back to their pre-attempt
            // (default) values, not showing a fake "success" timestamp/user.
            Assert.Equal(default, duplicate.CreatedAt);
            Assert.Equal(string.Empty, duplicate.CreatedBy);
            Assert.Equal(default, duplicate.UpdatedAt);
            Assert.Equal(string.Empty, duplicate.UpdatedBy);

            Output.WriteLine($"{provider}: audit fields correctly restored after a real unique constraint violation");
        });
    }

    private static string BuildAuditTableSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = IntegrationObjectNameHelper.Table(context, "audited_entity");
        var idColumn = context.WrapObjectName("id");
        var nameColumn = context.WrapObjectName("name");
        var createdAtColumn = context.WrapObjectName("created_at");
        var createdByColumn = context.WrapObjectName("created_by");
        var updatedAtColumn = context.WrapObjectName("updated_at");
        var updatedByColumn = context.WrapObjectName("updated_by");

        var idType = GetIdType(provider);
        var stringType = GetStringType(provider);
        var dateType = GetDateTimeType(provider);

        return $@"
CREATE TABLE {table} (
    {idColumn} {idType} NOT NULL PRIMARY KEY,
    {nameColumn} {stringType} NOT NULL,
    {createdAtColumn} {dateType},
    {createdByColumn} {stringType},
    {updatedAtColumn} {dateType},
    {updatedByColumn} {stringType}
)";
    }

    private static string GetIdType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Oracle => "NUMBER(19)",
            _ => "BIGINT"
        };
    }

    private static string GetStringType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "TEXT",
            SupportedDatabase.SqlServer => "NVARCHAR(255)",
            SupportedDatabase.Oracle => "VARCHAR2(255)",
            SupportedDatabase.Firebird => "VARCHAR(255)",
            _ => "VARCHAR(255)"
        };
    }

    private static string GetDateTimeType(SupportedDatabase provider)
    {
        return provider switch
        {
            SupportedDatabase.Sqlite => "DATETIME",
            SupportedDatabase.SqlServer => "DATETIME2",
            SupportedDatabase.MySql => "DATETIME",
            SupportedDatabase.MariaDb => "DATETIME",
            _ => "TIMESTAMP"
        };
    }

    private static async Task EnsureFirebirdAuditTableAsync(IDatabaseContext context)
    {
        var tableName = "audited_entity";
        var wrappedTable = IntegrationObjectNameHelper.Table(context, tableName);
        if (!await FirebirdAuditTableExistsAsync(context))
        {
            var createSql = BuildAuditTableSql(SupportedDatabase.Firebird, context);
            await using var createContainer = context.CreateSqlContainer(createSql);
            await createContainer.ExecuteNonQueryAsync();
        }

        await using var deleteContainer = context.CreateSqlContainer($"DELETE FROM {wrappedTable}");
        await deleteContainer.ExecuteNonQueryAsync();
    }

    private static async Task<bool> FirebirdAuditTableExistsAsync(IDatabaseContext context)
    {
        const string query = @"
SELECT 1
FROM rdb$relations
WHERE lower(trim(rdb$relation_name)) = @name
  AND coalesce(rdb$system_flag, 0) = 0";

        await using var container = context.CreateSqlContainer(query);
        container.AddParameterWithValue("name", DbType.String, "audited_entity");
        var value = await container.ExecuteScalarOrNullAsync<object>();
        return value != null && !(value is DBNull);
    }
}

/// <summary>
/// Test entity with full audit field support
/// </summary>
[Table("audited_entity")]
public class AuditedEntity
{
    [Id][Column("id", DbType.Int64)] public long Id { get; set; }

    [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

    [CreatedOn]
    [Column("created_at", DbType.DateTime)]
    public DateTime CreatedAt { get; set; }

    [CreatedBy]
    [Column("created_by", DbType.String)]
    public string CreatedBy { get; set; } = string.Empty;

    [LastUpdatedOn]
    [Column("updated_at", DbType.DateTime)]
    public DateTime UpdatedAt { get; set; }

    [LastUpdatedBy]
    [Column("updated_by", DbType.String)]
    public string UpdatedBy { get; set; } = string.Empty;
}
