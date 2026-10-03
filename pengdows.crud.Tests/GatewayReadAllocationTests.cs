using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Moq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Per-row allocation of gateway hydration for column/property pairs that have a typed read.
/// A decimal column into a double property (TYPE-002's ODP.NET beyond-decimal fallback) went
/// through GetValue and the general coercion for every provider: +39 ns and +56 B per row. A long
/// column into a decimal or double property (Snowflake's beyond-Int64 fallback) did the same:
/// +68 ns and +80 B per row.
/// </summary>
public sealed class GatewayReadAllocationTests
{
    [Table("t")]
    public sealed class DecimalValue
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Decimal)] public decimal V { get; set; }
    }

    [Table("t")]
    public sealed class DoubleValue
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Double)] public double V { get; set; }
    }

    private static long AllocatedPerRow<T>(List<Dictionary<string, object>> rows) where T : class, new()
    {
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));
        var gateway = new TableGateway<T, int>(context);
        TrackedReader Open() => new(new fakeDbDataReader(rows), new Mock<ITrackedConnection>().Object,
            Mock.Of<IAsyncDisposable>(), false);

        var warm = Open();
        while (warm.Read())
        {
            gateway.MapReaderToObject(warm);
        }

        var reader = Open();
        reader.Read();
        gateway.MapReaderToObject(reader);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 1;
        while (reader.Read())
        {
            gateway.MapReaderToObject(reader);
            count++;
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / (count - 1);
    }

    private static List<Dictionary<string, object>> Rows(Func<int, object> value) =>
        Enumerable.Range(0, 500).Select(i => new Dictionary<string, object> { ["id"] = i, ["v"] = value(i) }).ToList();

    [Fact]
    public void DecimalColumnIntoDoubleProperty_AllocatesNoMoreThanIntoDecimalProperty()
    {
        var rows = Rows(i => 1234.5678m + i);

        Assert.True(AllocatedPerRow<DoubleValue>(rows) <= AllocatedPerRow<DecimalValue>(rows),
            $"double {AllocatedPerRow<DoubleValue>(rows)} B/row, decimal {AllocatedPerRow<DecimalValue>(rows)} B/row");
    }

    [Fact]
    public void LongColumnIntoDecimalProperty_AllocatesNoMoreThanDecimalColumn()
    {
        var asLong = AllocatedPerRow<DecimalValue>(Rows(i => 1_000_000_000_000L + i));
        var asDecimal = AllocatedPerRow<DecimalValue>(Rows(i => 1_000_000_000_000m + i));

        Assert.True(asLong <= asDecimal, $"long column {asLong} B/row, decimal column {asDecimal} B/row");
    }

    [Fact]
    public void LongColumnIntoDoubleProperty_AllocatesNoMoreThanDoubleColumn()
    {
        var asLong = AllocatedPerRow<DoubleValue>(Rows(i => 1_000_000_000_000L + i));
        var asDouble = AllocatedPerRow<DoubleValue>(Rows(i => 1_000_000_000_000.0 + i));

        Assert.True(asLong <= asDouble, $"long column {asLong} B/row, double column {asDouble} B/row");
    }
}
