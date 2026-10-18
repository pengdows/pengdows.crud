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
[Collection("AllocationSerial")]
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

    [Table("t")]
    public sealed class BoolValue
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Boolean)] public bool V { get; set; }
    }

    [Table("t")]
    public sealed class StringValue
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.String)] public string? V { get; set; }
    }

    private static long AllocatedPerRow<T>(List<Dictionary<string, object>> rows) where T : class, new() =>
        AllocationMeasurement.Lowest(() => AllocatedPerRowOnce<T>(rows));

    private static long AllocatedPerRowOnce<T>(List<Dictionary<string, object>> rows) where T : class, new()
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

    // Oracle reports NUMBER(1) as decimal; a decimal or integer column into a bool property went
    // through the general Coerce (boxing the value) on every row. Same result: non-zero is true.
    [Fact]
    public void DecimalColumnIntoBoolProperty_AllocatesNoMoreThanBoolColumn()
    {
        var asDecimal = AllocatedPerRow<BoolValue>(Rows(i => (decimal)(i % 2)));
        var asBool = AllocatedPerRow<BoolValue>(Rows(i => i % 2 == 1));

        Assert.True(asDecimal <= asBool, $"decimal column {asDecimal} B/row, bool column {asBool} B/row");
    }

    // A float or double column into a bool still went through the general Coerce (DataReaderMapper
    // read it typed): one numeric expression builder now serves both mappers (DRY-010).
    [Fact]
    public void DoubleColumnIntoBoolProperty_AllocatesNoMoreThanBoolColumn()
    {
        var asDouble = AllocatedPerRow<BoolValue>(Rows(i => (double)(i % 2)));
        var asFloat = AllocatedPerRow<BoolValue>(Rows(i => (float)(i % 2)));
        var asBool = AllocatedPerRow<BoolValue>(Rows(i => i % 2 == 1));

        Assert.True(asDouble <= asBool && asFloat <= asBool,
            $"double column {asDouble} B/row, float column {asFloat} B/row, bool column {asBool} B/row");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-3, true)]
    public void DecimalAndIntegerColumns_IntoBoolProperty_NonZeroIsTrue(int stored, bool expected)
    {
        foreach (var value in new object[] { (decimal)stored, (short)stored, stored, (long)stored })
        {
            var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));
            var gateway = new TableGateway<BoolValue, int>(context);
            var reader = new TrackedReader(new fakeDbDataReader(new[] { new Dictionary<string, object> { ["id"] = 1, ["v"] = value } }),
                new Mock<ITrackedConnection>().Object, Mock.Of<IAsyncDisposable>(), false);
            reader.Read();

            Assert.Equal(expected, gateway.MapReaderToObject(reader).V);
        }
    }

    // Npgsql charges a round of work per IsDBNull call (measured 2026-10-04: ~0.12 µs); a string
    // column is read once with GetValue, whose DBNull result is the null check.
    [Fact]
    public void StringColumn_IsReadWithoutIsDBNull_AndNullStaysNull()
    {
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", new fakeDbFactory(SupportedDatabase.PostgreSql));
        var gateway = new TableGateway<StringValue, int>(context);
        var fake = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["v"] = "a" },
            new Dictionary<string, object> { ["id"] = 2, ["v"] = DBNull.Value }
        });
        var reader = new TrackedReader(fake, new Mock<ITrackedConnection>().Object, Mock.Of<IAsyncDisposable>(), false);

        reader.Read();
        Assert.Equal("a", gateway.MapReaderToObject(reader).V);
        reader.Read();
        Assert.Null(gateway.MapReaderToObject(reader).V);
        Assert.Equal(0, fake.IsDBNullCallCount);
    }
}
