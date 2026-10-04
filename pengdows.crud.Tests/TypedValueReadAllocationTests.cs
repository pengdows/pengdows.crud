using System;
using System.Collections;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Moq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-020 (found by the 2026-10-04 performance review): value types with no IDataRecord getter
/// (DateTimeOffset, TimeSpan, DateOnly, TimeOnly, char, unsigned integers) were read with GetValue,
/// boxing every value (32 B for a DateTimeOffset) only to unbox it. They are read with the provider's
/// GetFieldValue&lt;T&gt;. This reader boxes on GetValue as real drivers do and returns typed values
/// from GetFieldValue&lt;T&gt;.
/// </summary>
public class TypedValueReadAllocationTests
{
    [Table("t")]
    public sealed class Row
    {
        [Id] [Column("id", System.Data.DbType.Int32)] public int Id { get; set; }
        [Column("when", System.Data.DbType.DateTimeOffset)] public DateTimeOffset When { get; set; }
        [Column("span", System.Data.DbType.Time)] public TimeSpan Span { get; set; }
    }

    [Table("t")]
    public sealed class OnlyId
    {
        [Id] [Column("id", System.Data.DbType.Int32)] public int Id { get; set; }
    }

    private sealed class BoxingReader : DbDataReader
    {
        private int _row = -1;
        private readonly int _rows;
        private readonly bool _refusesTypedReads;

        public BoxingReader(int rows, bool refusesTypedReads = false)
        {
            _rows = rows;
            _refusesTypedReads = refusesTypedReads;
        }

        private DateTimeOffset When => new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        private TimeSpan Span => TimeSpan.FromMinutes(_row);

        public override bool Read() => ++_row < _rows;
        public override int FieldCount => 3;
        public override string GetName(int ordinal) => ordinal switch { 0 => "id", 1 => "when", _ => "span" };
        public override Type GetFieldType(int ordinal) => ordinal switch { 0 => typeof(int), 1 => typeof(DateTimeOffset), _ => typeof(TimeSpan) };
        public override object GetValue(int ordinal) => ordinal switch { 0 => _row, 1 => When, _ => Span };
        public override int GetInt32(int ordinal) => _row;
        public override bool IsDBNull(int ordinal) => false;
        public override T GetFieldValue<T>(int ordinal)
        {
            if (_refusesTypedReads) throw new InvalidCastException("typed reads not supported");
            if (ordinal == 1) { var v = When; return Unsafe.As<DateTimeOffset, T>(ref v); }
            if (ordinal == 2) { var v = Span; return Unsafe.As<TimeSpan, T>(ref v); }
            var i = _row; return Unsafe.As<int, T>(ref i);
        }

        public override int GetOrdinal(string name) => name switch { "id" => 0, "when" => 1, _ => 2 };
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override int Depth => 0;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override bool NextResult() => false;
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int o, long d, byte[]? b, int bo, int l) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int o, long d, char[]? b, int bo, int l) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetString(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
    }

    private static long EntitySize<T>() where T : new()
    {
        _ = new T();
        var before = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(new T());
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static TrackedReader Open(int rows, bool refusesTypedReads = false) =>
        new(new BoxingReader(rows, refusesTypedReads), new Mock<ITrackedConnection>().Object, Mock.Of<IAsyncDisposable>(), false);

    private static TableGateway<T, int> Gateway<T>() where T : class, new() =>
        new(new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", new fakeDbFactory(SupportedDatabase.SqlServer)));

    [Fact]
    public void ProviderRefusingTypedReads_IsReadThroughGetValue()
    {
        var reader = Open(3, refusesTypedReads: true);
        reader.Read();
        reader.Read();

        var row = Gateway<Row>().MapReaderToObject(reader);

        Assert.Equal(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero), row.When);
        Assert.Equal(TimeSpan.FromMinutes(1), row.Span);
    }

    // A plan-cache hit allocated the closure of the plan-building slow path on every call (PERF-020).
    [Fact]
    public void MapReaderToObject_AllocatesOnlyTheEntityPerRow()
    {
        var gateway = Gateway<OnlyId>();
        var reader = Open(501);
        reader.Read();
        gateway.MapReaderToObject(reader);
        var before = GC.GetAllocatedBytesForCurrentThread();
        long ids = 0;
        while (reader.Read())
        {
            ids += gateway.MapReaderToObject(reader).Id;
        }

        var perRow = (GC.GetAllocatedBytesForCurrentThread() - before) / 500;
        Assert.Equal(500L * 501 / 2, ids);
        Assert.True(perRow <= EntitySize<OnlyId>(), $"{perRow} B/row; the entity alone is {EntitySize<OnlyId>()} B");
    }

    [Fact]
    public void DateTimeOffsetAndTimeSpanColumns_AreNotBoxedPerRow()
    {
        var gateway = Gateway<Row>();

        var warm = Open(10);
        while (warm.Read()) gateway.MapReaderToObject(warm);

        var reader = Open(501);
        reader.Read();
        gateway.MapReaderToObject(reader);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        var spans = TimeSpan.Zero;
        while (reader.Read())
        {
            spans += gateway.MapReaderToObject(reader).Span;
            count++;
        }

        var perRow = (GC.GetAllocatedBytesForCurrentThread() - before) / count;
        Assert.Equal(TimeSpan.FromMinutes(500 * 501 / 2), spans);
        var entityBytes = EntitySize<Row>();
        Assert.True(perRow <= entityBytes, $"{perRow} B/row; the entity alone is {entityBytes} B");
    }
}
