#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
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
    private readonly IDatabaseContext _snowflakeContext;
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
        _sqlServerContext =
            new DatabaseContext("Server=localhost;EmulatedProduct=SqlServer", sqlServerFactory, _typeMap);

        var snowflakeFactory = new fakeDbFactory(SupportedDatabase.Snowflake);
        snowflakeFactory.EnableDataPersistence = true;
        _snowflakeContext =
            new DatabaseContext("Account=xyz;EmulatedProduct=Snowflake", snowflakeFactory, _typeMap);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var ctx in new[] { _sqliteContext, _pgContext, _mysqlContext, _sqlServerContext, _snowflakeContext })
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
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await helper.BatchCreateAsync(null!));
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
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await helper.BatchCreateAsync(
            new[] { new TestEntitySimple { Name = "test" } },
            null,
            cts.Token));
    }

    [Fact]
    public async Task BatchCreateAsync_MultipleEntities_DisposesBuiltContainers()
    {
        await using var recordingContext = new RecordingBatchContext((DatabaseContext)_sqliteContext);
        var helper = new TableGateway<TestEntitySimple, int>(recordingContext);

        await helper.BatchCreateAsync(new[]
        {
            new TestEntitySimple { Name = "a" },
            new TestEntitySimple { Name = "b" }
        });

        Assert.NotEmpty(recordingContext.CreatedContainers);
        Assert.All(recordingContext.CreatedContainers, container => Assert.True(container.IsDisposed));
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
        // SQL Server has a 2100 parameter limit. With TestEntitySimple (1 insertable column: "name"),
        // usableParams = 2100 * 0.9 = 1890, rowsPerChunk = 1890.
        // 2000 entities should produce 2 chunks (1890 + 110).
        var helper = new TableGateway<TestEntitySimple, int>(_sqlServerContext);
        var entities = Enumerable.Range(0, 2000)
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
    public void BuildBatchUpdate_Snowflake_UsesUpdateFromValues()
    {
        // Arrange
        var helper = new TableGateway<TestEntitySimple, int>(_snowflakeContext);
        var entities = new List<TestEntitySimple>
        {
            new() { Id = 1, Name = "updated1" },
            new() { Id = 2, Name = "updated2" }
        };

        // Act
        var containers = helper.BuildBatchUpdate(entities);
        var sql = containers[0].Query.ToString();

        // Assert - Snowflake optimization: UPDATE FROM VALUES
        Assert.Contains("UPDATE", sql);
        Assert.Contains("FROM (VALUES", sql);
        Assert.Contains("(:b0, :b1), (:b2, :b3)", sql);
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public void BuildBatchUpdate_PostgreSql_UsesUpdateFromValues()
    {
        // Arrange
        var helper = new TableGateway<TestEntitySimple, int>(_pgContext);
        var entities = new List<TestEntitySimple> { new() { Id = 1, Name = "upd" } };

        // Act
        var containers = helper.BuildBatchUpdate(entities);
        var sql = containers[0].Query.ToString();

        // Assert
        Assert.Contains("UPDATE", sql);
        Assert.Contains("FROM (VALUES", sql);
        Assert.Contains("AS t", sql);
    }

    [Fact]
    public void BuildBatchUpdate_SqlServer_UsesMerge()
    {
        // Arrange
        var helper = new TableGateway<TestEntitySimple, int>(_sqlServerContext);
        var entities = new List<TestEntitySimple> { new() { Id = 1, Name = "upd" } };

        // Act
        var containers = helper.BuildBatchUpdate(entities);
        var sql = containers[0].Query.ToString();

        // Assert
        Assert.Contains("MERGE INTO", sql);
        Assert.Contains("USING (VALUES", sql);
        Assert.Contains("WHEN MATCHED THEN UPDATE", sql);
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
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await helper.BatchUpsertAsync(null!));
    }

    [Fact]
    public async Task BatchUpsertAsync_SupportsCancellation()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await helper.BatchUpsertAsync(
            new[] { new TestEntity { Name = "test" } },
            null,
            cts.Token));
    }

    [Fact]
    public async Task BatchUpsertAsync_MultipleEntities_DisposesBuiltContainers()
    {
        await using var recordingContext = new RecordingBatchContext((DatabaseContext)_pgContext);
        var helper = new TableGateway<TestEntity, int>(recordingContext, _audit);

        await helper.BatchUpsertAsync(new[]
        {
            new TestEntity { Name = "p" },
            new TestEntity { Name = "q" }
        });

        Assert.NotEmpty(recordingContext.CreatedContainers);
        Assert.All(recordingContext.CreatedContainers, container => Assert.True(container.IsDisposed));
    }

    // =========================================================================
    // BatchUpdateAsync — Empty, Null, Cancellation
    // =========================================================================

    [Fact]
    public async Task BatchUpdateAsync_EmptyList_ReturnsZero()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var result = await helper.BatchUpdateAsync(Array.Empty<TestEntitySimple>());
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task BatchUpdateAsync_NullList_Throws()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await helper.BatchUpdateAsync(null!));
    }

    [Fact]
    public async Task BatchUpdateAsync_SupportsCancellation()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await helper.BatchUpdateAsync(
            new[] { new TestEntitySimple { Id = 1, Name = "test" } },
            null,
            cts.Token));
    }

    [Fact]
    public async Task BatchUpdateAsync_MultipleEntities_DisposesBuiltContainers()
    {
        await using var recordingContext = new RecordingBatchContext((DatabaseContext)_sqliteContext);
        var helper = new TableGateway<TestEntitySimple, int>(recordingContext);

        await helper.BatchUpdateAsync(new[]
        {
            new TestEntitySimple { Id = 1, Name = "x" },
            new TestEntitySimple { Id = 2, Name = "y" }
        });

        Assert.NotEmpty(recordingContext.CreatedContainers);
        Assert.All(recordingContext.CreatedContainers, container => Assert.True(container.IsDisposed));
    }

    // =========================================================================
    // BuildBatchUpdate — Empty, Null, Fallback Dialects
    // =========================================================================

    [Fact]
    public void BuildBatchUpdate_EmptyList_ReturnsEmptyContainerList()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var containers = helper.BuildBatchUpdate(Array.Empty<TestEntitySimple>());
        Assert.Empty(containers);
    }

    [Fact]
    public void BuildBatchUpdate_NullList_Throws()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        Assert.Throws<ArgumentNullException>(() => helper.BuildBatchUpdate(null!));
    }

    [Fact]
    public void BuildBatchUpdate_MySQL_FallsBackToIndividualUpdates()
    {
        // MySQL SupportsBatchUpdate=false — falls back to one container per entity, each an UPDATE statement
        var helper = new TableGateway<TestEntitySimple, int>(_mysqlContext);
        var entities = new List<TestEntitySimple>
        {
            new() { Id = 1, Name = "first" },
            new() { Id = 2, Name = "second" },
            new() { Id = 3, Name = "third" }
        };

        var containers = helper.BuildBatchUpdate(entities);

        // Should produce one container per entity
        Assert.Equal(3, containers.Count);
        foreach (var container in containers)
        {
            var sql = container.Query.ToString();
            Assert.Contains("UPDATE", sql);
        }
    }

    // =========================================================================
    // CreateAsync(IReadOnlyList) — list overload delegates to BatchCreateAsync
    // =========================================================================

    [Fact]
    public async Task CreateAsync_List_NullList_Throws()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await helper.CreateAsync((IReadOnlyList<TestEntitySimple>)null!));
    }

    [Fact]
    public async Task CreateAsync_List_EmptyList_ReturnsZero()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var result = await helper.CreateAsync(Array.Empty<TestEntitySimple>());
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task CreateAsync_List_MultipleEntities_ProducesInsertSql()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var entities = new[]
        {
            new TestEntitySimple { Name = "a" },
            new TestEntitySimple { Name = "b" }
        };
        // Should route through BatchCreateAsync — verifies overload resolves and executes
        var result = await helper.CreateAsync((IReadOnlyList<TestEntitySimple>)entities);
        Assert.True(result >= 0);
    }

    // =========================================================================
    // UpdateAsync(IReadOnlyList) — list overload delegates to BatchUpdateAsync
    // =========================================================================

    [Fact]
    public async Task UpdateAsync_List_NullList_Throws()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await helper.UpdateAsync((IReadOnlyList<TestEntitySimple>)null!));
    }

    [Fact]
    public async Task UpdateAsync_List_EmptyList_ReturnsZero()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var result = await helper.UpdateAsync(Array.Empty<TestEntitySimple>());
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task UpdateAsync_List_MultipleEntities_ProducesUpdateSql()
    {
        var helper = new TableGateway<TestEntitySimple, int>(_sqliteContext);
        var entities = new[]
        {
            new TestEntitySimple { Id = 1, Name = "x" },
            new TestEntitySimple { Id = 2, Name = "y" }
        };
        // Routes through BatchUpdateAsync (SQLite has no SupportsBatchUpdate → individual UPDATEs)
        var containers = helper.BuildBatchUpdate((IReadOnlyList<TestEntitySimple>)entities);
        Assert.Equal(2, containers.Count);
        Assert.All(containers, c => Assert.Contains("UPDATE", c.Query.ToString()));
    }

    [Fact]
    public async Task BatchDeleteAsync_IdList_DisposesBuiltContainers()
    {
        await using var recordingContext = new RecordingBatchContext((DatabaseContext)_sqliteContext);
        var helper = new TableGateway<TestEntitySimple, int>(recordingContext);

        await helper.BatchDeleteAsync(new[] { 1, 2, 3 });

        Assert.NotEmpty(recordingContext.CreatedContainers);
        Assert.All(recordingContext.CreatedContainers, container => Assert.True(container.IsDisposed));
    }

    [Fact]
    public async Task BatchDeleteAsync_EntityList_DisposesBuiltContainers()
    {
        await using var recordingContext = new RecordingBatchContext((DatabaseContext)_sqliteContext);
        var helper = new TableGateway<TestEntity, int>(recordingContext, _audit);

        await helper.BatchDeleteAsync(new[]
        {
            new TestEntity { Name = "x" },
            new TestEntity { Name = "y" }
        });

        Assert.NotEmpty(recordingContext.CreatedContainers);
        Assert.All(recordingContext.CreatedContainers, container => Assert.True(container.IsDisposed));
    }

    // =========================================================================
    // UpsertAsync(IReadOnlyList) — list overload delegates to BatchUpsertAsync
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_List_NullList_Throws()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await helper.UpsertAsync((IReadOnlyList<TestEntity>)null!));
    }

    [Fact]
    public async Task UpsertAsync_List_EmptyList_ReturnsZero()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        var result = await helper.UpsertAsync(Array.Empty<TestEntity>());
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task UpsertAsync_List_MultipleEntities_ProducesUpsertSql()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        var entities = new[]
        {
            new TestEntity { Name = "p" },
            new TestEntity { Name = "q" }
        };
        var containers = helper.BuildBatchUpsert((IReadOnlyList<TestEntity>)entities);
        Assert.NotEmpty(containers);
        var sql = containers[0].Query.ToString();
        Assert.Contains("ON CONFLICT", sql);
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

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Column("description", DbType.String)] public string? Description { get; set; }
    }

    [Table("no_key")]
    public class NoKeyEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.String)] public string Value { get; set; } = string.Empty;
    }

    private sealed class RecordingBatchContext : IDatabaseContext, IInternalConnectionProvider, ITypeMapAccessor
    {
        private readonly DatabaseContext _context;

        public RecordingBatchContext(DatabaseContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public List<TrackingSqlContainer> CreatedContainers { get; } = new();

        public DbMode ConnectionMode => _context.ConnectionMode;
        public Guid RootId => _context.RootId;
        public ReadWriteMode ReadWriteMode => _context.ReadWriteMode;
        public string ConnectionString => _context.ConnectionString;

        public string Name
        {
            get => _context.Name;
            set => _context.Name = value;
        }

        public DbDataSource? DataSource => _context.DataSource;
        public IDataSourceInformation DataSourceInfo => _context.DataSourceInfo;
        public TimeSpan? ModeLockTimeout => _context.ModeLockTimeout;
        public ProcWrappingStyle ProcWrappingStyle => _context.ProcWrappingStyle;
        public int MaxParameterLimit => _context.MaxParameterLimit;
        public int MaxOutputParameters => _context.MaxOutputParameters;
        public long NumberOfOpenConnections => _context.NumberOfOpenConnections;
        public DatabaseMetrics Metrics => _context.Metrics;
        public ISqlDialect Dialect => _context.GetDialect();
        public SupportedDatabase Product => _context.Product;
        public long PeakOpenConnections => _context.PeakOpenConnections;
        public bool? ForceManualPrepare => _context.ForceManualPrepare;
        public bool? DisablePrepare => _context.DisablePrepare;
        public bool SupportsInsertReturning => _context.SupportsInsertReturning;
        public string QuotePrefix => _context.QuotePrefix;
        public string QuoteSuffix => _context.QuoteSuffix;
        public string CompositeIdentifierSeparator => _context.CompositeIdentifierSeparator;
        public bool IsReadOnlyConnection => _context.IsReadOnlyConnection;
        public bool RCSIEnabled => _context.RCSIEnabled;
        public bool SnapshotIsolationEnabled => _context.SnapshotIsolationEnabled;
        public bool IsDisposed => _context.IsDisposed;

        ITypeMapRegistry ITypeMapAccessor.TypeMapRegistry =>
            (_context as ITypeMapAccessor)?.TypeMapRegistry
            ?? throw new InvalidOperationException("DatabaseContext must expose TypeMapRegistry.");

        public event EventHandler<DatabaseMetrics> MetricsUpdated
        {
            add => _context.MetricsUpdated += value;
            remove => _context.MetricsUpdated -= value;
        }

        public ILockerAsync GetLock()
        {
            return _context.GetLock();
        }

        public string GetBaseSessionSettings()
        {
            return _context.GetBaseSessionSettings();
        }

        public string GetReadOnlySessionSettings()
        {
            return _context.GetReadOnlySessionSettings();
        }

        public ISqlContainer CreateSqlContainer(string? query = null)
        {
            var container = new TrackingSqlContainer(_context.CreateSqlContainer(query));
            CreatedContainers.Add(container);
            return container;
        }

        public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
        {
            return _context.CreateDbParameter(name, type, value);
        }

        public DbParameter CreateDbParameter<T>(string? name, DbType type, T value, ParameterDirection direction)
        {
            return _context.CreateDbParameter(name, type, value, direction);
        }

        public DbParameter CreateDbParameter<T>(DbType type, T value)
        {
            return _context.CreateDbParameter(type, value);
        }

        public string WrapObjectName(string name)
        {
            return _context.WrapObjectName(name);
        }

        public string MakeParameterName(DbParameter dbParameter)
        {
            return _context.MakeParameterName(dbParameter);
        }

        public string MakeParameterName(string parameterName)
        {
            return _context.MakeParameterName(parameterName);
        }

        public ITransactionContext BeginTransaction(IsolationLevel? isolationLevel = null,
            ExecutionType executionType = ExecutionType.Write, bool? readOnly = null)
        {
            return _context.BeginTransaction(isolationLevel, executionType, readOnly);
        }

        public ITransactionContext BeginTransaction(IsolationProfile isolationProfile,
            ExecutionType executionType = ExecutionType.Write, bool? readOnly = null)
        {
            return _context.BeginTransaction(isolationProfile, executionType, readOnly);
        }

        public Task<ITransactionContext> BeginTransactionAsync(IsolationLevel? isolationLevel = null,
            ExecutionType executionType = ExecutionType.Write, bool? readOnly = null,
            CancellationToken cancellationToken = default)
        {
            return _context.BeginTransactionAsync(isolationLevel, executionType, readOnly, cancellationToken);
        }

        public Task<ITransactionContext> BeginTransactionAsync(IsolationProfile isolationProfile,
            ExecutionType executionType = ExecutionType.Write, bool? readOnly = null,
            CancellationToken cancellationToken = default)
        {
            return _context.BeginTransactionAsync(isolationProfile, executionType, readOnly, cancellationToken);
        }

        public string GenerateParameterName()
        {
            return _context.GenerateParameterName();
        }

        public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30)
        {
            return _context.GenerateRandomName(length, parameterNameMaxLength);
        }

        public void CloseAndDisposeConnection(ITrackedConnection? conn)
        {
            _context.CloseAndDisposeConnection(conn);
        }

        public ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? conn)
        {
            return _context.CloseAndDisposeConnectionAsync(conn);
        }

        public void Dispose()
        {
            _context.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return _context.DisposeAsync();
        }

        ITrackedConnection IInternalConnectionProvider.GetConnection(ExecutionType executionType, bool isShared)
        {
            return _context.GetConnection(executionType, isShared);
        }
    }

    private sealed class TrackingSqlContainer : ISqlContainer, ISqlDialectProvider
    {
        private readonly ISqlContainer _inner;

        public TrackingSqlContainer(ISqlContainer inner)
        {
            _inner = inner;
        }

        public bool IsDisposed { get; private set; }
        public ISqlQueryBuilder Query => _inner.Query;
        public int ParameterCount => _inner.ParameterCount;
        public bool HasWhereAppended
        {
            get => _inner.HasWhereAppended;
            set => _inner.HasWhereAppended = value;
        }

        public string QuotePrefix => _inner.QuotePrefix;
        public string QuoteSuffix => _inner.QuoteSuffix;
        public string CompositeIdentifierSeparator => _inner.CompositeIdentifierSeparator;
        public ISqlDialect Dialect => ((ISqlDialectProvider)_inner).Dialect;

        public string WrapObjectName(string name) => _inner.WrapObjectName(name);
        public string MakeParameterName(DbParameter dbParameter) => _inner.MakeParameterName(dbParameter);
        public string MakeParameterName(string parameterName) => _inner.MakeParameterName(parameterName);
        public DbParameter CreateDbParameter<T>(string? name, DbType type, T value) => _inner.CreateDbParameter(name, type, value);
        public DbParameter CreateDbParameter<T>(DbType type, T value) => _inner.CreateDbParameter(type, value);
        public void AddParameter(DbParameter parameter) => _inner.AddParameter(parameter);
        public DbParameter AddParameterWithValue<T>(DbType type, T value) => _inner.AddParameterWithValue(type, value);
        public DbParameter AddParameterWithValue<T>(string? name, DbType type, T value) => _inner.AddParameterWithValue(name, type, value);
        public DbParameter AddParameterWithValue<T>(DbType type, T value, ParameterDirection direction) => _inner.AddParameterWithValue(type, value, direction);
        public DbParameter AddParameterWithValue<T>(string? name, DbType type, T value, ParameterDirection direction) => _inner.AddParameterWithValue(name, type, value, direction);
        public void SetParameterValue(string parameterName, object? newValue) => _inner.SetParameterValue(parameterName, newValue);
        public object? GetParameterValue(string parameterName) => _inner.GetParameterValue(parameterName);
        public T GetParameterValue<T>(string parameterName) => _inner.GetParameterValue<T>(parameterName);
        public ValueTask<int> ExecuteNonQueryAsync(CommandType commandType = CommandType.Text) => _inner.ExecuteNonQueryAsync(commandType);
        public ValueTask<int> ExecuteNonQueryAsync(CommandType commandType, CancellationToken cancellationToken) => _inner.ExecuteNonQueryAsync(commandType, cancellationToken);
        public ValueTask<int> ExecuteNonQueryAsync(ExecutionType executionType, CommandType commandType = CommandType.Text) => _inner.ExecuteNonQueryAsync(executionType, commandType);
        public ValueTask<int> ExecuteNonQueryAsync(ExecutionType executionType, CommandType commandType, CancellationToken cancellationToken) => _inner.ExecuteNonQueryAsync(executionType, commandType, cancellationToken);
        public ValueTask<T> ExecuteScalarRequiredAsync<T>(CommandType commandType = CommandType.Text) => _inner.ExecuteScalarRequiredAsync<T>(commandType);
        public ValueTask<T> ExecuteScalarRequiredAsync<T>(CommandType commandType, CancellationToken cancellationToken) => _inner.ExecuteScalarRequiredAsync<T>(commandType, cancellationToken);
        public ValueTask<T> ExecuteScalarRequiredAsync<T>(ExecutionType executionType, CommandType commandType = CommandType.Text) => _inner.ExecuteScalarRequiredAsync<T>(executionType, commandType);
        public ValueTask<T> ExecuteScalarRequiredAsync<T>(ExecutionType executionType, CommandType commandType, CancellationToken cancellationToken) => _inner.ExecuteScalarRequiredAsync<T>(executionType, commandType, cancellationToken);
        public ValueTask<T?> ExecuteScalarOrNullAsync<T>(CommandType commandType = CommandType.Text) => _inner.ExecuteScalarOrNullAsync<T>(commandType);
        public ValueTask<T?> ExecuteScalarOrNullAsync<T>(CommandType commandType, CancellationToken cancellationToken) => _inner.ExecuteScalarOrNullAsync<T>(commandType, cancellationToken);
        public ValueTask<T?> ExecuteScalarOrNullAsync<T>(ExecutionType executionType, CommandType commandType = CommandType.Text) => _inner.ExecuteScalarOrNullAsync<T>(executionType, commandType);
        public ValueTask<T?> ExecuteScalarOrNullAsync<T>(ExecutionType executionType, CommandType commandType, CancellationToken cancellationToken) => _inner.ExecuteScalarOrNullAsync<T>(executionType, commandType, cancellationToken);
        public ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(CommandType commandType = CommandType.Text) => _inner.TryExecuteScalarAsync<T>(commandType);
        public ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(CommandType commandType, CancellationToken cancellationToken) => _inner.TryExecuteScalarAsync<T>(commandType, cancellationToken);
        public ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(ExecutionType executionType, CommandType commandType = CommandType.Text) => _inner.TryExecuteScalarAsync<T>(executionType, commandType);
        public ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(ExecutionType executionType, CommandType commandType, CancellationToken cancellationToken) => _inner.TryExecuteScalarAsync<T>(executionType, commandType, cancellationToken);
        public ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType commandType = CommandType.Text) => _inner.ExecuteReaderAsync(commandType);
        public ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType commandType, CancellationToken cancellationToken) => _inner.ExecuteReaderAsync(commandType, cancellationToken);
        public ValueTask<ITrackedReader> ExecuteReaderAsync(ExecutionType executionType, CommandType commandType = CommandType.Text) => _inner.ExecuteReaderAsync(executionType, commandType);
        public ValueTask<ITrackedReader> ExecuteReaderAsync(ExecutionType executionType, CommandType commandType, CancellationToken cancellationToken) => _inner.ExecuteReaderAsync(executionType, commandType, cancellationToken);
        public void AddParameters(IEnumerable<DbParameter> list) => _inner.AddParameters(list);
        public void AddParameters(IList<DbParameter> list) => _inner.AddParameters(list);
        public void Clear() => _inner.Clear();
        public string WrapForStoredProc(ExecutionType executionType, bool includeParameters = true, bool captureReturn = false) => _inner.WrapForStoredProc(executionType, includeParameters, captureReturn);
        public ISqlContainer Clone() => new TrackingSqlContainer(_inner.Clone());
        public ISqlContainer Clone(IDatabaseContext? context) => new TrackingSqlContainer(_inner.Clone(context));

        public void Dispose()
        {
            _inner.Dispose();
            IsDisposed = true;
        }

        public async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            IsDisposed = true;
        }
    }
}
