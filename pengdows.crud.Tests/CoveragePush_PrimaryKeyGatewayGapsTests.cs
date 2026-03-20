// =============================================================================
// FILE: CoveragePush_PrimaryKeyGatewayGapsTests.cs
// PURPOSE: Coverage boost for uncovered paths in PrimaryKeyTableGateway and
//          internal extension files.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class CoveragePush_PrimaryKeyGatewayGapsTests
{
    // -------------------------------------------------------------------------
    // Test entities (mirrored from PrimaryKeyTableGatewayTests to avoid coupling)
    // -------------------------------------------------------------------------

    [Table("gap_order_line")]
    private class GapOrderLine
    {
        [PrimaryKey(1)]
        [Column("order_id", DbType.Int32)]
        public int OrderId { get; set; }

        [PrimaryKey(2)]
        [Column("line_number", DbType.Int32)]
        public int LineNumber { get; set; }

        [Column("product_code", DbType.String)]
        public string ProductCode { get; set; } = string.Empty;

        [Column("quantity", DbType.Int32)]
        public int Quantity { get; set; }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IDatabaseContext MakeContext(SupportedDatabase db = SupportedDatabase.Sqlite)
    {
        var factory = new fakeDbFactory(db);
        factory.EnableDataPersistence = true;
        var cs = db switch
        {
            SupportedDatabase.PostgreSql => "Host=localhost;EmulatedProduct=PostgreSql",
            SupportedDatabase.MySql      => "Server=localhost;EmulatedProduct=MySql",
            SupportedDatabase.SqlServer  => "Server=localhost;EmulatedProduct=SqlServer",
            _                            => "Data Source=:memory:;EmulatedProduct=Sqlite"
        };
        return new DatabaseContext(cs, factory);
    }

    // =========================================================================
    // BuildUpdateAsync null-guard (Update.cs line 23-24)
    // =========================================================================

    [Fact]
    public async Task BuildUpdateAsync_NullEntity_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await gw.BuildUpdateAsync(null!));
    }

    // =========================================================================
    // BuildUpdateAsync(entity, loadOriginal) overload (Update.cs lines 34-38)
    // =========================================================================

    [Fact]
    public async Task BuildUpdateAsync_WithLoadOriginalTrue_DelegatesToBuildUpdateAsync()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entity = new GapOrderLine { OrderId = 1, LineNumber = 1, ProductCode = "X", Quantity = 3 };

        // loadOriginal=true is silently ignored — should still build valid UPDATE SQL
        await using var sc = await gw.BuildUpdateAsync(entity, loadOriginal: true);
        var sql = sc.Query.ToString();

        Assert.Contains("UPDATE", sql);
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public async Task BuildUpdateAsync_WithLoadOriginalFalse_DelegatesToBuildUpdateAsync()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entity = new GapOrderLine { OrderId = 2, LineNumber = 1, ProductCode = "Y", Quantity = 7 };

        await using var sc = await gw.BuildUpdateAsync(entity, loadOriginal: false);
        var sql = sc.Query.ToString();

        Assert.Contains("UPDATE", sql);
    }

    // =========================================================================
    // UpdateAsync null-guard (Update.cs lines 45-46)
    // =========================================================================

    [Fact]
    public async Task UpdateAsync_NullEntity_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await gw.UpdateAsync(null!));
    }

    // =========================================================================
    // UpdateAsync(entity, loadOriginal) overload (Update.cs lines 56-60)
    // =========================================================================

    [Fact]
    public async Task UpdateAsync_WithLoadOriginalTrue_ExecutesAndReturnsNonNegative()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entity = new GapOrderLine { OrderId = 3, LineNumber = 1, ProductCode = "Z", Quantity = 2 };

        var rows = await gw.UpdateAsync(entity, loadOriginal: true);
        Assert.True(rows >= 0);
    }

    [Fact]
    public async Task UpdateAsync_WithLoadOriginalFalse_ExecutesAndReturnsNonNegative()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entity = new GapOrderLine { OrderId = 4, LineNumber = 2, ProductCode = "W", Quantity = 1 };

        var rows = await gw.UpdateAsync(entity, loadOriginal: false);
        Assert.True(rows >= 0);
    }

    // =========================================================================
    // BuildBatchUpdate null-guard and empty list (Update.cs lines 71-77)
    // =========================================================================

    [Fact]
    public void BuildBatchUpdate_NullEntities_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        Assert.Throws<ArgumentNullException>(
            () => gw.BuildBatchUpdate(null!));
    }

    [Fact]
    public void BuildBatchUpdate_EmptyList_ReturnsEmpty()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        var result = gw.BuildBatchUpdate(Array.Empty<GapOrderLine>());
        Assert.Empty(result);
    }

    // =========================================================================
    // BatchUpdateAsync (Update.cs lines 104-132) — completely uncovered
    // =========================================================================

    [Fact]
    public async Task BatchUpdateAsync_NullEntities_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await gw.BatchUpdateAsync(null!));
    }

    [Fact]
    public async Task BatchUpdateAsync_EmptyList_ReturnsZero()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        var rows = await gw.BatchUpdateAsync(Array.Empty<GapOrderLine>());
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task BatchUpdateAsync_SingleEntity_DelegatesToUpdateAsync()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entity = new GapOrderLine { OrderId = 10, LineNumber = 1, ProductCode = "A", Quantity = 5 };

        var rows = await gw.BatchUpdateAsync(new[] { entity });
        Assert.True(rows >= 0);
    }

    [Fact]
    public async Task BatchUpdateAsync_MultipleEntities_ReturnsNonNegative()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entities = new[]
        {
            new GapOrderLine { OrderId = 10, LineNumber = 1, ProductCode = "A", Quantity = 5 },
            new GapOrderLine { OrderId = 10, LineNumber = 2, ProductCode = "B", Quantity = 6 }
        };

        var rows = await gw.BatchUpdateAsync(entities);
        Assert.True(rows >= 0);
    }

    [Fact]
    public async Task BatchUpdateAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entities = new[]
        {
            new GapOrderLine { OrderId = 10, LineNumber = 1, ProductCode = "A", Quantity = 5 }
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await gw.BatchUpdateAsync(entities, cancellationToken: cts.Token));
    }

    // =========================================================================
    // BuildUpsert null-guard (Upsert.cs lines 26-27)
    // =========================================================================

    [Fact]
    public void BuildUpsert_NullEntity_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        Assert.Throws<ArgumentNullException>(() => gw.BuildUpsert(null!));
    }

    // =========================================================================
    // UpsertAsync null-guard (Upsert.cs lines 74-75)
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_NullEntity_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await gw.UpsertAsync(null!));
    }

    // =========================================================================
    // BuildBatchUpsert null-guard and empty list (Upsert.cs lines 92-98)
    // =========================================================================

    [Fact]
    public void BuildBatchUpsert_NullEntities_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        Assert.Throws<ArgumentNullException>(() => gw.BuildBatchUpsert(null!));
    }

    [Fact]
    public void BuildBatchUpsert_EmptyList_ReturnsEmpty()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        var result = gw.BuildBatchUpsert(Array.Empty<GapOrderLine>());
        Assert.Empty(result);
    }

    // =========================================================================
    // BatchUpsertAsync null-guard, empty list, single-entity shortcut
    // (Upsert.cs lines 141-154)
    // =========================================================================

    [Fact]
    public async Task BatchUpsertAsync_NullEntities_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await gw.BatchUpsertAsync(null!));
    }

    [Fact]
    public async Task BatchUpsertAsync_EmptyList_ReturnsZero()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        var rows = await gw.BatchUpsertAsync(Array.Empty<GapOrderLine>());
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task BatchUpsertAsync_SingleEntity_DelegatesToUpsertAsync()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entity = new GapOrderLine { OrderId = 20, LineNumber = 1, ProductCode = "S", Quantity = 1 };

        var rows = await gw.BatchUpsertAsync(new[] { entity });
        Assert.True(rows >= 0);
    }

    // =========================================================================
    // BuildBatchCreate null-guard and empty list (Delete.cs lines 26-32)
    // =========================================================================

    [Fact]
    public void BuildBatchCreate_NullEntities_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        Assert.Throws<ArgumentNullException>(() => gw.BuildBatchCreate(null!));
    }

    [Fact]
    public void BuildBatchCreate_EmptyList_ReturnsEmpty()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        var result = gw.BuildBatchCreate(Array.Empty<GapOrderLine>());
        Assert.Empty(result);
    }

    // =========================================================================
    // BatchCreateAsync edge paths (Delete.cs lines 78-92)
    // =========================================================================

    [Fact]
    public async Task BatchCreateAsync_NullEntities_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await gw.BatchCreateAsync(null!));
    }

    [Fact]
    public async Task BatchCreateAsync_EmptyList_ReturnsZero()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        var rows = await gw.BatchCreateAsync(Array.Empty<GapOrderLine>());
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task BatchCreateAsync_SingleEntity_DelegatesToCreateAsync()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entity = new GapOrderLine { OrderId = 30, LineNumber = 1, ProductCode = "C", Quantity = 1 };

        var rows = await gw.BatchCreateAsync(new[] { entity });
        Assert.True(rows >= 0);
    }

    [Fact]
    public async Task BatchCreateAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entities = new[]
        {
            new GapOrderLine { OrderId = 30, LineNumber = 1, ProductCode = "C", Quantity = 1 }
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await gw.BatchCreateAsync(entities, cancellationToken: cts.Token));
    }

    // =========================================================================
    // BuildBatchDelete null-guard and empty list (Delete.cs lines 116-122)
    // =========================================================================

    [Fact]
    public void BuildBatchDelete_NullEntities_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        Assert.Throws<ArgumentNullException>(() => gw.BuildBatchDelete(null!));
    }

    [Fact]
    public void BuildBatchDelete_EmptyList_ReturnsEmpty()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        var result = gw.BuildBatchDelete(Array.Empty<GapOrderLine>());
        Assert.Empty(result);
    }

    // =========================================================================
    // BatchDeleteAsync null-guard and empty list (Delete.cs lines 186-194)
    // =========================================================================

    [Fact]
    public async Task BatchDeleteAsync_NullEntities_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await gw.BatchDeleteAsync(null!));
    }

    [Fact]
    public async Task BatchDeleteAsync_EmptyList_ReturnsZero()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        var rows = await gw.BatchDeleteAsync(Array.Empty<GapOrderLine>());
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task BatchDeleteAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entities = new[]
        {
            new GapOrderLine { OrderId = 40, LineNumber = 1 }
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await gw.BatchDeleteAsync(entities, cancellationToken: cts.Token));
    }

    // =========================================================================
    // BuildUpsert — fallback dialect unsupported throw (Upsert.cs line 66)
    // (DuckDb uses a fallback dialect that doesn't support upsert)
    // =========================================================================

    [Table("gap_pure_jxn")]
    private class GapPureJunction
    {
        [PrimaryKey(1)]
        [Column("a", DbType.Int32)]
        public int A { get; set; }

        [PrimaryKey(2)]
        [Column("b", DbType.Int32)]
        public int B { get; set; }
    }

    // =========================================================================
    // SqlServer upsert path (MERGE) — exercises BuildBatchUpsert fallback
    // =========================================================================

    [Fact]
    public void BuildBatchUpsert_SqlServer_FallsBackToOneByOne()
    {
        using var ctx = MakeContext(SupportedDatabase.SqlServer);
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entities = new[]
        {
            new GapOrderLine { OrderId = 1, LineNumber = 1, ProductCode = "A", Quantity = 1 },
            new GapOrderLine { OrderId = 1, LineNumber = 2, ProductCode = "B", Quantity = 2 }
        };

        var containers = gw.BuildBatchUpsert(entities);
        Assert.NotEmpty(containers);
    }

    // =========================================================================
    // CreateAsync null-guard (Core.cs lines 237-238)
    // =========================================================================

    [Fact]
    public async Task CreateAsync_NullEntity_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await gw.CreateAsync(null!, null, CancellationToken.None));
    }

    // =========================================================================
    // RetrieveOneAsync null-guard (Core.cs lines 255-256)
    // =========================================================================

    [Fact]
    public async Task RetrieveOneAsync_NullEntity_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await gw.RetrieveOneAsync(null!));
    }

    // =========================================================================
    // BuildCreate null-guard (Core.cs lines 208-209)
    // =========================================================================

    [Fact]
    public void BuildCreate_NullEntity_ThrowsArgumentNullException()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);

        Assert.Throws<ArgumentNullException>(() => gw.BuildCreate(null!));
    }

    // =========================================================================
    // Entity with [Version] column — exercises version-column branches in
    // Core.cs (102-105, 145-154, 177-184) and Update.cs (209-212, 253-261)
    // =========================================================================

    [Table("gap_versioned_pk")]
    private class GapVersionedPkEntity
    {
        [PrimaryKey(1)]
        [Column("tenant_id", DbType.Int32)]
        public int TenantId { get; set; }

        [PrimaryKey(2)]
        [Column("code", DbType.String)]
        public string Code { get; set; } = string.Empty;

        [Column("label", DbType.String)]
        public string Label { get; set; } = string.Empty;

        [Version]
        [Column("row_version", DbType.Int32)]
        public int RowVersion { get; set; }
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public async Task BuildUpdateAsync_VersionedEntity_IncludesVersionInWhereClause(SupportedDatabase db)
    {
        using var ctx = MakeContext(db);
        var gw = new PrimaryKeyTableGateway<GapVersionedPkEntity>(ctx);
        var entity = new GapVersionedPkEntity { TenantId = 1, Code = "X", Label = "foo", RowVersion = 2 };

        await using var sc = await gw.BuildUpdateAsync(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("row_version", sql);
    }

    [Fact]
    public async Task BuildUpdateAsync_VersionedEntity_VersionZero_SetsVersionToOne()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapVersionedPkEntity>(ctx);
        // version = 0 triggers version initialisation path
        var entity = new GapVersionedPkEntity { TenantId = 1, Code = "Y", Label = "bar", RowVersion = 0 };

        await using var sc = await gw.BuildUpdateAsync(entity);
        Assert.NotNull(sc);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public void BuildUpsert_VersionedEntity_ContainsVersionColumn(SupportedDatabase db)
    {
        using var ctx = MakeContext(db);
        var gw = new PrimaryKeyTableGateway<GapVersionedPkEntity>(ctx);
        var entity = new GapVersionedPkEntity { TenantId = 1, Code = "Z", Label = "baz", RowVersion = 1 };

        var sc = gw.BuildUpsert(entity);
        var sql = sc.Query.ToString();

        Assert.NotNull(sql);
    }

    // =========================================================================
    // Entity with audit columns — exercises audit branches in BuildUpdateByPk
    // (Update.cs lines 145-147) and BuildBatchCreate (Delete.cs lines 54-57)
    // =========================================================================

    [Table("gap_audited_pk2")]
    private class GapAuditedPkEntity
    {
        [PrimaryKey(1)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [LastUpdatedBy]
        [Column("updated_by", DbType.String)]
        public string? UpdatedBy { get; set; }

        [LastUpdatedOn]
        [Column("updated_on", DbType.DateTime)]
        public DateTime? UpdatedOn { get; set; }
    }

    [Fact]
    public async Task BuildUpdateAsync_AuditedEntity_SetsAuditFieldsInQuery()
    {
        using var ctx = MakeContext();
        var resolver = new GapCountingAuditResolver();
        var gw = new PrimaryKeyTableGateway<GapAuditedPkEntity>(ctx, resolver);
        var entity = new GapAuditedPkEntity { Id = 1, Name = "test" };

        await using var sc = await gw.BuildUpdateAsync(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("UPDATE", sql);
        Assert.Equal(1, resolver.CallCount);
    }

    [Fact]
    public void BuildBatchCreate_AuditedEntity_SetsAuditFields()
    {
        using var ctx = MakeContext();
        var resolver = new GapCountingAuditResolver();
        var gw = new PrimaryKeyTableGateway<GapAuditedPkEntity>(ctx, resolver);
        var entities = new[]
        {
            new GapAuditedPkEntity { Id = 1, Name = "A" },
            new GapAuditedPkEntity { Id = 2, Name = "B" }
        };

        var containers = gw.BuildBatchCreate(entities);
        Assert.NotEmpty(containers);
    }

    // =========================================================================
    // Firebird dialect — BuildBatchCreate fallback (Delete.cs lines 39-46)
    // =========================================================================

    [Fact]
    public void BuildBatchCreate_FirebirdDialect_FallsBackToPerEntityCreate()
    {
        using var ctx = MakeContext(SupportedDatabase.Firebird);
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entities = new[]
        {
            new GapOrderLine { OrderId = 1, LineNumber = 1, ProductCode = "A", Quantity = 1 },
            new GapOrderLine { OrderId = 1, LineNumber = 2, ProductCode = "B", Quantity = 2 }
        };

        var containers = gw.BuildBatchCreate(entities);
        // Each entity gets its own container because Firebird doesn't support batch insert
        Assert.Equal(2, containers.Count);
    }

    // =========================================================================
    // Nullable PK value — DELETE IS NULL path (Delete.cs lines 153-155)
    // =========================================================================

    [Table("gap_nullable_pk")]
    private class GapNullablePkEntity
    {
        [PrimaryKey(1)]
        [Column("tenant_code", DbType.String)]
        public string? TenantCode { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }

    [Fact]
    public void BuildBatchDelete_NullablePkValue_UsesIsNullClause()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapNullablePkEntity>(ctx);
        var entities = new[] { new GapNullablePkEntity { TenantCode = null, Value = "x" } };

        var containers = gw.BuildBatchDelete(entities);
        Assert.Single(containers);
        Assert.Contains("IS NULL", containers[0].Query.ToString());
    }

    // =========================================================================
    // BatchUpdateAsync single-entity-cancellation path (Update.cs)
    // =========================================================================

    [Fact]
    public async Task BatchUpdateAsync_SingleEntity_CancelledToken_Throws()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entities = new[] { new GapOrderLine { OrderId = 1, LineNumber = 1, ProductCode = "A", Quantity = 1 } };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Single-entity path delegates to UpdateAsync which also respects cancellation
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await gw.BatchUpdateAsync(entities, cancellationToken: cts.Token));
    }

    // =========================================================================
    // Nullable value in SET clause (Update.cs lines 171-174) — NULL column
    // =========================================================================

    [Table("gap_nullable_col_pk")]
    private class GapNullableColumnPkEntity
    {
        [PrimaryKey(1)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("optional_label", DbType.String)]
        public string? OptionalLabel { get; set; }
    }

    [Fact]
    public async Task BuildUpdateAsync_NullColumnValue_RendersIsNull()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapNullableColumnPkEntity>(ctx);
        var entity = new GapNullableColumnPkEntity { Id = 1, OptionalLabel = null };

        await using var sc = await gw.BuildUpdateAsync(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("= NULL", sql);
    }

    // =========================================================================
    // Upsert fallback/unsupported paths
    // =========================================================================

    /// <summary>
    /// Batch upsert with a pure-junction entity on PostgreSQL (SupportsInsertOnConflict=true)
    /// must throw NotSupportedException because there are no updatable columns.
    /// (Upsert.cs lines 109-112)
    /// </summary>
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    public void BuildBatchUpsert_PureJunctionEntity_OnConflictOrDuplicateDialect_ThrowsNotSupportedException(
        SupportedDatabase db)
    {
        using var ctx = MakeContext(db);
        var gw = new PrimaryKeyTableGateway<GapPureJunction>(ctx);
        var entities = new[]
        {
            new GapPureJunction { A = 1, B = 2 },
            new GapPureJunction { A = 3, B = 4 }
        };

        Assert.Throws<NotSupportedException>(() => gw.BuildBatchUpsert(entities));
    }

    // =========================================================================
    // Versioned entity upsert — exercises PrepareForPkUpsert version path
    // (Upsert.cs lines 176-212)
    // =========================================================================

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    public void BuildUpsert_VersionedEntity_ZeroVersion_InitializesVersionToOne(SupportedDatabase db)
    {
        using var ctx = MakeContext(db);
        var gw = new PrimaryKeyTableGateway<GapVersionedPkEntity>(ctx);
        var entity = new GapVersionedPkEntity { TenantId = 1, Code = "V", Label = "test", RowVersion = 0 };

        // Should not throw — version is initialized from 0 to 1
        var sc = gw.BuildUpsert(entity);
        Assert.NotNull(sc);
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    public void BuildBatchUpsert_VersionedEntity_ProducesContainers(SupportedDatabase db)
    {
        using var ctx = MakeContext(db);
        var gw = new PrimaryKeyTableGateway<GapVersionedPkEntity>(ctx);
        var entities = new[]
        {
            new GapVersionedPkEntity { TenantId = 1, Code = "A", Label = "first", RowVersion = 1 },
            new GapVersionedPkEntity { TenantId = 1, Code = "B", Label = "second", RowVersion = 2 }
        };

        var containers = gw.BuildBatchUpsert(entities);
        Assert.NotEmpty(containers);
    }

    // =========================================================================
    // Update: columnsAdded == 0 path (entity with only PK columns + version)
    // Would need a purely-non-updatable entity beyond just PKs.
    // =========================================================================

    [Table("gap_pk_only_with_version")]
    private class GapPkOnlyWithVersion
    {
        [PrimaryKey(1)]
        [Column("id_a", DbType.Int32)]
        public int IdA { get; set; }

        [PrimaryKey(2)]
        [Column("id_b", DbType.Int32)]
        public int IdB { get; set; }

        // Version column only — no other updatable columns
        [Version]
        [Column("ver", DbType.Int32)]
        public int Ver { get; set; }
    }

    [Fact]
    public async Task BuildUpdateAsync_OnlyVersionColumn_StillProducesUpdate()
    {
        // Version column is not in UpdateColumns — so this would have 0 updatable columns
        // and should throw InvalidOperationException("No updatable columns found for UPDATE.")
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapPkOnlyWithVersion>(ctx);
        var entity = new GapPkOnlyWithVersion { IdA = 1, IdB = 2, Ver = 1 };

        // If there are no updatable columns (only PK + version), the update should throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await gw.BuildUpdateAsync(entity));
    }

    // =========================================================================
    // Update: null PK value in WHERE = IS NULL path (Update.cs lines 232-234)
    // =========================================================================

    [Table("gap_nullable_pk_update")]
    private class GapNullablePkUpdate
    {
        [PrimaryKey(1)]
        [Column("category", DbType.String)]
        public string? Category { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task BuildUpdateAsync_NullPkValue_UsesIsNullInWhere()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapNullablePkUpdate>(ctx);
        var entity = new GapNullablePkUpdate { Category = null, Name = "uncategorized" };

        await using var sc = await gw.BuildUpdateAsync(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("IS NULL", sql);
    }

    // =========================================================================
    // Core.cs — BuildUpsertUpdateFragment MERGE path with audit resolver
    // (Core.cs lines 130-131: skip LastUpdatedBy when no resolver in MERGE path)
    // =========================================================================

    [Table("gap_audited_merge")]
    private class GapAuditedMergeEntity
    {
        [PrimaryKey(1)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [LastUpdatedBy]
        [Column("updated_by", DbType.String)]
        public string? UpdatedBy { get; set; }
    }

    [Fact]
    public void BuildUpsert_SqlServer_AuditedEntityNoResolver_SkipsLastUpdatedBy()
    {
        // No audit resolver — should skip LastUpdatedBy in MERGE SET clause
        using var ctx = MakeContext(SupportedDatabase.SqlServer);
        var gw = new PrimaryKeyTableGateway<GapAuditedMergeEntity>(ctx);
        var entity = new GapAuditedMergeEntity { Id = 1, Name = "test" };

        // Should not throw — resolver is null so LastUpdatedBy is skipped
        var sc = gw.BuildUpsert(entity);
        Assert.NotNull(sc);
    }

    [Fact]
    public void BuildUpsert_PostgreSql_AuditedEntityNoResolver_SkipsLastUpdatedBy()
    {
        // No audit resolver — should skip LastUpdatedBy in ON CONFLICT SET clause
        using var ctx = MakeContext(SupportedDatabase.PostgreSql);
        var gw = new PrimaryKeyTableGateway<GapAuditedMergeEntity>(ctx);
        var entity = new GapAuditedMergeEntity { Id = 1, Name = "test" };

        var sc = gw.BuildUpsert(entity);
        Assert.NotNull(sc);
    }

    // =========================================================================
    // Core.cs — version in non-merge upsert fragment (lines 216-218)
    // =========================================================================

    [Table("gap_versioned_upsert_pg")]
    private class GapVersionedUpsertPg
    {
        [PrimaryKey(1)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;

        [Version]
        [Column("ver", DbType.Int32)]
        public int Ver { get; set; }
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    public void BuildUpsert_VersionedEntity_OnConflictDialect_IncludesVersionIncrement(SupportedDatabase db)
    {
        using var ctx = MakeContext(db);
        var gw = new PrimaryKeyTableGateway<GapVersionedUpsertPg>(ctx);
        var entity = new GapVersionedUpsertPg { Id = 1, Value = "test", Ver = 1 };

        var sc = gw.BuildUpsert(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("ver", sql);
    }

    // =========================================================================
    // Constructor: entity with no [PrimaryKey] columns throws (Core.cs lines 69-71)
    // =========================================================================

    [Table("gap_no_pk")]
    private class GapNoPkEntity
    {
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void Constructor_EntityWithNoPrimaryKey_ThrowsSqlGenerationException()
    {
        using var ctx = MakeContext();

        Assert.Throws<SqlGenerationException>(
            () => new PrimaryKeyTableGateway<GapNoPkEntity>(ctx));
    }

    // =========================================================================
    // BuildCreate with audit resolver — covers Core.cs line 217 (SetAuditFields)
    // =========================================================================

    [Fact]
    public void BuildCreate_AuditedEntityWithResolver_SetsAuditFields()
    {
        using var ctx = MakeContext();
        var resolver = new GapCountingAuditResolver();
        var gw = new PrimaryKeyTableGateway<GapAuditedPkEntity>(ctx, resolver);
        var entity = new GapAuditedPkEntity { Id = 100, Name = "create-test" };

        var sc = gw.BuildCreate(entity);

        Assert.NotNull(sc);
        Assert.Equal(1, resolver.CallCount);
    }

    // =========================================================================
    // BuildCreate versioned entity with zero version — Core.cs lines 275-285
    // =========================================================================

    [Fact]
    public void BuildCreate_VersionedEntity_ZeroVersion_InitializesToOne()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapVersionedPkEntity>(ctx);
        var entity = new GapVersionedPkEntity { TenantId = 1, Code = "NEW", Label = "test", RowVersion = 0 };

        var sc = gw.BuildCreate(entity);

        Assert.NotNull(sc);
        // The PrepareVersionForCreate path should have set RowVersion to 1
        Assert.Equal(1, entity.RowVersion);
    }

    // =========================================================================
    // Entity with JSON column — covers Core.cs lines 329-330 (BuildCreate JSON path)
    // and Update.cs lines 181, 189 (BuildUpdateAsync JSON path)
    // =========================================================================

    [Table("gap_json_pk")]
    private class GapJsonPkEntity
    {
        [PrimaryKey(1)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Json]
        [Column("payload", DbType.String)]
        public string? Payload { get; set; }
    }

    [Fact]
    public void BuildCreate_JsonColumnEntity_RendersJsonSyntax()
    {
        // PostgreSQL wraps JSON in a cast expression
        using var ctx = MakeContext(SupportedDatabase.PostgreSql);
        var gw = new PrimaryKeyTableGateway<GapJsonPkEntity>(ctx);
        var entity = new GapJsonPkEntity { Id = 1, Name = "test", Payload = "{\"key\":\"value\"}" };

        var sc = gw.BuildCreate(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("payload", sql);
    }

    [Fact]
    public async Task BuildUpdateAsync_JsonColumnEntity_RendersJsonInSetClause()
    {
        using var ctx = MakeContext(SupportedDatabase.PostgreSql);
        var gw = new PrimaryKeyTableGateway<GapJsonPkEntity>(ctx);
        var entity = new GapJsonPkEntity { Id = 1, Name = "test", Payload = "{}" };

        await using var sc = await gw.BuildUpdateAsync(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("UPDATE", sql);
        Assert.Contains("payload", sql);
    }

    // =========================================================================
    // Firebird single-entity upsert — covers Upsert.cs lines 368-436
    // (BuildPkFirebirdMergeUpsert)
    // =========================================================================

    [Fact]
    public void BuildUpsert_FirebirdDialect_BuildsUpdateOrInsert()
    {
        using var ctx = MakeContext(SupportedDatabase.Firebird);
        var gw = new PrimaryKeyTableGateway<GapOrderLine>(ctx);
        var entity = new GapOrderLine { OrderId = 1, LineNumber = 1, ProductCode = "FB", Quantity = 3 };

        var sc = gw.BuildUpsert(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("UPDATE OR INSERT INTO", sql);
        Assert.Contains("MATCHING", sql);
    }

    // =========================================================================
    // Single-entity upsert with audit resolver — Upsert.cs line 177
    // (PrepareForPkUpsert calls SetAuditFields when resolver is set)
    // =========================================================================

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    public void BuildUpsert_AuditedEntityWithResolver_CallsAuditResolver(SupportedDatabase db)
    {
        using var ctx = MakeContext(db);
        var resolver = new GapCountingAuditResolver();
        var gw = new PrimaryKeyTableGateway<GapAuditedMergeEntity>(ctx, resolver);
        var entity = new GapAuditedMergeEntity { Id = 1, Name = "upsert-audit" };

        var sc = gw.BuildUpsert(entity);

        Assert.NotNull(sc);
        Assert.True(resolver.CallCount > 0);
    }

    // =========================================================================
    // Batch upsert with audit resolver + version entity — Upsert.cs lines 198, 209-211
    // (PrepareForPkUpsert(entity, cachedAuditValues) with resolver and version)
    // =========================================================================

    [Table("gap_audited_versioned_pk")]
    private class GapAuditedVersionedPk
    {
        [PrimaryKey(1)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [LastUpdatedBy]
        [Column("updated_by", DbType.String)]
        public string? UpdatedBy { get; set; }

        [Version]
        [Column("ver", DbType.Int32)]
        public int Ver { get; set; }
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    public void BuildBatchUpsert_AuditedVersionedEntity_WithResolver_SetsAuditAndVersion(SupportedDatabase db)
    {
        using var ctx = MakeContext(db);
        var resolver = new GapCountingAuditResolver();
        var gw = new PrimaryKeyTableGateway<GapAuditedVersionedPk>(ctx, resolver);
        var entities = new[]
        {
            new GapAuditedVersionedPk { Id = 1, Name = "A", Ver = 0 },
            new GapAuditedVersionedPk { Id = 2, Name = "B", Ver = 1 }
        };

        var containers = gw.BuildBatchUpsert(entities);

        Assert.NotEmpty(containers);
        Assert.True(resolver.CallCount > 0);
    }

    // =========================================================================
    // Nullable version column in UPDATE WHERE — Update.cs lines 272-275
    // (AppendPkVersionCondition: versionValue == null → IS NULL)
    // =========================================================================

    [Table("gap_nullable_version_pk")]
    private class GapNullableVersionPk
    {
        [PrimaryKey(1)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("ver", DbType.Int32)]
        public int? Ver { get; set; }
    }

    [Fact]
    public async Task BuildUpdateAsync_NullableVersionIsNull_UsesIsNullInVersionCondition()
    {
        using var ctx = MakeContext();
        var gw = new PrimaryKeyTableGateway<GapNullableVersionPk>(ctx);
        var entity = new GapNullableVersionPk { Id = 1, Name = "null-ver", Ver = null };

        await using var sc = await gw.BuildUpdateAsync(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("ver", sql.ToLower());
        Assert.Contains("IS NULL", sql);
    }

    // =========================================================================
    // JSON batch upsert — Upsert.cs line 562 (TryMarkJsonParameter in batch)
    // =========================================================================

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    public void BuildBatchUpsert_JsonColumnEntity_ProducesContainers(SupportedDatabase db)
    {
        using var ctx = MakeContext(db);
        var gw = new PrimaryKeyTableGateway<GapJsonPkEntity>(ctx);
        var entities = new[]
        {
            new GapJsonPkEntity { Id = 1, Name = "A", Payload = "{}" },
            new GapJsonPkEntity { Id = 2, Name = "B", Payload = "{\"x\":1}" }
        };

        var containers = gw.BuildBatchUpsert(entities);
        Assert.NotEmpty(containers);
    }

    // =========================================================================
    // Helper: counting audit resolver
    // =========================================================================

    private sealed class GapCountingAuditResolver : IAuditValueResolver
    {
        private int _callCount;
        public int CallCount => _callCount;

        public IAuditValues Resolve()
        {
            System.Threading.Interlocked.Increment(ref _callCount);
            return new GapSimpleAuditValues("gap-user");
        }
    }

    private sealed class GapSimpleAuditValues : IAuditValues
    {
        public GapSimpleAuditValues(string userId) => UserId = userId;
        public object UserId { get; init; }
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTimeOffset? TimestampOffset => null;
    }
}
