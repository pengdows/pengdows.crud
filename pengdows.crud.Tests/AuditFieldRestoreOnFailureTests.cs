using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// SetAuditFields mutates CreatedOn/CreatedBy/LastUpdatedOn/LastUpdatedBy during Build — before
/// any SQL executes. If Execute then fails (or, for versioned entities, succeeds but affects 0
/// rows), the entity's audit fields would otherwise claim a write that never persisted. These
/// tests prove the convenience methods (Create/Update/Upsert, single-entity, on both
/// TableGateway and PrimaryKeyTableGateway) restore the pre-attempt audit values whenever the
/// write doesn't actually succeed. Batch variants are explicitly out of scope — see
/// docs/FUTURE_WORK.md for why (partial-batch-failure semantics need a different design).
/// </summary>
public class AuditFieldRestoreOnFailureTests
{
    [Table("audited_items")]
    private class AuditedItem
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedOn]
        [Column("created_on", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }

        [CreatedBy]
        [Column("created_by", DbType.String)]
        public string CreatedBy { get; set; } = string.Empty;

        [LastUpdatedOn]
        [Column("last_updated_on", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }

        [LastUpdatedBy]
        [Column("last_updated_by", DbType.String)]
        public string LastUpdatedBy { get; set; } = string.Empty;
    }

    [Table("audited_versioned_items")]
    private class AuditedVersionedItem
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("version", DbType.Int32)]
        public int Version { get; set; }

        [LastUpdatedOn]
        [Column("last_updated_on", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }

        [LastUpdatedBy]
        [Column("last_updated_by", DbType.String)]
        public string LastUpdatedBy { get; set; } = string.Empty;
    }

    [Table("audited_pk_items")]
    private class AuditedPkItem
    {
        [PrimaryKey(1)]
        [Column("key", DbType.Int32)]
        public int Key { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [CreatedOn]
        [Column("created_on", DbType.DateTime)]
        public DateTime CreatedOn { get; set; }

        [CreatedBy]
        [Column("created_by", DbType.String)]
        public string CreatedBy { get; set; } = string.Empty;

        [LastUpdatedOn]
        [Column("last_updated_on", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }

        [LastUpdatedBy]
        [Column("last_updated_by", DbType.String)]
        public string LastUpdatedBy { get; set; } = string.Empty;
    }

    private static DatabaseContext CreateContext(fakeDbFactory factory)
    {
        return new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
    }

    // ---------------- TableGateway ----------------

    [Fact]
    public async Task TableGateway_CreateAsync_RestoresAuditFieldsOnFailure()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new TableGateway<AuditedItem, int>(ctx, new StubAuditValueResolver("creator"));

        var entity = new AuditedItem { Id = 1, Name = "widget" };
        factory.SetNonQueryException(new InvalidOperationException("simulated failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.CreateAsync(entity, ctx).AsTask());

        Assert.Equal(default, entity.CreatedOn);
        Assert.Equal(string.Empty, entity.CreatedBy);
        Assert.Equal(default, entity.LastUpdatedOn);
        Assert.Equal(string.Empty, entity.LastUpdatedBy);
    }

    [Fact]
    public async Task TableGateway_UpdateAsync_RestoresAuditFieldsOnFailure()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new TableGateway<AuditedItem, int>(ctx, new StubAuditValueResolver("updater"));

        var originalCreatedOn = new DateTime(2026, 1, 1);
        var originalLastUpdatedOn = new DateTime(2026, 6, 1);
        var entity = new AuditedItem
        {
            Id = 1,
            Name = "widget",
            CreatedOn = originalCreatedOn,
            CreatedBy = "original-creator",
            LastUpdatedOn = originalLastUpdatedOn,
            LastUpdatedBy = "original-updater"
        };

        factory.SetNonQueryException(new InvalidOperationException("simulated failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.UpdateAsync(entity, loadOriginal: false, context: ctx).AsTask());

        Assert.Equal(originalLastUpdatedOn, entity.LastUpdatedOn);
        Assert.Equal("original-updater", entity.LastUpdatedBy);
        // Update never touches CreatedOn/CreatedBy in the first place — confirm untouched too.
        Assert.Equal(originalCreatedOn, entity.CreatedOn);
        Assert.Equal("original-creator", entity.CreatedBy);
    }

    [Fact]
    public async Task TableGateway_UpdateAsync_RestoresAuditFieldsOnVersionConflict()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new TableGateway<AuditedVersionedItem, int>(ctx, new StubAuditValueResolver("updater"));

        var originalLastUpdatedOn = new DateTime(2026, 6, 1);
        var entity = new AuditedVersionedItem
        {
            Id = 1,
            Name = "widget",
            Version = 1,
            LastUpdatedOn = originalLastUpdatedOn,
            LastUpdatedBy = "original-updater"
        };

        // Execute "succeeds" but affects 0 rows — the version-mismatch/row-deleted signal.
        factory.SetNonQueryResult(0);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => gateway.UpdateAsync(entity, loadOriginal: false, context: ctx).AsTask());

        Assert.Equal(originalLastUpdatedOn, entity.LastUpdatedOn);
        Assert.Equal("original-updater", entity.LastUpdatedBy);
    }

    [Fact]
    public async Task TableGateway_UpsertAsync_RestoresAuditFieldsOnFailure()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new TableGateway<AuditedItem, int>(ctx, new StubAuditValueResolver("upserter"));

        var entity = new AuditedItem { Id = 1, Name = "widget" };
        factory.SetNonQueryException(new InvalidOperationException("simulated failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.UpsertAsync(entity, ctx).AsTask());

        Assert.Equal(default, entity.CreatedOn);
        Assert.Equal(string.Empty, entity.CreatedBy);
        Assert.Equal(default, entity.LastUpdatedOn);
        Assert.Equal(string.Empty, entity.LastUpdatedBy);
    }

    // ---------------- PrimaryKeyTableGateway ----------------

    [Fact]
    public async Task PrimaryKeyTableGateway_CreateAsync_RestoresAuditFieldsOnFailure()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new PrimaryKeyTableGateway<AuditedPkItem>(ctx, new StubAuditValueResolver("creator"));

        var entity = new AuditedPkItem { Key = 1, Name = "widget" };
        factory.SetNonQueryException(new InvalidOperationException("simulated failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.CreateAsync(entity, ctx).AsTask());

        Assert.Equal(default, entity.CreatedOn);
        Assert.Equal(string.Empty, entity.CreatedBy);
        Assert.Equal(default, entity.LastUpdatedOn);
        Assert.Equal(string.Empty, entity.LastUpdatedBy);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_UpdateAsync_RestoresAuditFieldsOnFailure()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new PrimaryKeyTableGateway<AuditedPkItem>(ctx, new StubAuditValueResolver("updater"));

        var originalLastUpdatedOn = new DateTime(2026, 6, 1);
        var entity = new AuditedPkItem
        {
            Key = 1,
            Name = "widget",
            LastUpdatedOn = originalLastUpdatedOn,
            LastUpdatedBy = "original-updater"
        };

        factory.SetNonQueryException(new InvalidOperationException("simulated failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.UpdateAsync(entity, ctx).AsTask());

        Assert.Equal(originalLastUpdatedOn, entity.LastUpdatedOn);
        Assert.Equal("original-updater", entity.LastUpdatedBy);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_UpsertAsync_RestoresAuditFieldsOnFailure()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new PrimaryKeyTableGateway<AuditedPkItem>(ctx, new StubAuditValueResolver("upserter"));

        var entity = new AuditedPkItem { Key = 1, Name = "widget" };
        factory.SetNonQueryException(new InvalidOperationException("simulated failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.UpsertAsync(entity, ctx).AsTask());

        Assert.Equal(default, entity.CreatedOn);
        Assert.Equal(string.Empty, entity.CreatedBy);
        Assert.Equal(default, entity.LastUpdatedOn);
        Assert.Equal(string.Empty, entity.LastUpdatedBy);
    }
}
