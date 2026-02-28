#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
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

    public TableGatewayByteArrayVersionTests()
    {
        TypeMap.Register<ByteVerEntity>();
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = $@"CREATE TABLE IF NOT EXISTS {qp}RowVer{qs}(
            {qp}Id{qs} INTEGER PRIMARY KEY AUTOINCREMENT,
            {qp}Name{qs} TEXT NOT NULL,
            {qp}Version{qs} BLOB
        )";
        Context.CreateSqlContainer(sql).ExecuteNonQueryAsync().GetAwaiter().GetResult();
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
}