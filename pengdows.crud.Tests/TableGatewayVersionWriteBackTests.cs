#region

using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.exceptions;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// BP-203: a successful UpdateAsync/BatchUpdateAsync increments [Version] server-side
/// ("version = version + 1") but never wrote the new value back into the caller's entity, so
/// reusing the same instance for a second update always failed the version check. The increment
/// is a fixed "+1" and a successful write (rows affected &gt; 0) proves the WHERE clause's version
/// matched, so "current + 1" is known without a round trip. Opaque byte[]/RowVersion versions are
/// DB-generated and are not written back (see TableGatewayByteArrayVersionTests).
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

    [Table("VerWriteBackLong")]
    private sealed class NullableLongVerEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int64)]
        public long? Version { get; set; }
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
        TypeMap.Register<NullableLongVerEntity>();
        TypeMap.Register<PkVerWriteBackEntity>();

        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        foreach (var table in new[] { "VerWriteBack", "VerWriteBackLong" })
        {
            Context.CreateSqlContainer($@"CREATE TABLE IF NOT EXISTS {qp}{table}{qs}(
                {qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
                {qp}Name{qs} TEXT NOT NULL,
                {qp}Version{qs} INTEGER NOT NULL DEFAULT 0
            )").ExecuteNonQueryAsync().GetAwaiter().GetResult();
        }

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
        Assert.Equal(1, entity.Version);

        entity.Name = "updated";
        var rowsAffected = await helper.UpdateAsync(entity, false, Context);

        Assert.Equal(1, rowsAffected);
        Assert.Equal(2, entity.Version);
    }

    [Fact]
    public async Task TableGateway_UpdateAsync_ReusedInstance_SecondUpdateSucceeds()
    {
        var helper = new TableGateway<VerWriteBackEntity, int>(Context, AuditValueResolver);
        var entity = new VerWriteBackEntity { Name = "original" };
        await helper.CreateAsync(entity, Context);

        entity.Name = "first";
        await helper.UpdateAsync(entity, Context);
        entity.Name = "second";
        var rowsAffected = await helper.UpdateAsync(entity, Context);

        Assert.Equal(1, rowsAffected);
        Assert.Equal(3, entity.Version);
        var reloaded = await helper.RetrieveOneAsync(entity.Id, Context);
        Assert.NotNull(reloaded);
        Assert.Equal(3, reloaded!.Version);
        Assert.Equal("second", reloaded.Name);
    }

    [Fact]
    public async Task TableGateway_UpdateAsync_NullableLongVersion_WritesBackIncrementedValue()
    {
        var helper = new TableGateway<NullableLongVerEntity, int>(Context, AuditValueResolver);
        var entity = new NullableLongVerEntity { Name = "original" };
        await helper.CreateAsync(entity, Context);
        Assert.Equal(1L, entity.Version);

        entity.Name = "updated";
        await helper.UpdateAsync(entity, false, Context);

        Assert.Equal(2L, entity.Version);
    }

    [Fact]
    public async Task TableGateway_UpdateAsync_ConcurrencyConflict_DoesNotWriteBackVersion()
    {
        var helper = new TableGateway<VerWriteBackEntity, int>(Context, AuditValueResolver);
        var entity = new VerWriteBackEntity { Name = "original" };
        await helper.CreateAsync(entity, Context);

        // Stale in-memory copy: the update must fail, and a failed write must not fabricate a
        // new version value.
        entity.Version = 999;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => helper.UpdateAsync(entity, false, Context).AsTask());

        Assert.Equal(999, entity.Version);
    }

    [Fact]
    public async Task TableGateway_BatchUpdateAsync_Success_WritesIncrementedVersionBackToEveryEntity()
    {
        var helper = new TableGateway<VerWriteBackEntity, int>(Context, AuditValueResolver);
        var a = new VerWriteBackEntity { Name = "a" };
        var b = new VerWriteBackEntity { Name = "b" };
        await helper.CreateAsync(a, Context);
        await helper.CreateAsync(b, Context);

        a.Name = "a-updated";
        b.Name = "b-updated";
        var affected = await helper.BatchUpdateAsync(new[] { a, b }, Context);

        Assert.Equal(2, affected);
        Assert.Equal(2, a.Version);
        Assert.Equal(2, b.Version);

        // Reusing the same instances for another batch must not conflict.
        a.Name = "a-again";
        b.Name = "b-again";
        Assert.Equal(2, await helper.BatchUpdateAsync(new[] { a, b }, Context));
        Assert.Equal(3, a.Version);
        Assert.Equal(3, b.Version);
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

        entity.Name = "again";
        Assert.Equal(1, await helper.UpdateAsync(entity, false, Context));
        Assert.Equal(3, entity.Version);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_UpdateAsync_ConcurrencyConflict_DoesNotWriteBackVersion()
    {
        var helper = new PrimaryKeyTableGateway<PkVerWriteBackEntity>(Context);
        var entity = new PkVerWriteBackEntity { Key = 2, Name = "original" };
        await helper.CreateAsync(entity, Context);
        entity.Version = 999;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => helper.UpdateAsync(entity, Context).AsTask());

        Assert.Equal(999, entity.Version);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_BatchUpdateAsync_Success_WritesIncrementedVersionBackToEveryEntity()
    {
        var helper = new PrimaryKeyTableGateway<PkVerWriteBackEntity>(Context);
        var a = new PkVerWriteBackEntity { Key = 10, Name = "a" };
        var b = new PkVerWriteBackEntity { Key = 11, Name = "b" };
        await helper.CreateAsync(a, Context);
        await helper.CreateAsync(b, Context);

        a.Name = "a-updated";
        b.Name = "b-updated";
        var affected = await helper.BatchUpdateAsync(new[] { a, b }, Context);

        Assert.Equal(2, affected);
        Assert.Equal(2, a.Version);
        Assert.Equal(2, b.Version);
    }

    [Fact]
    public async Task PrimaryKeyTableGateway_BatchUpdateAsync_Conflict_DoesNotWriteBackConflictingEntity()
    {
        var helper = new PrimaryKeyTableGateway<PkVerWriteBackEntity>(Context);
        var a = new PkVerWriteBackEntity { Key = 20, Name = "a" };
        var b = new PkVerWriteBackEntity { Key = 21, Name = "b" };
        await helper.CreateAsync(a, Context);
        await helper.CreateAsync(b, Context);

        a.Name = "a-updated";
        b.Name = "b-updated";
        b.Version = 999;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => helper.BatchUpdateAsync(new[] { a, b }, Context).AsTask());

        // a was written before the conflict, so it carries its new version; b keeps its own.
        Assert.Equal(2, a.Version);
        Assert.Equal(999, b.Version);
    }
}
