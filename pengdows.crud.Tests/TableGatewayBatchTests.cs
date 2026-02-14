#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class TableGatewayBatchTests : IAsyncLifetime
{
    private readonly IDatabaseContext _sqliteContext;
    private readonly IDatabaseContext _pgContext;
    private readonly IDatabaseContext _mysqlContext;
    private readonly IDatabaseContext _sqlServerContext;
    private readonly TypeMapRegistry _typeMap;
    private readonly IAuditValueResolver _audit;

    public TableGatewayBatchTests()
    {
        _typeMap = new TypeMapRegistry();
        _audit = new StubAuditValueResolver("batch-test-user");

        var sqliteFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        sqliteFactory.EnableDataPersistence = true;
        _sqliteContext = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", sqliteFactory, _typeMap);

        var pgFactory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        pgFactory.EnableDataPersistence = true;
        _pgContext = new DatabaseContext("Host=localhost;EmulatedProduct=PostgreSql", pgFactory, _typeMap);

        var mysqlFactory = new fakeDbFactory(SupportedDatabase.MySql);
        mysqlFactory.EnableDataPersistence = true;
        _mysqlContext = new DatabaseContext("Server=localhost;EmulatedProduct=MySql", mysqlFactory, _typeMap);

        var sqlServerFactory = new fakeDbFactory(SupportedDatabase.SqlServer);
        sqlServerFactory.EnableDataPersistence = true;
        _sqlServerContext = new DatabaseContext("Server=localhost;EmulatedProduct=SqlServer", sqlServerFactory, _typeMap);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var ctx in new[] { _sqliteContext, _pgContext, _mysqlContext, _sqlServerContext })
        {
            if (ctx is IAsyncDisposable asyncDisp)
                await asyncDisp.DisposeAsync();
            else if (ctx is IDisposable disp)
                disp.Dispose();
        }
    }

    // =========================================================================
    // BatchCreateAsync — Empty & Single Entity Fast Paths
    // =========================================================================

    [Fact]
    public async Task BatchCreateAsync_EmptyList_ReturnsZero()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var result = await helper.BatchCreateAsync(Array.Empty<TestEntitySimple>());
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task BatchCreateAsync_NullList_Throws()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await helper.BatchCreateAsync(null!));
    }

    [Fact]
    public async Task BatchCreateAsync_SingleEntity_DelegatesToCreate()
    {
        // Single entity should use the fast path (same as CreateAsync)
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var entity = new TestEntitySimple { Name = "solo" };
        var result = await helper.BatchCreateAsync(new[] { entity });
        // Should succeed (returns affected row count)
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task BatchCreateAsync_SupportsCancellation()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await helper.BatchCreateAsync(
                new[] { new TestEntitySimple { Name = "test" } },
                null,
                cts.Token));
    }

    // =========================================================================
    // BuildBatchCreate — SQL Generation
    // =========================================================================

    [Fact]
    public void BuildBatchCreate_MultipleEntities_GeneratesMultiRowValues()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var entities = new List<TestEntitySimple>
        {
            new() { Name = "Alice" },
            new() { Name = "Bob" },
            new() { Name = "Charlie" }
        };

        var containers = helper.BuildBatchCreate(entities);
        Assert.Single(containers);

        var sql = containers[0].Query.ToString();
        // Should have multi-row VALUES with 3 tuples
        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("VALUES", sql);

        // Count the number of value tuple groups "(...),"
        var valueSection = sql.Substring(sql.IndexOf("VALUES", StringComparison.Ordinal));
        var tupleCount = valueSection.Count(c => c == '(');
        Assert.Equal(3, tupleCount);
    }

    [Fact]
    public void BuildBatchCreate_EmptyList_ReturnsEmptyContainerList()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var containers = helper.BuildBatchCreate(Array.Empty<TestEntitySimple>());
        Assert.Empty(containers);
    }

    [Fact]
    public void BuildBatchCreate_NullList_Throws()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        Assert.Throws<ArgumentNullException>(() => helper.BuildBatchCreate(null!));
    }

    [Fact]
    public void BuildBatchCreate_SetsAuditFields()
    {
        var helper = new TableGateway<TestEntity, int>(_sqliteContext, _audit);
        var entity = new TestEntity { Name = "audited" };

        var containers = helper.BuildBatchCreate(new[] { entity });

        // Audit fields should have been set on the entity
        Assert.Equal("batch-test-user", entity.CreatedBy);
        Assert.Equal("batch-test-user", entity.LastUpdatedBy);
        Assert.NotEqual(default, entity.CreatedOn);
        Assert.NotEqual(default, entity.LastUpdatedOn);
    }

    [Fact]
    public void BuildBatchCreate_SetsVersionToOne()
    {
        var helper = new TableGateway<TestEntity, int>(_sqliteContext, _audit);
        var entity = new TestEntity { Name = "versioned" };

        helper.BuildBatchCreate(new[] { entity });

        Assert.Equal(1, entity.version);
    }

    [Fact]
    public void BuildBatchCreate_ExcludesAutoIncrementId()
    {
        // TestEntitySimple has [Id(false)] → autoincrement, should NOT appear in INSERT columns
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var entities = new List<TestEntitySimple>
        {
            new() { Name = "test1" },
            new() { Name = "test2" }
        };

        var containers = helper.BuildBatchCreate(entities);
        var sql = containers[0].Query.ToString();

        // The "id" column should not appear in the column list
        // The column list is between INSERT INTO "table" ( ... ) VALUES
        var colSection = sql.Substring(0, sql.IndexOf("VALUES", StringComparison.Ordinal));
        Assert.DoesNotContain("\"id\"", colSection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildBatchCreate_ExcludesNonInsertableColumns()
    {
        var helper = new TableGateway<NonInsertableColumnEntity, int>(_sqliteContext);
        var entities = new List<NonInsertableColumnEntity>
        {
            new() { Id = 1, Name = "test1", Secret = "hidden" },
            new() { Id = 2, Name = "test2", Secret = "also hidden" }
        };

        var containers = helper.BuildBatchCreate(entities);
        var sql = containers[0].Query.ToString();

        // NonInsertable "Secret" column should not appear
        Assert.DoesNotContain("Secret", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildBatchCreate_HandlesNullValues()
    {
        var helper = new TableGateway<NullableTestEntity, int>(_sqliteContext);
        var entities = new List<NullableTestEntity>
        {
            new() { Name = "has-value", Description = "desc" },
            new() { Name = "null-desc", Description = null }
        };

        var containers = helper.BuildBatchCreate(entities);
        var sql = containers[0].Query.ToString();

        // NULL values should be inlined as NULL literal
        Assert.Contains("NULL", sql);
    }

    [Fact]
    public void BuildBatchCreate_ChunksWhenExceedingParameterLimit()
    {
        // SQLite has 999 parameter limit. With TestEntitySimple (1 insertable column: "name"),
        // we can fit 999 rows per chunk. With 1000 rows we should get 2 chunks.
        // But with more columns we need fewer entities. Let's use a concrete calculation:
        // TestEntitySimple insertable columns = 1 (name only, id is autoincrement)
        // usableParams = 999 * 0.9 = 899
        // rowsPerChunk = 899 / 1 = 899
        // So 900 entities should produce 2 chunks.
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var entities = Enumerable.Range(0, 900)
            .Select(i => new TestEntitySimple { Name = $"entity_{i}" })
            .ToList();

        var containers = helper.BuildBatchCreate(entities);
        Assert.True(containers.Count >= 2, $"Expected at least 2 chunks, got {containers.Count}");
    }

    [Fact]
    public void BuildBatchCreate_SingleEntity_ReturnsSingleContainer()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var containers = helper.BuildBatchCreate(new[] { new TestEntitySimple { Name = "solo" } });
        Assert.Single(containers);
    }

    [Fact]
    public void BuildBatchCreate_UsesCorrectParameterNaming()
    {
        // Parameters should use the batch counter prefix: b0, b1, b2, ...
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var entities = new List<TestEntitySimple>
        {
            new() { Name = "first" },
            new() { Name = "second" }
        };

        var containers = helper.BuildBatchCreate(entities);
        var sql = containers[0].Query.ToString();

        // SQLite uses @-prefixed parameter names
        Assert.Contains("@b0", sql);
        Assert.Contains("@b1", sql);
    }

    // =========================================================================
    // BuildBatchUpsert — Dialect-Specific SQL
    // =========================================================================

    [Fact]
    public void BuildBatchUpsert_PostgreSql_OnConflict()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        var entities = new List<TestEntity>
        {
            new() { Name = "upsert1" },
            new() { Name = "upsert2" }
        };

        var containers = helper.BuildBatchUpsert(entities);
        Assert.NotEmpty(containers);

        var sql = containers[0].Query.ToString();
        Assert.Contains("ON CONFLICT", sql);
        Assert.Contains("DO UPDATE SET", sql);
        Assert.Contains("VALUES", sql);
    }

    [Fact]
    public void BuildBatchUpsert_MySql_OnDuplicateKey()
    {
        var helper = new TableGateway<TestEntity, int>(_mysqlContext, _audit);
        var entities = new List<TestEntity>
        {
            new() { Name = "upsert1" },
            new() { Name = "upsert2" }
        };

        var containers = helper.BuildBatchUpsert(entities);
        Assert.NotEmpty(containers);

        var sql = containers[0].Query.ToString();
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql);
        Assert.Contains("VALUES", sql);
    }

    [Fact]
    public void BuildBatchUpsert_SqlServer_FallsBackToSingleRow()
    {
        // SQL Server uses MERGE which doesn't support multi-row VALUES practically,
        // so it should fall back to one container per entity
        var helper = new TableGateway<TestEntity, int>(_sqlServerContext, _audit);
        var entities = new List<TestEntity>
        {
            new() { Name = "upsert1" },
            new() { Name = "upsert2" },
            new() { Name = "upsert3" }
        };

        var containers = helper.BuildBatchUpsert(entities);
        // Should return one container per entity (individual MERGE statements)
        Assert.Equal(3, containers.Count);

        foreach (var container in containers)
        {
            var sql = container.Query.ToString();
            Assert.Contains("MERGE", sql);
        }
    }

    [Fact]
    public void BuildBatchUpsert_NoKey_Throws()
    {
        // Entity without PrimaryKey or writable Id cannot be upserted
        var helper = new TableGateway<NoKeyEntity, int>(_pgContext);
        var entities = new List<NoKeyEntity>
        {
            new() { Value = "test" }
        };

        Assert.Throws<NotSupportedException>(() => helper.BuildBatchUpsert(entities));
    }

    [Fact]
    public void BuildBatchUpsert_EmptyList_ReturnsEmptyContainerList()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        var containers = helper.BuildBatchUpsert(Array.Empty<TestEntity>());
        Assert.Empty(containers);
    }

    [Fact]
    public void BuildBatchUpsert_NullList_Throws()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        Assert.Throws<ArgumentNullException>(() => helper.BuildBatchUpsert(null!));
    }

    [Fact]
    public void BuildBatchUpsert_VersionColumn_IncrementOnUpdate()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        var entities = new List<TestEntity>
        {
            new() { Name = "versioned1" }
        };

        var containers = helper.BuildBatchUpsert(entities);
        var sql = containers[0].Query.ToString();

        // Version increment should appear in the ON CONFLICT ... DO UPDATE SET portion
        Assert.Contains("Version", sql);
        Assert.Contains("+ 1", sql);
    }

    // =========================================================================
    // BatchUpsertAsync
    // =========================================================================

    [Fact]
    public async Task BatchUpsertAsync_EmptyList_ReturnsZero()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        var result = await helper.BatchUpsertAsync(Array.Empty<TestEntity>());
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task BatchUpsertAsync_NullList_Throws()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await helper.BatchUpsertAsync(null!));
    }

    [Fact]
    public async Task BatchUpsertAsync_SupportsCancellation()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await helper.BatchUpsertAsync(
                new[] { new TestEntity { Name = "test" } },
                null,
                cts.Token));
    }

    // =========================================================================
    // Test Entities for batch-specific scenarios
    // =========================================================================

    [Table("nullable_test")]
    public class NullableTestEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Column("description", DbType.String)]
        public string? Description { get; set; }
    }

    [Table("no_key")]
    public class NoKeyEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }
}
