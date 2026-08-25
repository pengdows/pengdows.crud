// =============================================================================
// FILE: TableGatewayBatchTests.cs
// PURPOSE: Unit tests for TableGateway batch INSERT and UPSERT operations.
//
// AI SUMMARY:
// - Tests BuildBatchCreate(), BatchCreateAsync(), BuildBatchUpsert(), BatchUpsertAsync().
// - Covers all three batch upsert paths:
//   * ON CONFLICT (PostgreSQL/CockroachDB) — including WHERE ver = EXCLUDED.ver version predicate
//   * ON DUPLICATE KEY UPDATE (MySQL/MariaDB) — including alias quoting
//   * MERGE/per-entity fallback (SQL Server/Oracle/Firebird)
// - Key version concurrency tests:
//   * BuildBatchUpsert_PostgreSql_VersionColumn_AppendsOnConflictWhere — asserts WHERE predicate present
// - Chunking behavior: verifies split into multiple containers when parameter limit exceeded
// - NULL inlining: verifies NULL literal used (no parameter) for nullable null values
// - Audit field propagation in batch mode
// =============================================================================

#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
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

        // Regression: fakeDb's canned Postgres version (15.0) makes PostgreSqlDialect.SupportsMerge
        // true. The batch dispatch (TableGateway.Batch.cs) always prefers ON CONFLICT over MERGE for
        // batches — there is no batch-MERGE implementation — but the cached SET-clause fragment was
        // built using the SAME precedence as the single-entity dispatch (SupportsMerge first), so it
        // wrongly used the MERGE convention's "s." incoming-row alias, which doesn't exist in an
        // ON CONFLICT statement. Must use EXCLUDED.<col>, never "= s.".
        Assert.Contains("EXCLUDED.", sql);
        Assert.DoesNotContain(" = s.", sql, StringComparison.Ordinal);
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

    // Regression: BuildBatchUpdateSql's optimized (non-fallback) path for PostgreSQL, SQL Server,
    // and Snowflake generated a WHERE clause matching only on the key column(s) — never on
    // [Version] — and copied the client's PRE-update version value straight into SET instead of
    // incrementing it server-side. BatchUpdateAsync then wrote an INCREMENTED version back into
    // the in-memory entity regardless (see WriteBackIncrementedVersion), even though the database
    // row's version never actually changed. Net effect: the DB row's version silently never moves,
    // while the entity's in-memory Version races ahead — the very next single-entity UpdateAsync on
    // that entity builds its WHERE clause against the now-wrong in-memory version and throws a
    // spurious ConcurrencyConflictException with zero real concurrent writers involved. The existing
    // regression test for this scenario (BatchUpdateAsync_OneEntityHasStaleVersion...) runs only
    // against SQLite, which has SupportsBatchUpdate=false and silently falls back to the correct
    // single-entity BuildUpdate path per entity — so it never actually exercised this SQL shape.
    [Fact]
    public void BuildBatchUpdate_PostgreSql_VersionColumn_IncrementsSetAndAppendsWherePredicate()
    {
        var helper = new TableGateway<VersionedBatchEntity, int>(_pgContext, _audit);
        var entities = new List<VersionedBatchEntity>
        {
            new() { Id = 1, Name = "a", Version = 5 },
            new() { Id = 2, Name = "b", Version = 7 }
        };

        var containers = helper.BuildBatchUpdate(entities);
        var sql = containers[0].Query.ToString();
        var setClause = sql.Substring(0, sql.IndexOf(" FROM (VALUES", StringComparison.Ordinal));

        // SET must increment server-side, not copy the client's stale value.
        Assert.Contains("\"version\" = \"version\" + 1", setClause);
        Assert.DoesNotContain("\"version\" = s.\"version\"", setClause);
        // WHERE must compare against each row's pre-update version — otherwise a stale-version
        // update is indistinguishable from a fresh one at the SQL level.
        Assert.Contains("t.\"version\" = s.\"version\"", sql);
    }

    [Fact]
    public void BuildBatchUpdate_SqlServer_VersionColumn_IncrementsSetAndAppendsOnPredicate()
    {
        var helper = new TableGateway<VersionedBatchEntity, int>(_sqlServerContext, _audit);
        var entities = new List<VersionedBatchEntity>
        {
            new() { Id = 1, Name = "a", Version = 5 },
            new() { Id = 2, Name = "b", Version = 7 }
        };

        var containers = helper.BuildBatchUpdate(entities);
        var sql = containers[0].Query.ToString();
        var setClause = sql.Substring(sql.IndexOf("UPDATE SET ", StringComparison.Ordinal));

        Assert.Contains("\"version\" = \"version\" + 1", setClause);
        Assert.DoesNotContain("\"version\" = s.\"version\"", setClause);
        Assert.Contains("t.\"version\" = s.\"version\"", sql);
    }

    [Fact]
    public void BuildBatchUpdate_Snowflake_VersionColumn_IncrementsSetAndAppendsWherePredicate()
    {
        var helper = new TableGateway<VersionedBatchEntity, int>(_snowflakeContext, _audit);
        var entities = new List<VersionedBatchEntity>
        {
            new() { Id = 1, Name = "a", Version = 5 },
            new() { Id = 2, Name = "b", Version = 7 }
        };

        var containers = helper.BuildBatchUpdate(entities);
        var sql = containers[0].Query.ToString();

        Assert.Contains("\"version\" = \"version\" + 1", sql);
        // Snowflake's UPDATE target has no "t" alias (unlike Postgres/SqlServer) — the WHERE
        // predicate qualifies with the wrapped table name instead, so check the WHERE clause
        // specifically rather than the SET clause for the version comparison.
        var whereClause = sql.Substring(sql.IndexOf("WHERE", StringComparison.Ordinal));
        Assert.DoesNotContain("\"version\" = s.\"version\"", sql.Substring(0, sql.IndexOf("WHERE", StringComparison.Ordinal)));
        Assert.Contains(".\"version\" = s.\"version\"", whereClause);
    }

    [Fact]
    public void BuildBatchUpdate_SqlServer_OpaqueVersionColumn_AppendsPredicateButDoesNotIncrementInSet()
    {
        // Opaque (byte[]/RowVersion) version columns are server-generated on every write — the
        // database bumps them automatically, so unlike a numeric [Version] column, batch SQL must
        // NOT try to increment them in SET (there's nothing meaningful to add 1 to), but the
        // WHERE/ON predicate must still compare against the row's pre-update value to catch a
        // stale write.
        var helper = new TableGateway<OpaqueVersionedBatchEntity, int>(_sqlServerContext);
        var entities = new List<OpaqueVersionedBatchEntity>
        {
            new() { Id = 1, Name = "a", Version = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 } },
            new() { Id = 2, Name = "b", Version = new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 } }
        };

        var containers = helper.BuildBatchUpdate(entities);
        var sql = containers[0].Query.ToString();
        var setClause = sql.Substring(sql.IndexOf("UPDATE SET ", StringComparison.Ordinal));

        Assert.DoesNotContain("\"version\" = \"version\" + 1", setClause);
        Assert.Contains("t.\"version\" = s.\"version\"", sql);
    }

    // Regression: BuildBatchUpdate's native multi-row path never mirrored the single-row
    // UpdateAsync contract (TableGateway.Sql.cs) — WHERE keyed on [PrimaryKey] preferentially
    // over [Id], and the SET-clause column filter excluded [PrimaryKey] columns but never
    // excluded [CreatedBy]/[CreatedOn]. TestEntity has [Id(false)] and [PrimaryKey(1)] on
    // different columns (Id, Name) plus [CreatedBy]/[CreatedOn] — exactly the shape that
    // exposes both bugs at once.

    [Fact]
    public void BuildBatchUpdate_PostgreSql_ExcludesCreatedByAndCreatedOnFromSetClause()
    {
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "a" },
            new() { Id = 2, Name = "b" }
        };

        var containers = helper.BuildBatchUpdate(entities);
        var sql = containers[0].Query.ToString();
        var setClause = sql.Substring(0, sql.IndexOf(" FROM (VALUES", StringComparison.Ordinal));

        // CreatedBy/CreatedOn must never be touched by an UPDATE — only set once, on CREATE.
        Assert.DoesNotContain("\"CreatedBy\"", setClause);
        Assert.DoesNotContain("\"CreatedOn\"", setClause);
        // LastUpdatedBy/LastUpdatedOn ARE expected to be set on every UPDATE.
        Assert.Contains("\"LastUpdatedBy\" = s.\"LastUpdatedBy\"", setClause);
    }

    [Fact]
    public void BuildBatchUpdate_PostgreSql_KeysOnId_NotPrimaryKey_WhenBothPresent()
    {
        // Single-row UpdateAsync always keys WHERE on [Id] and treats [PrimaryKey] columns as
        // normal, updateable SET columns (TableGateway.Sql.cs ~line 197-198). Batch must match.
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "a" },
            new() { Id = 2, Name = "b" }
        };

        var containers = helper.BuildBatchUpdate(entities);
        var sql = containers[0].Query.ToString();
        var setClause = sql.Substring(0, sql.IndexOf(" FROM (VALUES", StringComparison.Ordinal));

        // WHERE/USING must key on Id, not Name (the [PrimaryKey] column).
        Assert.Contains("t.\"Id\" = s.\"Id\"", sql);
        Assert.DoesNotContain("t.\"Name\" = s.\"Name\"", sql);
        // Name (the PK column) remains a normal, updateable SET column.
        Assert.Contains("\"Name\" = s.\"Name\"", setClause);
    }

    [Fact]
    public void BuildBatchUpdate_PkOnlyEntityWithNoId_Throws()
    {
        // A [PrimaryKey]-only entity (no [Id]) is not a valid TableGateway<T,TId> update target
        // — single-row UpdateAsync already refuses this (TableGateway.Update.cs: "Single-ID
        // operations require a designated Id column; use composite-key helpers."). Batch must
        // refuse it too instead of silently falling back to keying on [PrimaryKey].
        var helper = new TableGateway<PkOnlyBatchEntity, int>(_pgContext);
        var entities = new List<PkOnlyBatchEntity>
        {
            new() { Code = "a", Name = "one" },
            new() { Code = "b", Name = "two" }
        };

        var ex = Assert.Throws<NotSupportedException>(() => helper.BuildBatchUpdate(entities));
        Assert.Contains("designated Id column", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void BuildBatchUpsert_PostgreSql_VersionColumn_AppendsOnConflictWhere()
    {
        // ON CONFLICT WHERE version predicate must appear in batch upsert SQL so stale-version
        // rows are skipped (DO NOTHING) rather than silently overwriting newer data.
        var helper = new TableGateway<TestEntity, int>(_pgContext, _audit);
        var entities = new List<TestEntity>
        {
            new() { Name = "a", version = 1 },
            new() { Name = "b", version = 2 }
        };

        var containers = helper.BuildBatchUpsert(entities);
        var sql = containers[0].Query.ToString();

        Assert.Contains("WHERE", sql);
        Assert.Contains("EXCLUDED", sql);
        // The version column name must appear after WHERE (not just in DO UPDATE SET)
        var whereIdx = sql.IndexOf("WHERE", StringComparison.Ordinal);
        var versionInWhere = sql.IndexOf("Version", whereIdx, StringComparison.OrdinalIgnoreCase);
        Assert.True(versionInWhere >= 0, "Version predicate expected after WHERE in ON CONFLICT clause");
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
    public async Task BatchUpsertAsync_SqlServer_OneEntityHasStaleVersion_ThrowsConcurrencyConflictExceptionAndRestoresAudit()
    {
        // Regression: like BatchUpdateAsync, BatchUpsertAsync's loop only ever accumulated
        // totalAffected. For SQL Server (MERGE, per-entity fallback bucket — see BuildBatchUpsert),
        // the WHEN MATCHED AND version guard is always present, so 0-rows-from-MERGE reliably means
        // a real conflict, exactly like single-entity UpsertAsync's own already-correct check
        // (UpsertAsyncTests.UpsertAsync_SqlServer_StaleVersion_ThrowsConcurrencyConflictException).
        var typeMap = new TypeMapRegistry();
        typeMap.Register<VersionedUpsertBatchEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var connection = new fakeDbConnection();
        connection.EnqueueNonQueryResult(1); // entity a: MERGE matches, succeeds
        connection.EnqueueNonQueryResult(0); // entity b: version mismatch → 0 rows from MERGE
        factory.Connections.Add(connection);
        var audit = new StubAuditValueResolver("batch-upsert-conflict-user");
        await using var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=test;EmulatedProduct=SqlServer", DbMode = DbMode.SingleConnection
            },
            factory, NullLoggerFactory.Instance, typeMap);
        var helper = new TableGateway<VersionedUpsertBatchEntity, int>(context, audit);

        var a = new VersionedUpsertBatchEntity { Id = 1, Name = "a", Version = 1 };
        var b = new VersionedUpsertBatchEntity { Id = 2, Name = "b", Version = 999 };
        var bLastUpdatedByBeforeCall = b.LastUpdatedBy;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => helper.BatchUpsertAsync(new[] { a, b }, context).AsTask());

        Assert.Equal(bLastUpdatedByBeforeCall, b.LastUpdatedBy);
    }

    [Fact]
    public async Task BatchUpsertAsync_MySql_ZeroAffectedRow_DoesNotThrow()
    {
        // MySQL's ON DUPLICATE KEY UPDATE has no version-guard WHERE clause, and the driver
        // reports 0-affected for a row whose column values didn't actually change (a routine
        // no-op upsert) — not a conflict. The conflict-detection fix for BatchUpsertAsync must not
        // treat this as ConcurrencyConflictException.
        var typeMap = new TypeMapRegistry();
        typeMap.Register<VersionedUpsertBatchEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var connection = new fakeDbConnection();
        connection.EnqueueNonQueryResult(1); // entity a: inserted/changed
        connection.EnqueueNonQueryResult(0); // entity b: no-op update (values unchanged), not a conflict
        factory.Connections.Add(connection);
        await using var context = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = "Server=localhost;EmulatedProduct=MySql", DbMode = DbMode.SingleConnection
            },
            factory, NullLoggerFactory.Instance, typeMap);
        var helper = new TableGateway<VersionedUpsertBatchEntity, int>(context);

        var a = new VersionedUpsertBatchEntity { Id = 1, Name = "a", Version = 1 };
        var b = new VersionedUpsertBatchEntity { Id = 2, Name = "b", Version = 1 };

        var ex = await Record.ExceptionAsync(() => helper.BatchUpsertAsync(new[] { a, b }, context).AsTask());

        Assert.Null(ex);
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

    [Fact]
    public async Task BatchUpdateAsync_SingleEntity_ExecutesUpdateSql()
    {
        await using var recordingContext = new RecordingBatchContext((DatabaseContext)_sqliteContext);
        var helper = new TableGateway<TestEntitySimple, int>(recordingContext);

        await helper.BatchUpdateAsync(new[]
        {
            new TestEntitySimple { Id = 1, Name = "updated" }
        });

        var container = Assert.Single(recordingContext.CreatedContainers);
        Assert.Contains("UPDATE", container.LastCommandText);
        Assert.DoesNotContain("INSERT INTO", container.LastCommandText);
    }

    [Fact]
    public async Task BatchUpdateAsync_MultipleEntities_ExecutesUpdateSql()
    {
        await using var recordingContext = new RecordingBatchContext((DatabaseContext)_sqliteContext);
        var helper = new TableGateway<TestEntitySimple, int>(recordingContext);

        await helper.BatchUpdateAsync(new[]
        {
            new TestEntitySimple { Id = 1, Name = "x" },
            new TestEntitySimple { Id = 2, Name = "y" }
        });

        Assert.NotEmpty(recordingContext.CreatedContainers);
        Assert.All(recordingContext.CreatedContainers, container =>
        {
            Assert.Contains("UPDATE", container.LastCommandText);
            Assert.DoesNotContain("INSERT INTO", container.LastCommandText);
        });
    }

    [Fact]
    public async Task BatchUpdateAsync_OneEntityHasStaleVersion_ThrowsConcurrencyConflictExceptionAndRestoresAudit()
    {
        // Regression: the batch loop only accumulated totalAffected and never inspected each
        // container's individual rows-affected — a stale-[Version] conflict on one entity in the
        // batch was invisible, and that entity's already-bumped audit fields were never restored.
        var typeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = true;
        await using var context =
            new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory, typeMap);
        var audit = new StubAuditValueResolver("batch-conflict-user");
        typeMap.Register<VersionedBatchEntity>();

        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;
        await context.CreateSqlContainer($@"CREATE TABLE IF NOT EXISTS {qp}versioned_batch{qs}(
            {qp}id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}name{qs} TEXT NOT NULL,
            {qp}version{qs} INTEGER NOT NULL DEFAULT 0,
            {qp}last_updated_by{qs} TEXT
        )").ExecuteNonQueryAsync();

        var helper = new TableGateway<VersionedBatchEntity, int>(context, audit);
        var a = new VersionedBatchEntity { Name = "a" };
        var b = new VersionedBatchEntity { Name = "b" };
        await helper.CreateAsync(a, context);
        await helper.CreateAsync(b, context);

        a.Name = "a-updated";
        b.Name = "b-updated";
        b.Version = 999; // Stale/wrong version — WHERE clause won't match, 0 rows affected for b.
        var bLastUpdatedByBeforeCall = b.LastUpdatedBy;

        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => helper.BatchUpdateAsync(new[] { a, b }, context).AsTask());

        Assert.Equal(bLastUpdatedByBeforeCall, b.LastUpdatedBy);

        // SQLite falls back to one BuildUpdate container per entity (SupportsBatchUpdate=false),
        // so this chunk contains exactly one entity — the conflict is unambiguously b's, and the
        // exception should say so by id rather than leaving the caller to guess which of the two
        // entities in the batch actually failed.
        Assert.Contains($"id={b.Id}", ex.Message);
    }

    [Fact]
    public async Task BatchUpdateAsync_MultiRowChunk_PartialConflict_MessageIsHonestAboutAttribution()
    {
        // On a real batched dialect (Postgres/SqlServer/Snowflake), a single UPDATE/MERGE
        // statement covers the whole chunk — if 3 of 5 rows match and 2 don't, there is no
        // RETURNING/OUTPUT used for batch operations (by design, for cross-dialect portability;
        // see this file's own header), so the failed row(s) genuinely cannot be individually
        // identified from the affected-row count alone. The exception must say so honestly
        // rather than implying an attribution it can't make, and must warn that entities in
        // this chunk — including any that succeeded server-side — were not written back.
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetNonQueryResult(3); // 3 of 5 "succeed" regardless of actual SQL/WHERE content.
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var context = new DatabaseContext(config, factory);
        var helper = new TableGateway<VersionedBatchEntity, int>(context, _audit);
        var entities = Enumerable.Range(1, 5)
            .Select(i => new VersionedBatchEntity { Id = i, Name = $"n{i}", Version = 1 })
            .ToList();

        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => helper.BatchUpdateAsync(entities, context).AsTask());

        Assert.DoesNotContain("id=", ex.Message);
        Assert.Contains("cannot be individually identified", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not written back", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchUpdateAsync_Success_WritesIncrementedVersionBackToEveryEntity()
    {
        // Regression: only single-entity UpdateAsync called WriteBackIncrementedVersion
        // (TableGateway.Core.cs). After a successful BatchUpdateAsync, entities kept their
        // pre-update in-memory Version — a later single UpdateAsync on the same entity would then
        // build its WHERE clause from the stale value and throw a spurious ConcurrencyConflictException.
        var typeMap = new TypeMapRegistry();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = true;
        await using var context =
            new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory, typeMap);
        var audit = new StubAuditValueResolver("batch-writeback-user");
        typeMap.Register<VersionedBatchEntity>();

        var qp = context.QuotePrefix;
        var qs = context.QuoteSuffix;
        await context.CreateSqlContainer($@"CREATE TABLE IF NOT EXISTS {qp}versioned_batch{qs}(
            {qp}id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}name{qs} TEXT NOT NULL,
            {qp}version{qs} INTEGER NOT NULL DEFAULT 0,
            {qp}last_updated_by{qs} TEXT
        )").ExecuteNonQueryAsync();

        var helper = new TableGateway<VersionedBatchEntity, int>(context, audit);
        var a = new VersionedBatchEntity { Name = "a" };
        var b = new VersionedBatchEntity { Name = "b" };
        await helper.CreateAsync(a, context);
        await helper.CreateAsync(b, context);
        Assert.Equal(1, a.Version);
        Assert.Equal(1, b.Version);

        a.Name = "a-updated";
        b.Name = "b-updated";

        var affected = await helper.BatchUpdateAsync(new[] { a, b }, context);

        Assert.Equal(2, affected);
        Assert.Equal(2, a.Version);
        Assert.Equal(2, b.Version);
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

    [Table("versioned_batch")]
    public class VersionedBatchEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("version", DbType.Int32)]
        public int Version { get; set; }

        [LastUpdatedBy]
        [Column("last_updated_by", DbType.String)]
        public string LastUpdatedBy { get; set; } = string.Empty;
    }

    [Table("pk_only_batch")]
    public class PkOnlyBatchEntity
    {
        [PrimaryKey(1)]
        [Column("code", DbType.String)]
        public string Code { get; set; } = string.Empty;

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    [Table("opaque_versioned_batch")]
    public class OpaqueVersionedBatchEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("version", DbType.Binary)]
        public byte[] Version { get; set; } = Array.Empty<byte>();
    }

    [Table("versioned_upsert_batch")]
    public class VersionedUpsertBatchEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("version", DbType.Int32)]
        public int Version { get; set; }

        [LastUpdatedBy]
        [Column("last_updated_by", DbType.String)]
        public string LastUpdatedBy { get; set; } = string.Empty;
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

        public string Name => _context.Name;

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
        public CommandPrepareMode PrepareMode => _context.PrepareMode;
        public bool SupportsInsertReturning => _context.SupportsInsertReturning;
        public string QuotePrefix => _context.QuotePrefix;
        public string QuoteSuffix => _context.QuoteSuffix;
        public string CompositeIdentifierSeparator => _context.CompositeIdentifierSeparator;
        public bool IsReadOnlyConnection => _context.IsReadOnlyConnection;
        public bool RCSIEnabled => _context.RCSIEnabled;
        public bool SnapshotIsolationEnabled => _context.SnapshotIsolationEnabled;
        public IReadOnlySet<IsolationLevel> GetSupportedIsolationLevels() => _context.GetSupportedIsolationLevels();
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
            ExecutionType executionType = ExecutionType.Write)
        {
            return _context.BeginTransaction(isolationLevel, executionType);
        }

        public ITransactionContext BeginTransaction(IsolationProfile isolationProfile,
            ExecutionType executionType = ExecutionType.Write,
            IsolationResolutionPolicy policy = IsolationResolutionPolicy.AllowHigher)
        {
            return _context.BeginTransaction(isolationProfile, executionType, policy);
        }

        public ValueTask<ITransactionContext> BeginTransactionAsync(IsolationLevel? isolationLevel = null,
            ExecutionType executionType = ExecutionType.Write,
            CancellationToken cancellationToken = default)
        {
            return _context.BeginTransactionAsync(isolationLevel, executionType, cancellationToken);
        }

        public ValueTask<ITransactionContext> BeginTransactionAsync(IsolationProfile isolationProfile,
            ExecutionType executionType = ExecutionType.Write,
            CancellationToken cancellationToken = default,
            IsolationResolutionPolicy policy = IsolationResolutionPolicy.AllowHigher)
        {
            return _context.BeginTransactionAsync(isolationProfile, executionType, cancellationToken, policy);
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
        public string LastCommandText { get; private set; } = string.Empty;
        public ISqlQueryBuilder Query => _inner.Query;
        public int ParameterCount => _inner.ParameterCount;
        public bool HasWhereAppended => _inner.HasWhereAppended;

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
        public ValueTask<int> ExecuteNonQueryAsync(CommandType commandType = CommandType.Text)
        {
            LastCommandText = Query.ToString();
            return _inner.ExecuteNonQueryAsync(commandType);
        }

        public ValueTask<int> ExecuteNonQueryAsync(CommandType commandType, CancellationToken cancellationToken)
        {
            LastCommandText = Query.ToString();
            return _inner.ExecuteNonQueryAsync(commandType, cancellationToken);
        }

        public ValueTask<int> ExecuteNonQueryAsync(ExecutionType executionType, CommandType commandType = CommandType.Text)
        {
            LastCommandText = Query.ToString();
            return _inner.ExecuteNonQueryAsync(executionType, commandType);
        }

        public ValueTask<int> ExecuteNonQueryAsync(ExecutionType executionType, CommandType commandType, CancellationToken cancellationToken)
        {
            LastCommandText = Query.ToString();
            return _inner.ExecuteNonQueryAsync(executionType, commandType, cancellationToken);
        }
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
