#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.types.valueobjects;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TableGatewayByteArrayVersionTests : SqlLiteContextTestBase
{
    [Table("RowVer")]
    private sealed class ByteVerEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Binary)]
        public byte[] Version { get; set; } = Array.Empty<byte>();
    }

    [Table("RowVerStruct")]
    private sealed class RowVersionVerEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Binary)]
        public RowVersion Version { get; set; } = RowVersion.FromBytes(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });

        [CreatedBy]
        [Column("CreatedBy", DbType.String)]
        public string CreatedBy { get; set; } = string.Empty;

        [LastUpdatedOn]
        [Column("LastUpdatedOn", DbType.DateTime)]
        public DateTime LastUpdatedOn { get; set; }
    }

    public TableGatewayByteArrayVersionTests()
    {
        TypeMap.Register<ByteVerEntity>();
        TypeMap.Register<RowVersionVerEntity>();
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = $@"CREATE TABLE IF NOT EXISTS {qp}RowVer{qs}(
            {qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}Name{qs} TEXT NOT NULL,
            {qp}Version{qs} BLOB
        )";
        Context.CreateSqlContainer(sql).ExecuteNonQueryAsync().GetAwaiter().GetResult();

        var rowVersionSql = $@"CREATE TABLE IF NOT EXISTS {qp}RowVerStruct{qs}(
            {qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}Name{qs} TEXT NOT NULL,
            {qp}Version{qs} BLOB,
            {qp}CreatedBy{qs} TEXT,
            {qp}LastUpdatedOn{qs} DATETIME
        )";
        Context.CreateSqlContainer(rowVersionSql).ExecuteNonQueryAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Update_WithByteArrayVersion_DoesNotIncrement()
    {
        var helper = new TableGateway<ByteVerEntity, int>(Context, AuditValueResolver);
        var e = new ByteVerEntity { Name = "x", Version = new byte[] { 1, 2, 3 } };
        await helper.CreateAsync(e, Context);
        e.Name = "y";
        var sc = await helper.BuildUpdateAsync(e, false);
        var sql = sc.Query.ToString();
        Assert.DoesNotContain("Version = Version + 1", sql);
    }

    [Fact]
    public async Task UpdateAsync_WithByteArrayVersion_SucceedsAndDoesNotMutateVersion()
    {
        // Regression: WriteBackIncrementedVersion runs unconditionally after every successful
        // UpdateAsync now — must be a no-op for byte[] rowversion/timestamp columns (DB-generated,
        // not a fixed "+1"), not crash or fabricate a value.
        var helper = new TableGateway<ByteVerEntity, int>(Context, AuditValueResolver);
        var original = new byte[] { 1, 2, 3 };
        var e = new ByteVerEntity { Name = "x", Version = original };
        await helper.CreateAsync(e, Context);
        e.Name = "y";

        var rowsAffected = await helper.UpdateAsync(e, false, Context);

        Assert.Equal(1, rowsAffected);
        Assert.Same(original, e.Version);
    }

    [Fact]
    public async Task Update_WithRowVersionVersion_DoesNotIncrement()
    {
        var helper = new TableGateway<RowVersionVerEntity, int>(Context, AuditValueResolver);
        var e = new RowVersionVerEntity { Name = "x" };
        await helper.CreateAsync(e, Context);
        e.Name = "y";
        var sc = await helper.BuildUpdateAsync(e, false);
        var sql = sc.Query.ToString();
        Assert.DoesNotContain("Version\" + 1", sql);
    }

    [Fact]
    public async Task UpdateAsync_WithRowVersionVersion_SucceedsAndDoesNotMutateVersionOrRollBackAudit()
    {
        // Regression: RowVersion (a non-IConvertible struct) is a valid [Version] type per
        // TypeMapRegistry.ValidateVersionColumn, exactly like byte[] — it must be excluded from
        // the "+1" numeric write-back the same way byte[] already is, not crash and cause a
        // genuinely successful write to be reported as a failed one (with audit fields rolled back).
        var helper = new TableGateway<RowVersionVerEntity, int>(Context, AuditValueResolver);
        var original = RowVersion.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var e = new RowVersionVerEntity { Name = "x", Version = original };
        await helper.CreateAsync(e, Context);
        var createdBy = e.CreatedBy;
        var lastUpdatedOnAfterCreate = e.LastUpdatedOn;
        e.Name = "y";

        var rowsAffected = await helper.UpdateAsync(e, false, Context);

        Assert.Equal(1, rowsAffected);
        Assert.Equal(original, e.Version);
        Assert.Equal(createdBy, e.CreatedBy);
        Assert.True(e.LastUpdatedOn >= lastUpdatedOnAfterCreate);
    }
}