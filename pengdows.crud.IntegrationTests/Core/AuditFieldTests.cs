using System.Data;
using System.Threading;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Integration tests for audit field auto-population across supported database providers.
/// </summary>
public class AuditFieldTests : DatabaseTestBase
{
    private static long _nextId;

    public AuditFieldTests(ITestOutputHelper output) : base(output) { }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.TypeMapRegistry.Register<AuditedEntity>();

        await DropTableIfExistsAsync(context, "audited_entity");

        var createSql = BuildAuditTableSql(provider, context);
        using var container = context.CreateSqlContainer(createSql);
        await container.ExecuteNonQueryAsync();
    }

    [Fact]
    public Task CreateAsync_PopulatesCreatedByAndCreatedOn()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new EntityHelper<AuditedEntity, long>(context, auditValueResolver: GetAuditResolver());
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
            Assert.NotEqual(default(DateTime), retrieved.CreatedAt);
            Assert.True(retrieved.CreatedAt.Year >= 2024,
                $"CreatedAt {retrieved.CreatedAt} should be a reasonable date");

            Output.WriteLine($"{provider}: CreatedBy: {retrieved.CreatedBy}");
            Output.WriteLine($"{provider}: CreatedAt: {retrieved.CreatedAt}");
        });
    }

    [Fact]
    public Task CreateAsync_AlsoPopulatesLastUpdatedFields()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new EntityHelper<AuditedEntity, long>(context, auditValueResolver: GetAuditResolver());
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

    [Fact]
    public Task UpdateAsync_PopulatesLastUpdatedByAndLastUpdatedOn()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new EntityHelper<AuditedEntity, long>(context, auditValueResolver: GetAuditResolver());
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

    [Fact]
    public Task UpdateAsync_DoesNotModifyCreatedFields()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new EntityHelper<AuditedEntity, long>(context, auditValueResolver: GetAuditResolver());
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

    [Fact]
    public Task MultipleUpdates_UpdatesLastUpdatedFieldsEachTime()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new EntityHelper<AuditedEntity, long>(context, auditValueResolver: GetAuditResolver());
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

    [Fact]
    public Task CreateAsync_WithoutAuditResolver_ThrowsForUserAuditFields()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new EntityHelper<AuditedEntity, long>(context);
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

    [Fact]
    public Task UpsertAsync_NewEntity_PopulatesCreatedFields()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new EntityHelper<AuditedEntity, long>(context, auditValueResolver: GetAuditResolver());
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

    private static string BuildAuditTableSql(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = context.WrapObjectName("audited_entity");
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
    {idColumn} {idType} PRIMARY KEY,
    {nameColumn} {stringType} NOT NULL,
    {createdAtColumn} {dateType},
    {createdByColumn} {stringType},
    {updatedAtColumn} {dateType},
    {updatedByColumn} {stringType}
)";
    }

    private static string GetIdType(SupportedDatabase provider) =>
        provider switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Oracle => "NUMBER(19)",
            _ => "BIGINT"
        };

    private static string GetStringType(SupportedDatabase provider) =>
        provider switch
        {
            SupportedDatabase.Sqlite => "TEXT",
            SupportedDatabase.SqlServer => "NVARCHAR(255)",
            SupportedDatabase.Oracle => "VARCHAR2(255)",
            SupportedDatabase.Firebird => "VARCHAR(255)",
            _ => "VARCHAR(255)"
        };

    private static string GetDateTimeType(SupportedDatabase provider) =>
        provider switch
        {
            SupportedDatabase.Sqlite => "TEXT",
            SupportedDatabase.SqlServer => "DATETIME2",
            SupportedDatabase.MySql => "DATETIME",
            SupportedDatabase.MariaDb => "DATETIME",
            _ => "TIMESTAMP"
        };
}

/// <summary>
/// Test entity with full audit field support
/// </summary>
[Table("audited_entity")]
public class AuditedEntity
{
    [Id]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [Column("name", DbType.String)]
    public string Name { get; set; } = string.Empty;

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
