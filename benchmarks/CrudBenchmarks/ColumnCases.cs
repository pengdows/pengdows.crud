using System.Data;
using System.Data.Common;
using Dapper;
using System.Net;
using System.Net.NetworkInformation;
using NpgsqlTypes;
using pengdows.crud.types.valueobjects;
using TinyHelpers.Dapper.TypeHandlers;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;

namespace CrudBenchmarks;

/// <summary>
/// One single-column hydration case per column type, so FakeDbColumnHydrationBenchmarks can say which types the
/// time goes on. Every case runs the same four mappers over the same rows with the same sync loop: Dapper's parser,
/// a hand-written mapper, pengdows.crud's CompiledMapperFactory (the TableGateway path) and DataReaderMapper's
/// resolved plan (the arbitrary-POCO path), plus the bare reader read as a floor.
/// </summary>
public abstract class ColumnCase
{
    public abstract int RunDapper();
    public abstract int RunManual();
    public abstract int RunCompiled();
    public abstract int RunPlan();
    public abstract int RunFloor();

    /// <summary>False when Dapper has no mapping for the type; its arm then reports -1 instead of a wrong time.</summary>
    public abstract bool DapperSupported { get; }

    /// <summary>
    /// What Dapper's arm is: "built-in" (it maps the type itself), "TinyHelpers.Dapper ..." (the community type handler
    /// a developer installs; Dapper hands conversion to the developer, so its cost is the handler's), or "none".
    /// </summary>
    public abstract string DapperBaseline { get; }

    /// <summary>Null when every mapper produced the expected value for every row; otherwise what differed.</summary>
    public abstract string? Verify();
}

/// <summary>Dapper reading the provider's own type directly: how to build its parser, and whether a mapped value is row <c>i</c>'s.</summary>
public sealed class DirectArm
{
    public required Func<fakeDbDataReader, Func<DbDataReader, object?>> CreateParser { get; init; }

    public required Func<object?, int, bool> Matches { get; init; }
}

public static class ColumnCases
{
    public static string[] Names { get; } =
    [
        "Int32", "Int64", "String", "Decimal", "Boolean", "DateTime", "DateTimeOffset", "Double", "Single", "Guid", "Bytes",
        "NullableInt32", "NullableInt32Null", "StringNull", "Byte", "Int16", "SByte", "UInt16", "UInt32", "UInt64",
        "Char", "TimeSpan", "DateOnly", "TimeOnly", "EnumInt", "EnumString",
        "JsonDocument", "JsonElement",
        "PgRangeInt", "PgRangeTstz", "PgInterval", "PgInet", "PgCidr", "PgMacAddr", "PgIntArray", "PgTextArray", "PgHStore"
    ];

    public static ColumnCase Create(string column, int rowCount) => column switch
    {
        "Int32" => new ColumnCase<IntEntity, int>(rowCount, typeof(int), "INTEGER", i => i,
            r => new IntEntity { V = r.GetInt32(0) }, e => e.V),
        "Int64" => new ColumnCase<LongEntity, long>(rowCount, typeof(long), "BIGINT", i => 10_000_000_000L + i,
            r => new LongEntity { V = r.GetInt64(0) }, e => e.V),
        "String" => new ColumnCase<StringEntity, string>(rowCount, typeof(string), "TEXT", i => $"value {i}",
            r => new StringEntity { V = r.GetString(0) }, e => e.V),
        "Decimal" => new ColumnCase<DecimalEntity, decimal>(rowCount, typeof(decimal), "DECIMAL", i => 50_000m + i * 10m,
            r => new DecimalEntity { V = r.GetDecimal(0) }, e => e.V),
        "Boolean" => new ColumnCase<BoolEntity, bool>(rowCount, typeof(bool), "BOOLEAN", i => i % 2 == 0,
            r => new BoolEntity { V = r.GetBoolean(0) }, e => e.V),
        "DateTime" => new ColumnCase<DateTimeEntity, DateTime>(rowCount, typeof(DateTime), "TIMESTAMP",
            i => DateTime.UnixEpoch.AddMinutes(-i), r => new DateTimeEntity { V = r.GetDateTime(0) }, e => e.V),
        "DateTimeOffset" => new ColumnCase<OffsetEntity, DateTimeOffset>(rowCount, typeof(DateTimeOffset), "TIMESTAMP_TZ",
            i => new DateTimeOffset(DateTime.UnixEpoch.AddMinutes(-i), TimeSpan.Zero),
            r => new OffsetEntity { V = r.GetFieldValue<DateTimeOffset>(0) }, e => e.V),
        "Double" => new ColumnCase<DoubleEntity, double>(rowCount, typeof(double), "DOUBLE", i => i * 1.25d,
            r => new DoubleEntity { V = r.GetDouble(0) }, e => e.V),
        "Single" => new ColumnCase<SingleEntity, float>(rowCount, typeof(float), "REAL", i => i / 3.0f,
            r => new SingleEntity { V = r.GetFloat(0) }, e => e.V),
        "Guid" => new ColumnCase<GuidEntity, Guid>(rowCount, typeof(Guid), "UUID", i => new Guid(i, 0, 0, new byte[8]),
            r => new GuidEntity { V = r.GetGuid(0) }, e => e.V),
        "Bytes" => new ColumnCase<BytesEntity, byte[]>(rowCount, typeof(byte[]), "BLOB", i => BitConverter.GetBytes(i),
            r => new BytesEntity { V = (byte[])r.GetValue(0) }, e => e.V),
        "NullableInt32" => new ColumnCase<NullableIntEntity, int?>(rowCount, typeof(int), "INTEGER", i => i,
            r => new NullableIntEntity { V = r.IsDBNull(0) ? null : r.GetInt32(0) }, e => e.V),
        "NullableInt32Null" => new ColumnCase<NullableIntEntity, int?>(rowCount, typeof(int), "INTEGER", _ => DBNull.Value,
            r => new NullableIntEntity { V = r.IsDBNull(0) ? null : r.GetInt32(0) }, e => e.V, _ => null),
        "StringNull" => new ColumnCase<NullStringEntity, string?>(rowCount, typeof(string), "TEXT", _ => DBNull.Value,
            r =>
            {
                var e = new NullStringEntity();
                if (!r.IsDBNull(0))
                {
                    e.V = r.GetString(0);
                }

                return e;
            }, e => e.V, _ => "unset"),
        "Byte" => new ColumnCase<ByteEntity, byte>(rowCount, typeof(byte), "TINYINT", i => (byte)(i % 256),
            r => new ByteEntity { V = r.GetFieldValue<byte>(0) }, e => e.V),
        "Int16" => new ColumnCase<ShortEntity, short>(rowCount, typeof(short), "SMALLINT", i => (short)(i % 30000),
            r => new ShortEntity { V = r.GetInt16(0) }, e => e.V),
        "SByte" => new ColumnCase<SByteEntity, sbyte>(rowCount, typeof(sbyte), "TINYINT", i => (sbyte)(i % 100),
            r => new SByteEntity { V = r.GetFieldValue<sbyte>(0) }, e => e.V),
        "UInt16" => new ColumnCase<UShortEntity, ushort>(rowCount, typeof(ushort), "SMALLINT UNSIGNED", i => (ushort)(i % 60000),
            r => new UShortEntity { V = r.GetFieldValue<ushort>(0) }, e => e.V),
        "UInt32" => new ColumnCase<UIntEntity, uint>(rowCount, typeof(uint), "INT UNSIGNED", i => (uint)i,
            r => new UIntEntity { V = r.GetFieldValue<uint>(0) }, e => e.V),
        "UInt64" => new ColumnCase<ULongEntity, ulong>(rowCount, typeof(ulong), "BIGINT UNSIGNED", i => (ulong)i * 1_000_000UL,
            r => new ULongEntity { V = r.GetFieldValue<ulong>(0) }, e => e.V),
        // A char column comes back from most providers as a one-character string.
        "Char" => new ColumnCase<CharEntity, char>(rowCount, typeof(string), "CHAR(1)", i => ((char)('a' + i % 26)).ToString(),
            r => new CharEntity { V = r.GetString(0)[0] }, e => e.V, i => (char)('a' + i % 26)),
        "TimeSpan" => new ColumnCase<TimeSpanEntity, TimeSpan>(rowCount, typeof(TimeSpan), "TIME", i => TimeSpan.FromSeconds(i),
            r => new TimeSpanEntity { V = r.GetFieldValue<TimeSpan>(0) }, e => e.V),
        // A date column comes back as a DateTime; a time column as a TimeSpan.
        "DateOnly" => new ColumnCase<DateOnlyEntity, DateOnly>(rowCount, typeof(DateTime), "DATE", i => DateTime.UnixEpoch.AddDays(i),
            r => new DateOnlyEntity { V = DateOnly.FromDateTime(r.GetDateTime(0)) }, e => e.V,
            i => DateOnly.FromDateTime(DateTime.UnixEpoch.AddDays(i))),
        "TimeOnly" => new ColumnCase<TimeOnlyEntity, TimeOnly>(rowCount, typeof(TimeSpan), "TIME", i => TimeSpan.FromSeconds(i),
            r => new TimeOnlyEntity { V = TimeOnly.FromTimeSpan(r.GetFieldValue<TimeSpan>(0)) }, e => e.V,
            i => TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(i))),
        "EnumInt" => new ColumnCase<EnumIntEntity, Mood>(rowCount, typeof(int), "INTEGER", i => i % 3,
            r => new EnumIntEntity { V = (Mood)r.GetInt32(0) }, e => e.V, i => (Mood)(i % 3)),
        "EnumString" => new ColumnCase<EnumStringEntity, Mood>(rowCount, typeof(string), "TEXT", i => ((Mood)(i % 3)).ToString(),
            r => new EnumStringEntity { V = Enum.Parse<Mood>(r.GetString(0)) }, e => e.V, i => (Mood)(i % 3)),
        // Dapper reads JSON only through a handler the developer installs; TinyHelpers.Dapper's JsonTypeHandler<T> is the
        // community one (System.Text.Json), registered as its README shows.
        // PostgreSQL types. pengdows.crud returns portable, database-agnostic value objects. A Dapper user on Npgsql declares
        // Npgsql's own type as the property (NpgsqlRange<int>, NpgsqlInet ...): no handler and no conversion, which is the
        // cheapest possible read and not portable. That is Dapper's arm here.
        "PgRangeInt" => new ColumnCase<PgRangeIntEntity, Range<int>>(rowCount, typeof(NpgsqlRange<int>), "int4range",
            i => new NpgsqlRange<int>(i, true, i + 10, false),
            r => { var v = r.GetFieldValue<NpgsqlRange<int>>(0); return new PgRangeIntEntity { V = new Range<int>(v.LowerBound, v.UpperBound, v.LowerBoundIsInclusive, v.UpperBoundIsInclusive) }; },
            e => e.V, i => new Range<int>(i, i + 10, true, false),
            direct: Direct<DRangeInt>(e => e.V, (v, i) => v is NpgsqlRange<int> n && n.LowerBound == i && n.UpperBound == i + 10)),
        "PgRangeTstz" => new ColumnCase<PgRangeTstzEntity, Range<DateTimeOffset>>(rowCount, typeof(NpgsqlRange<DateTime>), "tstzrange",
            i => new NpgsqlRange<DateTime>(TstzLower(i), true, TstzLower(i).AddDays(30), false),
            r => { var v = r.GetFieldValue<NpgsqlRange<DateTime>>(0); return new PgRangeTstzEntity { V = new Range<DateTimeOffset>(new DateTimeOffset(v.LowerBound), new DateTimeOffset(v.UpperBound), v.LowerBoundIsInclusive, v.UpperBoundIsInclusive) }; },
            e => e.V, i => new Range<DateTimeOffset>(new DateTimeOffset(TstzLower(i)), new DateTimeOffset(TstzLower(i).AddDays(30)), true, false),
            direct: Direct<DRangeTstz>(e => e.V, (v, i) => v is NpgsqlRange<DateTime> n && n.LowerBound == TstzLower(i))),
        "PgInterval" => new ColumnCase<PgIntervalEntity, PostgreSqlInterval>(rowCount, typeof(NpgsqlInterval), "interval",
            i => new NpgsqlInterval(i % 12, i % 28, i * 1_000_000L),
            r => { var v = r.GetFieldValue<NpgsqlInterval>(0); return new PgIntervalEntity { V = new PostgreSqlInterval(v.Months, v.Days, v.Time) }; },
            e => e.V, i => new PostgreSqlInterval(i % 12, i % 28, i * 1_000_000L),
            direct: Direct<DInterval>(e => e.V, (v, i) => v is NpgsqlInterval n && n.Months == i % 12 && n.Time == i * 1_000_000L)),
        "PgInet" => new ColumnCase<PgInetEntity, Inet>(rowCount, typeof(NpgsqlInet), "inet",
            i => new NpgsqlInet(IPAddress.Parse("10.0." + i % 255 + ".1"), 24),
            r => { var v = r.GetFieldValue<NpgsqlInet>(0); return new PgInetEntity { V = Inet.FromProvider(v.Address, (byte)v.Netmask) }; },
            e => e.V, i => Inet.Parse("10.0." + i % 255 + ".1/24"),
            direct: Direct<DInet>(e => e.V, (v, i) => v is NpgsqlInet n && n.Address.ToString() == "10.0." + i % 255 + ".1")),
        "PgCidr" => new ColumnCase<PgCidrEntity, Cidr>(rowCount, typeof(NpgsqlCidr), "cidr",
            i => new NpgsqlCidr(IPAddress.Parse("10." + i % 255 + ".0.0"), 16),
            r => { var v = r.GetFieldValue<NpgsqlCidr>(0); return new PgCidrEntity { V = new Cidr(v.Address, (byte)v.Netmask) }; },
            e => e.V, i => Cidr.Parse("10." + i % 255 + ".0.0/16"),
            direct: Direct<DCidr>(e => e.V, (v, i) => v is NpgsqlCidr n && n.Address.ToString() == "10." + i % 255 + ".0.0")),
        "PgMacAddr" => new ColumnCase<PgMacEntity, MacAddress>(rowCount, typeof(PhysicalAddress), "macaddr",
            i => new PhysicalAddress(new byte[] { 8, 0, 43, 1, (byte)(i % 256), 3 }),
            r => new PgMacEntity { V = new MacAddress(r.GetFieldValue<PhysicalAddress>(0)) },
            e => e.V, i => new MacAddress(new PhysicalAddress(new byte[] { 8, 0, 43, 1, (byte)(i % 256), 3 })),
            direct: Direct<DMac>(e => e.V, (v, i) => v is PhysicalAddress p && p.GetAddressBytes()[4] == (byte)(i % 256))),
        "PgIntArray" => new ColumnCase<PgIntArrayEntity, int[]?>(rowCount, typeof(int[]), "integer[]",
            i => new[] { i, i + 1 },
            r => new PgIntArrayEntity { V = (int[])r.GetValue(0) }, e => e.V, i => new[] { i, i + 1 },
            same: (actual, expected) => actual!.AsSpan().SequenceEqual((int[])expected!)),
        "PgTextArray" => new ColumnCase<PgTextArrayEntity, string[]?>(rowCount, typeof(string[]), "text[]",
            i => new[] { "a" + i, "b" },
            r => new PgTextArrayEntity { V = (string[])r.GetValue(0) }, e => e.V, i => new[] { "a" + i, "b" },
            same: (actual, expected) => actual!.AsSpan().SequenceEqual((string[])expected!)),
        "PgHStore" => new ColumnCase<PgHStoreEntity, HStore>(rowCount, typeof(Dictionary<string, string?>), "hstore",
            i => new Dictionary<string, string?> { ["a"] = "1", ["k" + i] = null },
            r => new PgHStoreEntity { V = new HStore((Dictionary<string, string?>)r.GetValue(0)) },
            e => e.V, i => new HStore(new Dictionary<string, string?> { ["a"] = "1", ["k" + i] = null }),
            same: (actual, expected) => actual.ToString() == expected!.ToString(),
            direct: Direct<DHStore>(e => e.V, (v, i) => v is Dictionary<string, string?> d && d.ContainsKey("k" + i))),
        "JsonDocument" => JsonDocumentCase(rowCount),
        "JsonElement" => JsonElementCase(rowCount),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Unknown column case.")
    };

    private static DateTime TstzLower(int i) => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i);

    /// <summary>Dapper reading the provider's own type as the property: nothing to convert.</summary>
    private static DirectArm Direct<TDapper>(Func<TDapper, object?> get, Func<object?, int, bool> matches)
        where TDapper : class, new() =>
        new()
        {
            CreateParser = schema =>
            {
                var parser = schema.GetRowParser<TDapper>();
                return reader => get(parser(reader));
            },
            Matches = matches
        };

    private static string JsonText(int i) => "{\"id\":" + i + ",\"tags\":[\"a\",\"b\"],\"ok\":true}";

    private static ColumnCase JsonDocumentCase(int rowCount)
    {
        JsonTypeHandler<System.Text.Json.JsonDocument>.Configure();
        return new ColumnCase<JsonDocumentEntity, System.Text.Json.JsonDocument?>(rowCount, typeof(string), "JSON", i => JsonText(i),
            r => new JsonDocumentEntity { V = System.Text.Json.JsonDocument.Parse(r.GetString(0)) }, e => e.V,
            same: (actual, expected) => actual!.RootElement.GetRawText() == (string)expected!,
            dapperBaseline: "TinyHelpers.Dapper JsonTypeHandler<JsonDocument>");
    }

    private static ColumnCase JsonElementCase(int rowCount)
    {
        JsonTypeHandler<System.Text.Json.JsonElement>.Configure();
        return new ColumnCase<JsonElementEntity, System.Text.Json.JsonElement>(rowCount, typeof(string), "JSON", i => JsonText(i),
            r => new JsonElementEntity { V = System.Text.Json.JsonDocument.Parse(r.GetString(0)).RootElement.Clone() }, e => e.V,
            same: (actual, expected) => actual.GetRawText() == (string)expected!,
            dapperBaseline: "TinyHelpers.Dapper JsonTypeHandler<JsonElement>");
    }

    /// <summary>Used by the shape tests: the first mismatch between a mapper and the expected value, or null.</summary>
    public static string? Verify(string column, int rowCount) => Create(column, rowCount).Verify();

    // [PrimaryKey] only satisfies the entity metadata every pengdows.crud mapper path needs; nothing here writes SQL.
    [Table("c_int")] public sealed class IntEntity { [PrimaryKey(1)] [Column("V", DbType.Int32)] public int V { get; set; } }
    [Table("c_long")] public sealed class LongEntity { [PrimaryKey(1)] [Column("V", DbType.Int64)] public long V { get; set; } }
    [Table("c_str")] public sealed class StringEntity { [PrimaryKey(1)] [Column("V", DbType.String)] public string V { get; set; } = string.Empty; }
    [Table("c_dec")] public sealed class DecimalEntity { [PrimaryKey(1)] [Column("V", DbType.Decimal)] public decimal V { get; set; } }
    [Table("c_bool")] public sealed class BoolEntity { [PrimaryKey(1)] [Column("V", DbType.Boolean)] public bool V { get; set; } }
    [Table("c_dt")] public sealed class DateTimeEntity { [PrimaryKey(1)] [Column("V", DbType.DateTime)] public DateTime V { get; set; } }
    [Table("c_dto")] public sealed class OffsetEntity { [PrimaryKey(1)] [Column("V", DbType.DateTimeOffset)] public DateTimeOffset V { get; set; } }
    [Table("c_dbl")] public sealed class DoubleEntity { [PrimaryKey(1)] [Column("V", DbType.Double)] public double V { get; set; } }
    [Table("c_sgl")] public sealed class SingleEntity { [PrimaryKey(1)] [Column("V", DbType.Single)] public float V { get; set; } }
    [Table("c_guid")] public sealed class GuidEntity { [PrimaryKey(1)] [Column("V", DbType.Guid)] public Guid V { get; set; } }
    public enum Mood { Sad, Ok, Happy }

    [Table("c_nint")] public sealed class NullableIntEntity { [PrimaryKey(1)] [Column("V", DbType.Int32)] public int? V { get; set; } }
    [Table("c_nstr")] public sealed class NullStringEntity { [PrimaryKey(1)] [Column("V", DbType.String)] public string? V { get; set; } = "unset"; }
    [Table("c_byte")] public sealed class ByteEntity { [PrimaryKey(1)] [Column("V", DbType.Byte)] public byte V { get; set; } }
    [Table("c_short")] public sealed class ShortEntity { [PrimaryKey(1)] [Column("V", DbType.Int16)] public short V { get; set; } }
    [Table("c_sbyte")] public sealed class SByteEntity { [PrimaryKey(1)] [Column("V", DbType.SByte)] public sbyte V { get; set; } }
    [Table("c_ushort")] public sealed class UShortEntity { [PrimaryKey(1)] [Column("V", DbType.UInt16)] public ushort V { get; set; } }
    [Table("c_uint")] public sealed class UIntEntity { [PrimaryKey(1)] [Column("V", DbType.UInt32)] public uint V { get; set; } }
    [Table("c_ulong")] public sealed class ULongEntity { [PrimaryKey(1)] [Column("V", DbType.UInt64)] public ulong V { get; set; } }
    [Table("c_char")] public sealed class CharEntity { [PrimaryKey(1)] [Column("V", DbType.String)] public char V { get; set; } }
    [Table("c_ts")] public sealed class TimeSpanEntity { [PrimaryKey(1)] [Column("V", DbType.Time)] public TimeSpan V { get; set; } }
    [Table("c_date")] public sealed class DateOnlyEntity { [PrimaryKey(1)] [Column("V", DbType.Date)] public DateOnly V { get; set; } }
    [Table("c_time")] public sealed class TimeOnlyEntity { [PrimaryKey(1)] [Column("V", DbType.Time)] public TimeOnly V { get; set; } }
    [Table("c_enumi")] public sealed class EnumIntEntity { [PrimaryKey(1)] [Column("V", DbType.Int32)] public Mood V { get; set; } }
    [Table("c_enums")] public sealed class EnumStringEntity { [PrimaryKey(1)] [Column("V", DbType.String)] public Mood V { get; set; } }
    [Table("c_jsondoc")] public sealed class JsonDocumentEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public System.Text.Json.JsonDocument? V { get; set; } }
    [Table("c_jsonel")] public sealed class JsonElementEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public System.Text.Json.JsonElement V { get; set; } }
    // Portable value objects (what pengdows.crud returns) ...
    [Table("c_pg_rangeint")] public sealed class PgRangeIntEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public Range<int> V { get; set; } }
    [Table("c_pg_rangetstz")] public sealed class PgRangeTstzEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public Range<DateTimeOffset> V { get; set; } }
    [Table("c_pg_interval")] public sealed class PgIntervalEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public PostgreSqlInterval V { get; set; } }
    [Table("c_pg_inet")] public sealed class PgInetEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public Inet V { get; set; } }
    [Table("c_pg_cidr")] public sealed class PgCidrEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public Cidr V { get; set; } }
    [Table("c_pg_mac")] public sealed class PgMacEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public MacAddress V { get; set; } }
    [Table("c_pg_intarray")] public sealed class PgIntArrayEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public int[]? V { get; set; } }
    [Table("c_pg_textarray")] public sealed class PgTextArrayEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public string[]? V { get; set; } }
    [Table("c_pg_hstore")] public sealed class PgHStoreEntity { [PrimaryKey(1)] [Column("V", DbType.Object)] public HStore V { get; set; } }

    // ... and Npgsql's own types, which is what a Dapper user declares (Dapper needs no attributes).
    public sealed class DRangeInt { public NpgsqlRange<int> V { get; set; } }
    public sealed class DRangeTstz { public NpgsqlRange<DateTime> V { get; set; } }
    public sealed class DInterval { public NpgsqlInterval V { get; set; } }
    public sealed class DInet { public NpgsqlInet V { get; set; } }
    public sealed class DCidr { public NpgsqlCidr V { get; set; } }
    public sealed class DMac { public PhysicalAddress? V { get; set; } }
    public sealed class DHStore { public Dictionary<string, string?>? V { get; set; } }

    [Table("c_bin")] public sealed class BytesEntity { [PrimaryKey(1)] [Column("V", DbType.Binary)] public byte[] V { get; set; } = []; }
}

public sealed class ColumnCase<TEntity, TValue> : ColumnCase where TEntity : class, new()
{
    private readonly int _rowCount;
    private readonly List<Dictionary<string, object>> _rows;
    private readonly IReadOnlyList<fakeDbColumn> _columns;
    private readonly Func<int, object> _value;
    private readonly Func<DbDataReader, TEntity> _manual;
    private readonly Func<TEntity, TValue> _get;
    private readonly Func<int, object?> _expected;
    private readonly Func<TValue, object?, bool>? _same;
    private readonly string _dapperBaseline;
    private readonly DirectArm? _direct;
    private readonly Func<DbDataReader, object?>? _directParser;
    private readonly Func<DbDataReader, TEntity>? _dapper;
    private readonly Func<DbDataReader, TEntity> _compiled;
    private readonly Func<DbDataReader, TEntity> _plan;
    private TEntity? _sink;

    public ColumnCase(int rowCount, Type clrType, string typeName, Func<int, object> value,
        Func<DbDataReader, TEntity> manual, Func<TEntity, TValue> get, Func<int, object?>? expected = null,
        Func<TValue, object?, bool>? same = null, string dapperBaseline = "built-in", DirectArm? direct = null)
    {
        _direct = direct;
        _same = same;
        _dapperBaseline = dapperBaseline;
        _rowCount = rowCount;
        _value = value;
        _expected = expected ?? (i => value(i));
        _manual = manual;
        _get = get;
        _columns = [new fakeDbColumn("V", clrType, typeName)];
        _rows = new List<Dictionary<string, object>>(rowCount);
        for (var i = 1; i <= rowCount; i++)
        {
            _rows.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["V"] = value(i) });
        }

        using var reader = NewReader();
        _dapper = TryDapper(reader);
        _directParser = TryDirect(reader);
        var columnMap = new TypeMapRegistry().GetTableInfo<TEntity>().Columns;
        _compiled = CompiledMapperFactory<TEntity>.Create<DbDataReader>(
            reader, columnMap, EnumParseFailureMode.Throw, ["V"], [clrType]);
        _plan = DataReaderMapper.ResolveRowMapper<TEntity>(reader, MapperOptions.Default);
    }

    private fakeDbDataReader NewReader() => new(_rows) { Columns = _columns };

    // Dapper has no mapping for some types (it throws when it parses the column); then there is nothing to compare.
    private Func<DbDataReader, TEntity>? TryDapper(fakeDbDataReader schemaReader)
    {
        try
        {
            var parser = schemaReader.GetRowParser<TEntity>();
            using var probe = NewReader();
            if (probe.Read())
            {
                _ = parser(probe);
            }

            return parser;
        }
        catch
        {
            return null;
        }
    }

    private Func<DbDataReader, object?>? TryDirect(fakeDbDataReader schemaReader)
    {
        if (_direct == null)
        {
            return null;
        }

        try
        {
            var parser = _direct.CreateParser(schemaReader);
            using var probe = NewReader();
            if (probe.Read())
            {
                _ = parser(probe);
            }

            return parser;
        }
        catch
        {
            return null;
        }
    }

    public override bool DapperSupported => _directParser != null || (_direct == null && _dapper != null);

    public override string DapperBaseline =>
        !DapperSupported ? "none" : _direct != null ? "built-in (provider type directly)" : _dapperBaseline;

    private int Run(Func<DbDataReader, TEntity> mapper)
    {
        using var reader = NewReader();
        var count = 0;
        while (reader.Read())
        {
            _sink = mapper(reader);
            count++;
        }

        return count;
    }

    public override int RunDapper()
    {
        if (_direct != null)
        {
            return _directParser == null ? -1 : RunDirect(_directParser);
        }

        return _dapper == null ? -1 : Run(_dapper);
    }

    private object? _directSink;

    private int RunDirect(Func<DbDataReader, object?> parser)
    {
        using var reader = NewReader();
        var count = 0;
        while (reader.Read())
        {
            _directSink = parser(reader);
            count++;
        }

        return count;
    }
    public override int RunManual() => Run(_manual);
    public override int RunCompiled() => Run(_compiled);
    public override int RunPlan() => Run(_plan);

    public override int RunFloor()
    {
        using var reader = NewReader();
        var count = 0;
        while (reader.Read())
        {
            _ = reader.GetValue(0);
            count++;
        }

        return count;
    }

    public override string? Verify()
    {
        foreach (var (name, mapper) in new (string, Func<DbDataReader, TEntity>?)[]
                 {
                     ("Dapper", _dapper), ("Manual", _manual), ("CompiledMapper", _compiled), ("ReaderMapperPlan", _plan)
                 })
        {
            if (mapper == null)
            {
                continue;
            }

            using var reader = NewReader();
            var row = 1;
            while (reader.Read())
            {
                var actual = _get(mapper(reader));
                var expected = _expected(row);
                var same = _same != null
                    ? _same(actual, expected)
                    : actual is byte[] a && expected is byte[] e ? a.AsSpan().SequenceEqual(e) : Equals(actual, expected);
                if (!same)
                {
                    return $"{name} row {row}: expected {expected}, got {actual}";
                }

                row++;
            }
        }

        if (_directParser != null)
        {
            using var reader = NewReader();
            var row = 1;
            while (reader.Read())
            {
                if (!_direct!.Matches(_directParser(reader), row))
                {
                    return $"Dapper (provider type directly) row {row}: not the stored value";
                }

                row++;
            }
        }

        return null;
    }
}
