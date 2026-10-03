using System.Collections.Generic;
using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-012: batch builders read every cell twice, once for the dialect's SQL builder and once to
/// bind it, so a [Json] column was serialized twice per row. Each cell is now read once per batch.
/// </summary>
public sealed class BatchCellExtractedOnceTests
{
    [Table("t")]
    public sealed class WithId
    {
        public static int Reads;
        private string _payload = "";

        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }

        [Column("payload", DbType.String)]
        public string Payload
        {
            get
            {
                Reads++;
                return _payload;
            }
            set => _payload = value;
        }
    }

    [Table("pk")]
    public sealed class WithKey
    {
        public static int Reads;
        private string _payload = "";

        [PrimaryKey(1)] [Column("k", DbType.Int32)] public int K { get; set; }

        [Column("payload", DbType.String)]
        public string Payload
        {
            get
            {
                Reads++;
                return _payload;
            }
            set => _payload = value;
        }
    }

    private static DatabaseContext Context(SupportedDatabase db) =>
        new($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));

    private static List<WithId> IdRows() =>
        Enumerable.Range(1, 3).Select(i => new WithId { Id = i, Payload = "p" + i }).ToList();

    private static List<WithKey> KeyRows() =>
        Enumerable.Range(1, 3).Select(i => new WithKey { K = i, Payload = "p" + i }).ToList();

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Oracle)]
    public void TableGateway_BatchCreateAndUpdate_ReadEachCellOnce(SupportedDatabase db)
    {
        using var context = Context(db);
        var gateway = new TableGateway<WithId, int>(context);
        var rows = IdRows();

        WithId.Reads = 0;
        gateway.BuildBatchCreate(rows);
        Assert.Equal(rows.Count, WithId.Reads);

        WithId.Reads = 0;
        gateway.BuildBatchUpdate(rows);
        Assert.Equal(rows.Count, WithId.Reads);

        WithId.Reads = 0;
        gateway.BuildBatchUpsert(rows);
        Assert.Equal(rows.Count, WithId.Reads);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MySql)]
    public void PrimaryKeyGateway_BatchCreateUpdateUpsert_ReadEachCellOnce(SupportedDatabase db)
    {
        using var context = Context(db);
        var gateway = new PrimaryKeyTableGateway<WithKey>(context);
        var rows = KeyRows();

        WithKey.Reads = 0;
        gateway.BuildBatchCreate(rows);
        Assert.Equal(rows.Count, WithKey.Reads);

        WithKey.Reads = 0;
        gateway.BuildBatchUpdate(rows);
        Assert.Equal(rows.Count, WithKey.Reads);

        WithKey.Reads = 0;
        gateway.BuildBatchUpsert(rows);
        Assert.Equal(rows.Count, WithKey.Reads);
    }
}
