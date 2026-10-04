using System.Collections;
using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace CrudBenchmarks;

/// <summary>
/// REV-085: BenchmarkDotNet evidence for the 2026-10-04 read-path changes, isolated from any driver.
/// <list type="bullet">
/// <item><see cref="GovernorAcquireRelease"/>: an uncontended pool-governor slot (PERF-024: no drain
/// signal per acquire, no lock).</item>
/// <item><see cref="MapRow"/>: gateway hydration of one row from an in-memory reader with string,
/// DateTimeOffset, TimeSpan, DateTime, decimal and int columns (PERF-017 string read, PERF-020 typed
/// reads, PERF-021/DRY-004 conversions). The reader itself allocates nothing per row.</item>
/// </list>
/// Run the same file against two builds to compare (see the results file).
/// </summary>
[OptInBenchmark]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 15)]
public class ReadPathMicroBenchmarks
{
    private PoolGovernor _governor = null!;
    private TableGateway<Row, int> _gateway = null!;
    private TrackedReader _reader = null!;

    [Table("t")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Column("at", DbType.DateTimeOffset)] public DateTimeOffset At { get; set; }
        [Column("span", DbType.Time)] public TimeSpan Span { get; set; }
        [Column("created", DbType.DateTime)] public DateTime Created { get; set; }
        [Column("amount", DbType.Decimal)] public decimal Amount { get; set; }
    }

    [GlobalSetup]
    public void Setup()
    {
        _governor = new PoolGovernor(PoolLabel.Writer, "bench", 1, TimeSpan.FromSeconds(5));
        var context = new DatabaseContext("Data Source=bench;EmulatedProduct=SqlServer",
            new fakeDbFactory(SupportedDatabase.SqlServer));
        _gateway = new TableGateway<Row, int>(context);
        _reader = new TrackedReader(new EndlessRowReader(), null!, null!, false);
        _reader.Read();
        _gateway.MapReaderToObject(_reader);
    }

    [GlobalCleanup]
    public void Cleanup() => _governor.Dispose();

    [Benchmark]
    public async Task GovernorAcquireRelease()
    {
        var slot = await _governor.AcquireAsync();
        slot.Dispose();
    }

    [Benchmark]
    public Row MapRow()
    {
        _reader.Read();
        return _gateway.MapReaderToObject(_reader);
    }

    // One row, returned forever: typed getters return values without allocating; GetValue boxes, as
    // drivers do.
    private sealed class EndlessRowReader : DbDataReader
    {
        private static readonly string[] Names = { "id", "name", "at", "span", "created", "amount" };
        private static readonly Type[] Types =
            { typeof(int), typeof(string), typeof(DateTimeOffset), typeof(TimeSpan), typeof(DateTime), typeof(decimal) };

        private const string Name = "a reasonably ordinary name";
        private static readonly DateTimeOffset At = new(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(-5));
        private static readonly TimeSpan Span = new(1, 2, 3);
        private static readonly DateTime Created = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Unspecified);

        public override bool Read() => true;
        public override int FieldCount => Names.Length;
        public override string GetName(int ordinal) => Names[ordinal];
        public override Type GetFieldType(int ordinal) => Types[ordinal];
        public override bool IsDBNull(int ordinal) => false;
        public override int GetInt32(int ordinal) => 7;
        public override string GetString(int ordinal) => Name;
        public override DateTime GetDateTime(int ordinal) => Created;
        public override decimal GetDecimal(int ordinal) => 12.34m;

        public override object GetValue(int ordinal) => ordinal switch
        {
            0 => 7,
            1 => Name,
            2 => At,
            3 => Span,
            4 => Created,
            _ => 12.34m
        };

        public override T GetFieldValue<T>(int ordinal)
        {
            // Without boxing, as a driver's typed read is.
            if (typeof(T) == typeof(DateTimeOffset))
            {
                var at = At;
                return System.Runtime.CompilerServices.Unsafe.As<DateTimeOffset, T>(ref at);
            }

            if (typeof(T) == typeof(TimeSpan))
            {
                var span = Span;
                return System.Runtime.CompilerServices.Unsafe.As<TimeSpan, T>(ref span);
            }

            return (T)GetValue(ordinal);
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
        public override string GetDataTypeName(int ordinal) => Types[ordinal].Name;
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int o, long d, byte[]? b, int bo, int l) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int o, long d, char[]? b, int bo, int l) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
    }
}
