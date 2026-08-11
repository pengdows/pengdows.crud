// =============================================================================
// FILE: OptimisticConcurrencyTests.cs
// PURPOSE: TDD tests (written RED) for the proactive optimistic-concurrency
//          check on loadOriginal=true, covering both TableGateway<TEntity,TRowID>
//          and PrimaryKeyTableGateway<TEntity>. Before this change:
//          - TableGateway only detected conflicts reactively (0 rows affected).
//          - PrimaryKeyTableGateway's loadOriginal flag was a silent no-op and
//            never threw on a 0-row update at all.
// =============================================================================

using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pengdows.crud.attributes;
using pengdows.crud.exceptions;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("SqliteSerial")]
public class OptimisticConcurrencyTests : IAsyncLifetime
{
    private readonly TypeMapRegistry _typeMap = new();
    private IDatabaseContext _context = null!;

    public async Task InitializeAsync()
    {
        _context = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance, _typeMap);
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;

        await Exec($"CREATE TABLE {qp}ver_entity{qs} ({qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT, {qp}Name{qs} TEXT NOT NULL, {qp}Version{qs} INTEGER NOT NULL DEFAULT 0)");
        await Exec($"CREATE TABLE {qp}audit_entity{qs} ({qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT, {qp}Name{qs} TEXT NOT NULL, {qp}LastUpdatedOn{qs} TIMESTAMP NULL)");
        await Exec($"CREATE TABLE {qp}plain_entity{qs} ({qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT, {qp}Name{qs} TEXT NOT NULL)");
        await Exec($"CREATE TABLE {qp}ver_pk_entity{qs} ({qp}Code{qs} TEXT PRIMARY KEY, {qp}Name{qs} TEXT NOT NULL, {qp}Version{qs} INTEGER NOT NULL DEFAULT 0)");
        await Exec($"CREATE TABLE {qp}audit_pk_entity{qs} ({qp}Code{qs} TEXT PRIMARY KEY, {qp}Name{qs} TEXT NOT NULL, {qp}LastUpdatedOn{qs} TIMESTAMP NULL)");
    }

    public async Task DisposeAsync()
    {
        if (_context is IAsyncDisposable disp)
        {
            await disp.DisposeAsync();
        }
    }

    private async Task Exec(string sql)
    {
        await using var sc = _context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    // =========================================================================
    // Entities
    // =========================================================================

    [Table("ver_entity")]
    private sealed class VersionedEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }
    }

    [Table("audit_entity")]
    private sealed class AuditOnlyEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [LastUpdatedOn]
        [Column("LastUpdatedOn", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    [Table("plain_entity")]
    private sealed class PlainEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("ver_pk_entity")]
    private sealed class VersionedPkEntity
    {
        [PrimaryKey(1)]
        [Column("Code", DbType.String)]
        public string Code { get; set; } = string.Empty;

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }
    }

    [Table("audit_pk_entity")]
    private sealed class AuditOnlyPkEntity
    {
        [PrimaryKey(1)]
        [Column("Code", DbType.String)]
        public string Code { get; set; } = string.Empty;

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [LastUpdatedOn]
        [Column("LastUpdatedOn", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    // =========================================================================
    // TableGateway — [Version] column
    // =========================================================================

    [Fact]
    public async Task TableGateway_VersionStale_ThrowsConcurrencyConflictException_BeforeIssuingUpdate()
    {
        var gw = new TableGateway<VersionedEntity, int>(_context);
        var entity = new VersionedEntity { Name = "A" };
        await gw.CreateAsync(entity);
        var loaded = await gw.RetrieveOneAsync(entity.Id);
        Assert.NotNull(loaded);

        var concurrent = await gw.RetrieveOneAsync(entity.Id);
        concurrent!.Name = "ConcurrentChange";
        await gw.UpdateAsync(concurrent);

        loaded!.Name = "MyChange";
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => gw.UpdateAsync(loaded, true).AsTask());

        var current = await gw.RetrieveOneAsync(entity.Id);
        Assert.Equal("ConcurrentChange", current!.Name);
    }

    [Fact]
    public async Task TableGateway_VersionMatches_UpdatesSuccessfully()
    {
        var gw = new TableGateway<VersionedEntity, int>(_context);
        var entity = new VersionedEntity { Name = "A" };
        await gw.CreateAsync(entity);
        var loaded = await gw.RetrieveOneAsync(entity.Id);
        loaded!.Name = "B";

        var affected = await gw.UpdateAsync(loaded, true);
        Assert.Equal(1, affected);

        var current = await gw.RetrieveOneAsync(entity.Id);
        Assert.Equal("B", current!.Name);
        Assert.Equal(2, current.Version);
    }

    [Fact]
    public async Task TableGateway_RowDeleted_ThrowsConcurrencyConflictException()
    {
        var gw = new TableGateway<VersionedEntity, int>(_context);
        var entity = new VersionedEntity { Name = "A" };
        await gw.CreateAsync(entity);
        var loaded = await gw.RetrieveOneAsync(entity.Id);
        await gw.DeleteAsync(entity.Id);

        loaded!.Name = "B";
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => gw.UpdateAsync(loaded, true).AsTask());
    }

    // =========================================================================
    // TableGateway — audit-only fallback (no [Version] column)
    // =========================================================================

    [Fact]
    public async Task TableGateway_AuditOnly_Stale_ThrowsConcurrencyConflictException()
    {
        var gw = new TableGateway<AuditOnlyEntity, int>(_context);
        var entity = new AuditOnlyEntity { Name = "A" };
        await gw.CreateAsync(entity);
        var loaded = await gw.RetrieveOneAsync(entity.Id);

        await Task.Delay(10);
        var concurrent = await gw.RetrieveOneAsync(entity.Id);
        concurrent!.Name = "ConcurrentChange";
        await gw.UpdateAsync(concurrent);

        loaded!.Name = "MyChange";
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => gw.UpdateAsync(loaded, true).AsTask());
    }

    [Fact]
    public async Task TableGateway_AuditOnly_Match_UpdatesSuccessfully()
    {
        var gw = new TableGateway<AuditOnlyEntity, int>(_context);
        var entity = new AuditOnlyEntity { Name = "A" };
        await gw.CreateAsync(entity);
        var loaded = await gw.RetrieveOneAsync(entity.Id);
        loaded!.Name = "B";

        var affected = await gw.UpdateAsync(loaded, true);
        Assert.Equal(1, affected);
    }

    // =========================================================================
    // TableGateway — no version, no audit columns: no proactive check possible
    // =========================================================================

    [Fact]
    public async Task TableGateway_NoVersionNoAudit_StaleChange_DoesNotThrow()
    {
        var gw = new TableGateway<PlainEntity, int>(_context);
        var entity = new PlainEntity { Name = "A" };
        await gw.CreateAsync(entity);
        var loaded = await gw.RetrieveOneAsync(entity.Id);

        var concurrent = await gw.RetrieveOneAsync(entity.Id);
        concurrent!.Name = "ConcurrentChange";
        await gw.UpdateAsync(concurrent);

        loaded!.Name = "MyChange";
        var affected = await gw.UpdateAsync(loaded, true);
        Assert.Equal(1, affected);
    }

    // =========================================================================
    // PrimaryKeyTableGateway — [Version] column
    // =========================================================================

    [Fact]
    public async Task PkGateway_VersionStale_ThrowsConcurrencyConflictException()
    {
        var gw = new PrimaryKeyTableGateway<VersionedPkEntity>(_context);
        var entity = new VersionedPkEntity { Code = "X1", Name = "A" };
        await gw.CreateAsync(entity);

        var concurrent = new VersionedPkEntity { Code = "X1", Name = "ConcurrentChange", Version = entity.Version };
        var concurrentAffected = await gw.UpdateAsync(concurrent);
        Assert.Equal(1, concurrentAffected);

        var stale = new VersionedPkEntity { Code = "X1", Name = "MyChange", Version = entity.Version };
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => gw.UpdateAsync(stale, true).AsTask());

        var current = await gw.RetrieveOneAsync(new VersionedPkEntity { Code = "X1" });
        Assert.Equal("ConcurrentChange", current!.Name);
    }

    [Fact]
    public async Task PkGateway_VersionMatches_UpdatesSuccessfully()
    {
        var gw = new PrimaryKeyTableGateway<VersionedPkEntity>(_context);
        var entity = new VersionedPkEntity { Code = "X2", Name = "A" };
        await gw.CreateAsync(entity);

        var update = new VersionedPkEntity { Code = "X2", Name = "B", Version = entity.Version };
        var affected = await gw.UpdateAsync(update, true);
        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task PkGateway_RowDeleted_ThrowsConcurrencyConflictException()
    {
        var gw = new PrimaryKeyTableGateway<VersionedPkEntity>(_context);
        var entity = new VersionedPkEntity { Code = "X3", Name = "A" };
        await gw.CreateAsync(entity);
        await gw.BatchDeleteAsync(new[] { entity });

        var stale = new VersionedPkEntity { Code = "X3", Name = "B", Version = entity.Version };
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => gw.UpdateAsync(stale, true).AsTask());
    }

    // =========================================================================
    // PrimaryKeyTableGateway — audit-only fallback
    // =========================================================================

    [Fact]
    public async Task PkGateway_AuditOnly_Stale_ThrowsConcurrencyConflictException()
    {
        var gw = new PrimaryKeyTableGateway<AuditOnlyPkEntity>(_context);
        var entity = new AuditOnlyPkEntity { Code = "Y1", Name = "A" };
        await gw.CreateAsync(entity);

        await Task.Delay(10);
        var concurrent = new AuditOnlyPkEntity { Code = "Y1", Name = "ConcurrentChange" };
        await gw.UpdateAsync(concurrent);

        var stale = new AuditOnlyPkEntity { Code = "Y1", Name = "MyChange" };
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => gw.UpdateAsync(stale, true).AsTask());
    }

    // =========================================================================
    // PrimaryKeyTableGateway — loadOriginal must actually reload (was a no-op)
    // =========================================================================

    [Fact]
    public async Task PkGateway_LoadOriginalTrue_ReloadsCurrentRow_NotANoOp()
    {
        var gw = new PrimaryKeyTableGateway<VersionedPkEntity>(_context);
        var entity = new VersionedPkEntity { Code = "Z1", Name = "A" };
        await gw.CreateAsync(entity);

        // Bump the DB row out from under a stale in-memory copy.
        var concurrent = new VersionedPkEntity { Code = "Z1", Name = "ConcurrentChange", Version = entity.Version };
        await gw.UpdateAsync(concurrent);

        var stale = new VersionedPkEntity { Code = "Z1", Name = "MyChange", Version = entity.Version };

        // Before the fix, loadOriginal was silently ignored, so this would succeed
        // (last-write-wins) instead of detecting the conflict.
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => gw.UpdateAsync(stale, true).AsTask());
    }
}
