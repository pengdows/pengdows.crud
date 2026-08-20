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
/// write doesn't actually succeed. Batch variants retain successfully-written entities' audit
/// values while restoring only the containers not successfully executed.
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

    [Table("audited_items_autoid")]
    private class AuditedItemAutoId
    {
        [Id(false)]
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

    /// <summary>
    /// The prior fix's blanket "restore on any exception" catch is itself wrong once the write
    /// has already succeeded: SQLite's CompoundStatement create plan executes the INSERT (which
    /// commits), then reads the generated ID back from the SAME statement's trailing
    /// "SELECT last_insert_rowid()" — and falls back to a separate query for that ID when the
    /// combined reader doesn't yield it (a known fakeDb limitation — NextResult() always returns
    /// false). If THAT fallback query throws, the INSERT already committed with the new audit
    /// values; restoring them at that point would make the entity claim a rollback that never
    /// happened.
    /// </summary>
    [Fact]
    public async Task TableGateway_CreateAsync_DoesNotRestoreAuditFieldsWhenInsertSucceededButIdSyncFailed()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        for (var i = 0; i < 8; i++)
        {
            var connection = new fakeDbConnection();
            connection.SetCommandFailure(
                "SELECT last_insert_rowid()",
                new InvalidOperationException("simulated post-write ID-sync failure"));
            factory.Connections.Add(connection);
        }

        await using var ctx = CreateContext(factory);
        var gateway = new TableGateway<AuditedItemAutoId, int>(ctx, new StubAuditValueResolver("creator"));

        var entity = new AuditedItemAutoId { Name = "widget" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.CreateAsync(entity, ctx).AsTask());

        // The INSERT itself succeeded — only the follow-up ID-sync query failed. The entity must
        // still reflect the successful write, not be rolled back to its pre-attempt (default)
        // audit values.
        Assert.NotEqual(default, entity.CreatedOn);
        Assert.Equal("creator", entity.CreatedBy);
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

    /// <summary>
    /// A catch block can only react to a THROWN exception — it can't see a normal "return false"
    /// value. ExecuteNonQueryAsync affecting 0 rows without throwing (no exception, just an
    /// unsuccessful write) is exactly that case, and needs its own explicit restore at the point
    /// the 0-rows result is observed, not just in the generic catch.
    /// </summary>
    [Fact]
    public async Task TableGateway_CreateAsync_RestoresAuditFieldsOnZeroRowsWithoutException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new TableGateway<AuditedItem, int>(ctx, new StubAuditValueResolver("creator"));

        var entity = new AuditedItem { Id = 1, Name = "widget" };
        factory.SetNonQueryResult(0);

        var created = await gateway.CreateAsync(entity, ctx);

        Assert.False(created);
        Assert.Equal(default, entity.CreatedOn);
        Assert.Equal(string.Empty, entity.CreatedBy);
        Assert.Equal(default, entity.LastUpdatedOn);
        Assert.Equal(string.Empty, entity.LastUpdatedBy);
    }

    [Fact]
    public async Task TableGateway_UpdateAsync_RestoresAuditFieldsOnZeroRowsWithoutException_Unversioned()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new TableGateway<AuditedItem, int>(ctx, new StubAuditValueResolver("updater"));

        var originalLastUpdatedOn = new DateTime(2026, 6, 1);
        var entity = new AuditedItem
        {
            Id = 1,
            Name = "widget",
            LastUpdatedOn = originalLastUpdatedOn,
            LastUpdatedBy = "original-updater"
        };

        // No [Version] column, so 0 rows affected does NOT throw ConcurrencyConflictException —
        // it just returns 0. Audit fields must still be restored.
        factory.SetNonQueryResult(0);

        var rowsAffected = await gateway.UpdateAsync(entity, loadOriginal: false, context: ctx);

        Assert.Equal(0, rowsAffected);
        Assert.Equal(originalLastUpdatedOn, entity.LastUpdatedOn);
        Assert.Equal("original-updater", entity.LastUpdatedBy);
    }

    [Fact]
    public async Task TableGateway_UpsertAsync_RestoresAuditFieldsOnZeroRowsWithoutException_Unversioned()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new TableGateway<AuditedItem, int>(ctx, new StubAuditValueResolver("upserter"));

        var entity = new AuditedItem { Id = 1, Name = "widget" };
        factory.SetNonQueryResult(0);

        var rowsAffected = await gateway.UpsertAsync(entity, ctx);

        Assert.Equal(0, rowsAffected);
        Assert.Equal(default, entity.CreatedOn);
        Assert.Equal(string.Empty, entity.CreatedBy);
        Assert.Equal(default, entity.LastUpdatedOn);
        Assert.Equal(string.Empty, entity.LastUpdatedBy);
    }

    [Fact]
    public async Task TableGateway_BatchUpsertAsync_RestoresOnlyEntityWhoseContainerFailed()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        await using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var gateway = new TableGateway<AuditedItem, int>(ctx, new StubAuditValueResolver("batch-upserter"));
        var persisted = new AuditedItem { Id = 1, Name = "persisted" };
        var unpersisted = new AuditedItem { Id = 2, Name = "unpersisted" };

        factory.Connections.Add(new fakeDbConnection());
        var failingConnection = new fakeDbConnection();
        failingConnection.SetNonQueryExecuteException(new InvalidOperationException("second container failed"));
        factory.Connections.Add(failingConnection);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.BatchUpsertAsync([persisted, unpersisted], ctx).AsTask());

        Assert.NotEqual(default, persisted.CreatedOn);
        Assert.Equal("batch-upserter", persisted.CreatedBy);
        Assert.Equal(default, unpersisted.CreatedOn);
        Assert.Equal(string.Empty, unpersisted.CreatedBy);
        Assert.Equal(default, unpersisted.LastUpdatedOn);
        Assert.Equal(string.Empty, unpersisted.LastUpdatedBy);
    }

    [Fact]
    public async Task TableGateway_BatchCreateAsync_RestoresOnlyEntityWhoseFallbackContainerFailed()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        await using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", factory);
        var gateway = new TableGateway<AuditedItem, int>(ctx, new StubAuditValueResolver("batch-creator"));
        var persisted = new AuditedItem { Id = 1, Name = "persisted" };
        var unpersisted = new AuditedItem { Id = 2, Name = "unpersisted" };

        factory.Connections.Add(new fakeDbConnection());
        var failingConnection = new fakeDbConnection();
        failingConnection.SetNonQueryExecuteException(new InvalidOperationException("second container failed"));
        factory.Connections.Add(failingConnection);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.BatchCreateAsync([persisted, unpersisted], ctx).AsTask());

        Assert.NotEqual(default, persisted.CreatedOn);
        Assert.Equal("batch-creator", persisted.CreatedBy);
        Assert.Equal(default, unpersisted.CreatedOn);
        Assert.Equal(string.Empty, unpersisted.CreatedBy);
        Assert.Equal(default, unpersisted.LastUpdatedOn);
        Assert.Equal(string.Empty, unpersisted.LastUpdatedBy);
    }

    [Fact]
    public async Task TableGateway_BatchUpdateAsync_RestoresOnlyEntityWhoseFallbackContainerFailed()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new TableGateway<AuditedItem, int>(ctx, new StubAuditValueResolver("batch-updater"));
        var persisted = new AuditedItem { Id = 1, Name = "persisted" };
        var unpersisted = new AuditedItem { Id = 2, Name = "unpersisted" };

        factory.Connections.Add(new fakeDbConnection());
        var failingConnection = new fakeDbConnection();
        failingConnection.SetNonQueryExecuteException(new InvalidOperationException("second container failed"));
        factory.Connections.Add(failingConnection);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.BatchUpdateAsync([persisted, unpersisted], ctx).AsTask());

        Assert.NotEqual(default, persisted.LastUpdatedOn);
        Assert.Equal("batch-updater", persisted.LastUpdatedBy);
        Assert.Equal(default, unpersisted.LastUpdatedOn);
        Assert.Equal(string.Empty, unpersisted.LastUpdatedBy);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_BatchUpsertAsync_RestoresOnlyEntityWhoseContainerFailed()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        await using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var gateway = new PrimaryKeyTableGateway<AuditedPkItem>(ctx, new StubAuditValueResolver("pk-batch-upserter"));
        var persisted = new AuditedPkItem { Key = 1, Name = "persisted" };
        var unpersisted = new AuditedPkItem { Key = 2, Name = "unpersisted" };

        factory.Connections.Add(new fakeDbConnection());
        var failingConnection = new fakeDbConnection();
        failingConnection.SetNonQueryExecuteException(new InvalidOperationException("second container failed"));
        factory.Connections.Add(failingConnection);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.BatchUpsertAsync([persisted, unpersisted], ctx).AsTask());

        Assert.NotEqual(default, persisted.CreatedOn);
        Assert.Equal("pk-batch-upserter", persisted.CreatedBy);
        Assert.Equal(default, unpersisted.CreatedOn);
        Assert.Equal(string.Empty, unpersisted.CreatedBy);
        Assert.Equal(default, unpersisted.LastUpdatedOn);
        Assert.Equal(string.Empty, unpersisted.LastUpdatedBy);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_BatchCreateAsync_RestoresOnlyEntityWhoseFallbackContainerFailed()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        await using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", factory);
        var gateway = new PrimaryKeyTableGateway<AuditedPkItem>(ctx, new StubAuditValueResolver("pk-batch-creator"));
        var persisted = new AuditedPkItem { Key = 1, Name = "persisted" };
        var unpersisted = new AuditedPkItem { Key = 2, Name = "unpersisted" };

        factory.Connections.Add(new fakeDbConnection());
        var failingConnection = new fakeDbConnection();
        failingConnection.SetNonQueryExecuteException(new InvalidOperationException("second container failed"));
        factory.Connections.Add(failingConnection);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.BatchCreateAsync([persisted, unpersisted], ctx).AsTask());

        Assert.NotEqual(default, persisted.CreatedOn);
        Assert.Equal("pk-batch-creator", persisted.CreatedBy);
        Assert.Equal(default, unpersisted.CreatedOn);
        Assert.Equal(string.Empty, unpersisted.CreatedBy);
        Assert.Equal(default, unpersisted.LastUpdatedOn);
        Assert.Equal(string.Empty, unpersisted.LastUpdatedBy);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_BatchUpdateAsync_RestoresOnlyEntityWhoseFallbackContainerFailed()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new PrimaryKeyTableGateway<AuditedPkItem>(ctx, new StubAuditValueResolver("pk-batch-updater"));
        var persisted = new AuditedPkItem { Key = 1, Name = "persisted" };
        var unpersisted = new AuditedPkItem { Key = 2, Name = "unpersisted" };

        factory.Connections.Add(new fakeDbConnection());
        var failingConnection = new fakeDbConnection();
        failingConnection.SetNonQueryExecuteException(new InvalidOperationException("second container failed"));
        factory.Connections.Add(failingConnection);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.BatchUpdateAsync([persisted, unpersisted], ctx).AsTask());

        Assert.NotEqual(default, persisted.LastUpdatedOn);
        Assert.Equal("pk-batch-updater", persisted.LastUpdatedBy);
        Assert.Equal(default, unpersisted.LastUpdatedOn);
        Assert.Equal(string.Empty, unpersisted.LastUpdatedBy);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_CreateAsync_RestoresAuditFieldsOnZeroRowsWithoutException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new PrimaryKeyTableGateway<AuditedPkItem>(ctx, new StubAuditValueResolver("creator"));

        var entity = new AuditedPkItem { Key = 1, Name = "widget" };
        factory.SetNonQueryResult(0);

        var created = await gateway.CreateAsync(entity, ctx);

        Assert.False(created);
        Assert.Equal(default, entity.CreatedOn);
        Assert.Equal(string.Empty, entity.CreatedBy);
        Assert.Equal(default, entity.LastUpdatedOn);
        Assert.Equal(string.Empty, entity.LastUpdatedBy);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_UpdateAsync_RestoresAuditFieldsOnZeroRowsWithoutException()
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

        factory.SetNonQueryResult(0);

        var rowsAffected = await gateway.UpdateAsync(entity, ctx);

        Assert.Equal(0, rowsAffected);
        Assert.Equal(originalLastUpdatedOn, entity.LastUpdatedOn);
        Assert.Equal("original-updater", entity.LastUpdatedBy);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_UpsertAsync_RestoresAuditFieldsOnZeroRowsWithoutException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var gateway = new PrimaryKeyTableGateway<AuditedPkItem>(ctx, new StubAuditValueResolver("upserter"));

        var entity = new AuditedPkItem { Key = 1, Name = "widget" };
        factory.SetNonQueryResult(0);

        var rowsAffected = await gateway.UpsertAsync(entity, ctx);

        Assert.Equal(0, rowsAffected);
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
