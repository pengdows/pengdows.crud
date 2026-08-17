#region

using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.exceptions;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// A fully successful UpdateAsync computes the new [Version] value server-side
/// ("version = version + 1") but, until this fix, never wrote it back into the caller's entity —
/// the entity kept showing the pre-update value even though the row now has a higher one. Since
/// the SET clause's increment is a fixed, deterministic "+1" (not a server-generated value like a
/// rowversion/timestamp), and a successful write (rowsAffected > 0) guarantees the WHERE clause's
/// version match succeeded, "current + 1" can be computed and written back with full certainty —
/// no extra round trip needed. See docs/FUTURE_WORK.md's "entity freshness after a successful
/// write" entry.
/// </summary>
public class TableGatewayVersionWriteBackTests : SqlLiteContextTestBase
{
    [Table("VerWriteBack")]
    private sealed class VerWriteBackEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }
    }

    [Table("PkVerWriteBack")]
    private sealed class PkVerWriteBackEntity
    {
        [PrimaryKey]
        [Column("Key", DbType.Int32)]
        public int Key { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }
    }

    public TableGatewayVersionWriteBackTests()
    {
        TypeMap.Register<VerWriteBackEntity>();
        TypeMap.Register<PkVerWriteBackEntity>();

        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        Context.CreateSqlContainer($@"CREATE TABLE IF NOT EXISTS {qp}VerWriteBack{qs}(
            {qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}Name{qs} TEXT NOT NULL,
            {qp}Version{qs} INTEGER NOT NULL DEFAULT 0
        )").ExecuteNonQueryAsync().GetAwaiter().GetResult();

        Context.CreateSqlContainer($@"CREATE TABLE IF NOT EXISTS {qp}PkVerWriteBack{qs}(
            {qp}Key{qs} INTEGER PRIMARY KEY,
            {qp}Name{qs} TEXT NOT NULL,
            {qp}Version{qs} INTEGER NOT NULL DEFAULT 0
        )").ExecuteNonQueryAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task TableGateway_UpdateAsync_Success_WritesIncrementedVersionBackToEntity()
    {
        var helper = new TableGateway<VerWriteBackEntity, int>(Context, AuditValueResolver);
        var entity = new VerWriteBackEntity { Name = "original" };
        await helper.CreateAsync(entity, Context);
        Assert.Equal(1, entity.Version); // Create sets version to 1 when null/0

        entity.Name = "updated";
        var rowsAffected = await helper.UpdateAsync(entity, false, Context);

        Assert.Equal(1, rowsAffected);
        Assert.Equal(2, entity.Version);
    }

    [Fact]
    public async Task TableGateway_UpdateAsync_ConcurrencyConflict_DoesNotWriteBackVersion()
    {
        var helper = new TableGateway<VerWriteBackEntity, int>(Context, AuditValueResolver);
        var entity = new VerWriteBackEntity { Name = "original" };
        await helper.CreateAsync(entity, Context);
        Assert.Equal(1, entity.Version);

        // Simulate a stale in-memory copy: the WHERE clause's version condition won't match
        // whatever is actually in the row, so this must fail (0 rows affected), not succeed.
        entity.Version = 999;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => helper.UpdateAsync(entity, false, Context).AsTask());

        // Version must remain exactly what the caller supplied — a failed write must not
        // fabricate a new value.
        Assert.Equal(999, entity.Version);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_UpdateAsync_Success_WritesIncrementedVersionBackToEntity()
    {
        var helper = new PrimaryKeyTableGateway<PkVerWriteBackEntity>(Context);
        var entity = new PkVerWriteBackEntity { Key = 1, Name = "original" };
        await helper.CreateAsync(entity, Context);
        Assert.Equal(1, entity.Version);

        entity.Name = "updated";
        var rowsAffected = await helper.UpdateAsync(entity, Context);

        Assert.Equal(1, rowsAffected);
        Assert.Equal(2, entity.Version);
    }
}
