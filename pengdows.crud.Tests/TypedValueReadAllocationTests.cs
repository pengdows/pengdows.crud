using System;
using System.Collections;
using System.Collections.Generic;
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
[Collection("AllocationSerial")]
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

    [Table("t")]
    public sealed class Converted
    {
        [Id] [Column("id", System.Data.DbType.Int32)] public int Id { get; set; }
        [Column("d", System.Data.DbType.Date)] public DateOnly Day { get; set; }
        [Column("t", System.Data.DbType.Time)] public TimeOnly Time { get; set; }
        [Column("ts", System.Data.DbType.DateTime)] public DateTime Stamp { get; set; }
        [Column("tso", System.Data.DbType.DateTimeOffset)] public DateTimeOffset StampOffset { get; set; }
        [Column("ds", System.Data.DbType.Date)] public DateOnly TextDay { get; set; }
        [Column("u", System.Data.DbType.Int64)] public long Unsigned { get; set; }
    }

    // Columns whose provider type differs from the property: DateTime into DateOnly, TimeSpan into
    // TimeOnly, ISO text into DateTime/DateTimeOffset/DateOnly (SQLite), uint into long.
    private sealed class ConvertingReader : DbDataReader
    {
        private static readonly string[] Names = { "id", "d", "t", "ts", "tso", "ds", "u" };
        private static readonly Type[] Types =
            { typeof(int), typeof(DateTime), typeof(TimeSpan), typeof(string), typeof(string), typeof(string), typeof(uint) };

        private readonly string _stampText;
        private readonly string _dayText;
        private readonly TimeSpan _time;
        private int _row = -1;
        private readonly int _rows;

        public ConvertingReader(int rows, string stampText = "2026-10-04T12:30:00Z", string dayText = "2026-10-04",
            TimeSpan? time = null)
        {
            _rows = rows;
            _stampText = stampText;
            _dayText = dayText;
            _time = time ?? new TimeSpan(13, 45, 0);
        }

        private object Value(int ordinal) => ordinal switch
        {
            0 => _row,
            1 => new DateTime(2026, 10, 4),
            2 => _time,
            3 or 4 => _stampText,
            5 => _dayText,
            _ => (uint)_row
        };

        public override bool Read() => ++_row < _rows;
        public override int FieldCount => Names.Length;
        public override string GetName(int ordinal) => Names[ordinal];
        public override Type GetFieldType(int ordinal) => Types[ordinal];
        public override object GetValue(int ordinal) => Value(ordinal);
        public override int GetInt32(int ordinal) => _row;
        public override DateTime GetDateTime(int ordinal) => new(2026, 10, 4);
        public override string GetString(int ordinal) => (string)Value(ordinal);
        public override bool IsDBNull(int ordinal) => false;
        public override T GetFieldValue<T>(int ordinal)
        {
            if (typeof(T) == typeof(TimeSpan))
            {
                var v = _time;
                return Unsafe.As<TimeSpan, T>(ref v);
            }

            if (typeof(T) == typeof(uint))
            {
                var v = (uint)_row;
                return Unsafe.As<uint, T>(ref v);
            }

            return (T)Value(ordinal);
        }

        public override int GetOrdinal(string name) => Array.IndexOf(Names, name);
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
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
    }

    private static TrackedReader OpenConverting(ConvertingReader reader) =>
        new(reader, new Mock<ITrackedConnection>().Object, Mock.Of<IAsyncDisposable>(), false);

    // Bytes per mapped row, the least of three passes of 2000 rows: a one-off allocation on this
    // thread while the full suite runs (a tier-up, another test's static init) inflates one pass,
    // not three; a per-row allocation shows in every pass.
    private static long LeastAllocatedPerRow<T>(TableGateway<T, int> gateway, Func<TrackedReader> open,
        Func<T, long> use, long expectedSum) where T : class, new()
    {
        var least = long.MaxValue;
        for (var pass = 0; pass < 3; pass++)
        {
            var reader = open();
            reader.Read();
            gateway.MapReaderToObject(reader);
            var before = GC.GetAllocatedBytesForCurrentThread();
            long sum = 0;
            while (reader.Read())
            {
                sum += use(gateway.MapReaderToObject(reader));
            }

            least = Math.Min(least, (GC.GetAllocatedBytesForCurrentThread() - before) / (MeasuredRows - 1));
            Assert.Equal(expectedSum, sum);
        }

        return least;
    }

    private const int MeasuredRows = 2001;
    private const long RowNumberSum = (long)MeasuredRows * (MeasuredRows - 1) / 2;

    // PERF-021: these conversions went through TypeCoercionHelper.Coerce, which boxes its result
    // (and a value-type input).
    [Fact]
    public void DateTimeAndTextConversions_AllocateOnlyTheEntityPerRow()
    {
        var perRow = LeastAllocatedPerRow(Gateway<Converted>(),
            () => OpenConverting(new ConvertingReader(MeasuredRows)), row => row.Unsigned, RowNumberSum);
        Assert.True(perRow <= EntitySize<Converted>(), $"{perRow} B/row; the entity alone is {EntitySize<Converted>()} B");
    }

    [Fact]
    public void DateTimeAndTextConversions_ReadTheSameValuesAsBefore()
    {
        var reader = OpenConverting(new ConvertingReader(1, stampText: "2026-10-04T07:30:00-05:00"));
        reader.Read();

        var row = Gateway<Converted>().MapReaderToObject(reader);

        Assert.Equal(new DateOnly(2026, 10, 4), row.Day);
        Assert.Equal(new TimeOnly(13, 45), row.Time);
        Assert.Equal(new DateTime(2026, 10, 4, 12, 30, 0, DateTimeKind.Utc), row.Stamp);
        Assert.Equal(DateTimeKind.Utc, row.Stamp.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 7, 30, 0, TimeSpan.FromHours(-5)), row.StampOffset);
        Assert.Equal(TimeSpan.FromHours(-5), row.StampOffset.Offset);
        Assert.Equal(new DateOnly(2026, 10, 4), row.TextDay);
    }

    // COR-008: an unsigned column (MySQL INT UNSIGNED reads as uint) into a long property threw
    // InvalidCastException: the boxed value was unboxed as long instead of converted.
    [Fact]
    public void UnsignedColumn_IntoAWiderSignedProperty_IsConverted()
    {
        var reader = OpenConverting(new ConvertingReader(4));
        var gateway = Gateway<Converted>();
        var values = new List<long>();
        while (reader.Read())
        {
            values.Add(gateway.MapReaderToObject(reader).Unsigned);
        }

        Assert.Equal(new long[] { 0, 1, 2, 3 }, values);
    }

    [Theory]
    [InlineData(" ", "2026-10-04", "Blank text")]
    [InlineData("2026-10-04T12:30:00Z", "", "Blank text")]
    [InlineData("not a date", "2026-10-04", "Stamp")]
    public void DateTimeAndTextConversions_FailAsBefore(string stampText, string dayText, string expected)
    {
        var reader = OpenConverting(new ConvertingReader(1, stampText, dayText));
        reader.Read();

        var error = Assert.Throws<pengdows.crud.exceptions.DataMappingException>(
            () => Gateway<Converted>().MapReaderToObject(reader));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void TimeSpanOfADayOrMore_IsNotATimeOnly()
    {
        var reader = OpenConverting(new ConvertingReader(1, time: TimeSpan.FromHours(25)));
        reader.Read();

        var error = Assert.Throws<pengdows.crud.exceptions.DataMappingException>(
            () => Gateway<Converted>().MapReaderToObject(reader));

        Assert.Contains("Time", error.Message);
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
            if (_refusesTypedReads)
            {
                throw new InvalidCastException("typed reads not supported");
            }

            if (ordinal == 1)
            {
                var v = When;
                return Unsafe.As<DateTimeOffset, T>(ref v);
            }

            if (ordinal == 2)
            {
                var v = Span;
                return Unsafe.As<TimeSpan, T>(ref v);
            }

            var i = _row;
            return Unsafe.As<int, T>(ref i);
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
        var perRow = LeastAllocatedPerRow(Gateway<OnlyId>(), () => Open(MeasuredRows), row => row.Id, RowNumberSum);
        Assert.True(perRow <= EntitySize<OnlyId>(), $"{perRow} B/row; the entity alone is {EntitySize<OnlyId>()} B");
    }

    [Fact]
    public void DateTimeOffsetAndTimeSpanColumns_AreNotBoxedPerRow()
    {
        // Span is the row number in minutes.
        var perRow = LeastAllocatedPerRow(Gateway<Row>(), () => Open(MeasuredRows), row => (long)row.Span.TotalMinutes,
            RowNumberSum);
        var entityBytes = EntitySize<Row>();
        Assert.True(perRow <= entityBytes, $"{perRow} B/row; the entity alone is {entityBytes} B");
    }
}
