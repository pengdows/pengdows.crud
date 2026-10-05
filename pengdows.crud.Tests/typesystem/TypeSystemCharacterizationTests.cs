using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// DRY-010 stage 0: pins what the type system does today, on every database, so centralizing it
/// changes nothing it isn't meant to. Three views: the parameter each dialect's CreateDbParameter
/// produces per value, the SQL and parameters BuildCreate produces per entity (column rendering and
/// marking), and TypeCoercionHelper.Coerce from each stored shape into each target type.
/// PIN_TYPES=1 rewrites the pinned files from the current code; any other difference fails.
/// </summary>
[Collection("TypeRegistry")]
public class TypeSystemCharacterizationTests
{
    public enum Mood
    {
        Happy = 1,
        Sad = 2
    }

    // ── Writes: CreateDbParameter per value ─────────────────────────────────────────────

    private static IEnumerable<(string Label, DbType Type, object? Value)> WriteSamples()
    {
        yield return ("null String", DbType.String, null);
        yield return ("DBNull Int32", DbType.Int32, DBNull.Value);
        yield return ("bool", DbType.Boolean, true);
        yield return ("byte", DbType.Byte, (byte)200);
        yield return ("sbyte", DbType.SByte, (sbyte)-5);
        yield return ("short", DbType.Int16, (short)-300);
        yield return ("ushort", DbType.UInt16, (ushort)60000);
        yield return ("int", DbType.Int32, -42);
        yield return ("uint", DbType.UInt32, 4000000000u);
        yield return ("long", DbType.Int64, long.MinValue);
        yield return ("ulong", DbType.UInt64, ulong.MaxValue);
        yield return ("float", DbType.Single, 1.5f);
        yield return ("double", DbType.Double, 0.1);
        yield return ("decimal", DbType.Decimal, 123.4500m);
        yield return ("string", DbType.String, "héllo");
        yield return ("ansi string", DbType.AnsiString, "abc");
        yield return ("char as String", DbType.String, 'x');
        yield return ("Guid as Guid", DbType.Guid, new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"));
        yield return ("Guid as String", DbType.String, new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"));
        yield return ("Guid as Binary", DbType.Binary, new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"));
        yield return ("DateTime Utc", DbType.DateTime, new DateTime(2026, 10, 5, 13, 45, 30, DateTimeKind.Utc).AddTicks(1234567));
        yield return ("DateTime Unspecified DateTime2", DbType.DateTime2, new DateTime(2026, 10, 5, 13, 45, 30).AddTicks(1234567));
        yield return ("DateTime as Date", DbType.Date, new DateTime(2026, 10, 5));
        yield return ("DateTimeOffset", DbType.DateTimeOffset, new DateTimeOffset(2026, 10, 5, 13, 45, 30, TimeSpan.FromHours(2)).AddTicks(1234567));
        yield return ("null DateTimeOffset", DbType.DateTimeOffset, null);
        yield return ("DateTime Utc as DateTimeOffset", DbType.DateTimeOffset, new DateTime(2026, 10, 5, 13, 45, 30, DateTimeKind.Utc).AddTicks(1234567));
        yield return ("DateTime Unspecified as DateTimeOffset", DbType.DateTimeOffset, new DateTime(2026, 10, 5, 13, 45, 30).AddTicks(1234567));
        yield return ("text as DateTimeOffset", DbType.DateTimeOffset, "2026-10-05T13:45:30.1234567+02:00");
        yield return ("DateOnly", DbType.Date, new DateOnly(2026, 10, 5));
        yield return ("TimeOnly", DbType.Time, new TimeOnly(13, 45, 30).Add(TimeSpan.FromTicks(1234567)));
        yield return ("TimeSpan as Time", DbType.Time, new TimeSpan(0, 13, 45, 30).Add(TimeSpan.FromTicks(1234567)));
        yield return ("TimeSpan as Object", DbType.Object, TimeSpan.FromDays(2));
        yield return ("byte[]", DbType.Binary, new byte[] { 1, 2, 255 });
        yield return ("enum as String", DbType.String, Mood.Sad);
        yield return ("enum as Int32", DbType.Int32, Mood.Sad);
        yield return ("int[]", DbType.Object, new[] { 1, -2 });
        yield return ("long?[]", DbType.Object, new long?[] { 1, null });
        yield return ("string[]", DbType.Object, new[] { "a", "b\"c" });
        yield return ("float[]", DbType.Object, new[] { 1.5f, -2f });
        yield return ("double[]", DbType.Object, new[] { 0.5, 2.0 });
        yield return ("JsonDocument", DbType.Object, JsonDocument.Parse("{\"a\":1}"));
        yield return ("JsonElement", DbType.Object, JsonDocument.Parse("[1,2]").RootElement);
        yield return ("Geometry", DbType.Object, Geometry.FromWellKnownText("POINT(1 2)", 4326));
        yield return ("Geography", DbType.Object, Geography.FromWellKnownText("POINT(1 2)", 4326));
        yield return ("Inet", DbType.Object, Inet.Parse("192.168.0.1/24"));
        yield return ("Cidr", DbType.Object, Cidr.Parse("10.0.0.0/8"));
        yield return ("MacAddress", DbType.Object, MacAddress.Parse("08:00:2b:01:02:03"));
        yield return ("IntervalYearMonth", DbType.Object, IntervalYearMonth.Parse("P1Y2M"));
        yield return ("IntervalDaySecond", DbType.Object, IntervalDaySecond.Parse("P1DT2H3M4S"));
        yield return ("Range<int>", DbType.Object, Range<int>.Parse("[1,5)"));
        yield return ("HierarchyId", DbType.Object, HierarchyId.Parse("/1/2/"));
        yield return ("RowVersion", DbType.Binary, RowVersion.FromBytes(new byte[] { 0, 0, 0, 0, 0, 0, 0, 9 }));
        yield return ("BitArray", DbType.Object, new BitArray(new[] { true, false, true }));
        yield return ("Int128", DbType.Object, (Int128)long.MaxValue * 4);
        yield return ("BigInteger", DbType.Object, BigInteger.Parse("123456789012345678901234567890"));
    }

    // ── Writes: BuildCreate per entity (rendering and marking) ──────────────────────────

    [Table("t_scalar")]
    public class Scalars
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("b", DbType.Boolean)] public bool B { get; set; } = true;
        [Column("l", DbType.Int64)] public long L { get; set; } = 7;
        [Column("d", DbType.Decimal)] public decimal D { get; set; } = 1.25m;
        [Column("f", DbType.Double)] public double F { get; set; } = 0.5;
        [Column("s", DbType.String)] public string? S { get; set; } = "x";
        [Column("g", DbType.Guid)] public Guid G { get; set; } = new("0f8fad5b-d9cb-469f-a165-70867728950e");
        [Column("e", DbType.String)] public Mood E { get; set; } = Mood.Happy;
        [Column("en", DbType.Int32)] public Mood EN { get; set; } = Mood.Sad;
        [Column("bin", DbType.Binary)] public byte[]? Bin { get; set; } = { 1, 2 };
    }

    [Table("t_time")]
    public class Temporal
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("dt", DbType.DateTime)] public DateTime Dt { get; set; } = new DateTime(2026, 10, 5, 1, 2, 3, DateTimeKind.Utc);
        [Column("dt2", DbType.DateTime2)] public DateTime Dt2 { get; set; } = new DateTime(2026, 10, 5, 1, 2, 3).AddTicks(1234567);
        [Column("dto", DbType.DateTimeOffset)] public DateTimeOffset Dto { get; set; } = new(2026, 10, 5, 1, 2, 3, TimeSpan.FromHours(-5));
        [Column("d", DbType.Date)] public DateOnly D { get; set; } = new(2026, 10, 5);
        [Column("t", DbType.Time)] public TimeOnly T { get; set; } = new(1, 2, 3);
        [Column("ts", DbType.Time)] public TimeSpan Ts { get; set; } = new(1, 2, 3);
    }

    [Table("t_adv")]
    public class Advanced
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Json] [Column("j", DbType.String)] public string[]? J { get; set; } = { "a" };
        [Column("ia", DbType.Object)] public int[]? Ia { get; set; } = { 1, 2 };
        [Column("fa", DbType.Object)] public float[]? Fa { get; set; } = { 1.5f };
        [Column("geo", DbType.Object)] public Geometry? Geo { get; set; } = Geometry.FromWellKnownText("POINT(1 2)", 4326);
        [Column("inet", DbType.Object)] public Inet? Inet { get; set; } = pengdows.crud.types.valueobjects.Inet.Parse("10.0.0.1");
        [Column("doc", DbType.Object)] public JsonDocument? Doc { get; set; } = JsonDocument.Parse("{}");
    }

    // ── Reads: Coerce(stored, target) ───────────────────────────────────────────────────

    private static IEnumerable<(object Raw, Type Target)> ReadSamples()
    {
        object[] numbers = { 1, 1L, (short)1, (byte)1, 1.0, 1.5, 1.5f, 1m, 2.7m, "1", "2.7", true, long.MaxValue, ulong.MaxValue };
        Type[] numericTargets = { typeof(int), typeof(long), typeof(short), typeof(byte), typeof(double), typeof(float), typeof(decimal), typeof(bool), typeof(ulong), typeof(int?) };
        foreach (var raw in numbers)
        foreach (var target in numericTargets)
        {
            yield return (raw, target);
        }

        foreach (var raw in new object[] { "Sad", "2", 2, 2L, "sad", "Nope", 9 })
        {
            yield return (raw, typeof(Mood));
            yield return (raw, typeof(Mood?));
        }

        var guid = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");
        foreach (var raw in new object[] { guid, guid.ToString(), guid.ToByteArray(), "{0f8fad5b-d9cb-469f-a165-70867728950e}", "nope" })
        {
            yield return (raw, typeof(Guid));
            yield return (raw, typeof(string));
        }

        var utc = new DateTime(2026, 10, 5, 13, 45, 30, DateTimeKind.Utc).AddTicks(1234567);
        object[] temporals =
        {
            utc, DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), new DateTimeOffset(utc).ToOffset(TimeSpan.FromHours(2)),
            "2026-10-05 13:45:30.1234567", "2026-10-05T13:45:30.1234567+02:00", "2026-10-05", "13:45:30.5",
            new TimeSpan(0, 13, 45, 30), new DateOnly(2026, 10, 5), new TimeOnly(13, 45, 30), 1759671930L
        };
        Type[] temporalTargets = { typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan), typeof(string) };
        foreach (var raw in temporals)
        foreach (var target in temporalTargets)
        {
            yield return (raw, target);
        }

        foreach (var raw in new object[] { "abc", new[] { 'a', 'b' }, new byte[] { 104, 105 }, 'z' })
        {
            yield return (raw, typeof(string));
            yield return (raw, typeof(byte[]));
            yield return (raw, typeof(char));
        }

        foreach (var raw in new object[] { "[1,2,3]", "{1,2,3}", new[] { 1, 2 }, new long[] { 1, 2 }, new object[] { 1, "2" } })
        {
            yield return (raw, typeof(int[]));
            yield return (raw, typeof(float[]));
            yield return (raw, typeof(string[]));
            yield return (raw, typeof(List<int>));
        }

        foreach (var raw in new object[] { "{\"a\":1}", "", "  ", new byte[] { 123, 125 }, "null" })
        {
            yield return (raw, typeof(JsonDocument));
            yield return (raw, typeof(JsonElement));
            yield return (raw, typeof(JsonValue));
        }

        var ewkb = Convert.FromHexString("0101000020E6100000000000000000F03F0000000000000040");
        var wkb = Convert.FromHexString("0101000000000000000000F03F0000000000000040");
        foreach (var raw in new object[] { "POINT(1 2)", "SRID=4326;POINT(1 2)", wkb, ewkb, "{\"type\":\"Point\",\"coordinates\":[1,2]}" })
        {
            yield return (raw, typeof(Geometry));
            yield return (raw, typeof(Geography));
        }

        foreach (var raw in new object[] { "192.168.0.1/24", "10.0.0.0/8", "08:00:2b:01:02:03", "::1" })
        {
            yield return (raw, typeof(Inet));
            yield return (raw, typeof(Cidr));
            yield return (raw, typeof(MacAddress));
        }

        foreach (var raw in new object[] { "P1Y2M", "p1y2m", "1-2", "P1DT2H3M4S", "p1dt2h3m4s", TimeSpan.FromHours(26) })
        {
            yield return (raw, typeof(IntervalYearMonth));
            yield return (raw, typeof(IntervalDaySecond));
        }

        foreach (var raw in new object[] { "[1,5)", "(,5]", "empty", "[1,x)" })
        {
            yield return (raw, typeof(Range<int>));
            yield return (raw, typeof(Range<long>));
        }

        foreach (var raw in new object[] { new byte[] { 0, 0, 0, 0, 0, 0, 0, 9 }, 9UL, new byte[] { 1, 2 } })
        {
            yield return (raw, typeof(RowVersion));
        }

        foreach (var raw in new object[] { "10110", new byte[] { 5 } })
        {
            yield return (raw, typeof(BitArray));
        }

        foreach (var raw in new object[] { "/1/2/", "170141183460469231731687303715884105727", 5L })
        {
            yield return (raw, typeof(HierarchyId));
            yield return (raw, typeof(Int128));
            yield return (raw, typeof(BigInteger));
        }
    }

    // ── Rendering ───────────────────────────────────────────────────────────────────────

    internal static string Show(object? value) => value switch
    {
        null => "null",
        DBNull => "DBNull",
        string s => "\"" + s + "\"",
        char c => "'" + c + "'",
        byte[] b => "0x" + Convert.ToHexString(b),
        float f => "float:" + f.ToString("R", CultureInfo.InvariantCulture),
        double d => "double:" + d.ToString("R", CultureInfo.InvariantCulture),
        decimal m => "decimal:" + m.ToString(CultureInfo.InvariantCulture),
        DateTime dt => "DateTime:" + dt.ToString("o", CultureInfo.InvariantCulture) + "/" + dt.Kind,
        DateTimeOffset dto => "DateTimeOffset:" + dto.ToString("o", CultureInfo.InvariantCulture),
        TimeOnly time => "TimeOnly:" + time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        DateOnly date => "DateOnly:" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        JsonDocument doc => "JsonDocument:" + doc.RootElement.GetRawText(),
        JsonElement el => "JsonElement:" + el.GetRawText(),
        SpatialValue sv => sv.GetType().Name + ":srid=" + sv.Srid + ":wkb=" + Convert.ToHexString(sv.WellKnownBinary.Span),
        BitArray bits => "BitArray:" + string.Concat(bits.Cast<bool>().Select(x => x ? '1' : '0')),
        Array array => array.GetType().Name + "{" + string.Join(",", array.Cast<object?>().Select(Show)) + "}",
        IList list => list.GetType().Name + "{" + string.Join(",", list.Cast<object?>().Select(Show)) + "}",
        IFormattable formattable => value.GetType().Name + ":" + formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.GetType().Name + ":" + value
    };

    private static string Outcome(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (Exception ex)
        {
            var inner = ex is System.Reflection.TargetInvocationException { InnerException: { } i } ? i : ex;
            return "throws " + inner.GetType().Name;
        }
    }

    private static readonly string[] BaseParameterProperties =
        typeof(DbParameter).GetProperties().Select(p => p.Name).ToArray();

    internal static string ShowParameter(DbParameter p)
    {
        var text = new StringBuilder();
        text.Append(p.DbType).Append(' ').Append(Show(p.Value));
        foreach (var property in p.GetType().GetProperties()
                     .Where(x => !BaseParameterProperties.Contains(x.Name) && x.CanRead && x.GetIndexParameters().Length == 0)
                     .OrderBy(x => x.Name))
        {
            var v = property.GetValue(p);
            if (v != null && !(v is Enum e && Convert.ToInt64(e, CultureInfo.InvariantCulture) == 0) && !(v is string s && s.Length == 0))
            {
                text.Append(' ').Append(property.Name).Append('=').Append(v);
            }
        }

        return text.ToString();
    }

    private static fakeDbFactory FactoryFor(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        switch (product)
        {
            case SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb
                or SupportedDatabase.AuroraPostgreSql or SupportedDatabase.Spanner:
                factory.EmulatesNpgsqlParameterMetadata = true;
                break;
            case SupportedDatabase.InterBase:
                factory.EmulatesInterBaseParameterMetadata = true;
                break;
            case SupportedDatabase.Informix:
                factory.EmulatesInformixParameterMetadata = true;
                break;
        }

        return factory;
    }

    private static IEnumerable<SupportedDatabase> Products() =>
        Enum.GetValues<SupportedDatabase>().Where(p => p != SupportedDatabase.Unknown);

    internal static string DescribeWrites()
    {
        var text = new StringBuilder();
        foreach (var product in Products())
        {
            text.Append("== ").Append(product).AppendLine();
            var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(product, FactoryFor(product), NullLogger.Instance);
            foreach (var (label, type, value) in WriteSamples())
            {
                text.Append("  ").Append(label).Append(": ")
                    .AppendLine(Outcome(() => ShowParameter(dialect.CreateDbParameter("p", type, value))));
            }
        }

        return text.ToString();
    }

    internal static string DescribeBuilds()
    {
        var text = new StringBuilder();
        foreach (var product in Products())
        {
            text.Append("== ").Append(product).AppendLine();
            DatabaseContext? context = null;
            var created = Outcome(() =>
            {
                context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", FactoryFor(product));
                return "ok";
            });
            if (context == null)
            {
                text.Append("  context: ").AppendLine(created);
                continue;
            }

            using (context)
            {
                Build<Scalars>(text, context, new Scalars());
                Build<Temporal>(text, context, new Temporal());
                Build<Advanced>(text, context, new Advanced());
            }
        }

        return text.ToString();
    }

    private static void Build<T>(StringBuilder text, DatabaseContext context, T entity) where T : class, new()
    {
        text.Append("  ").Append(typeof(T).Name).Append(": ");
        text.AppendLine(Outcome(() =>
        {
            using var sc = new TableGateway<T, int>(context).BuildCreate(entity);
            var parameters = (IDictionary<string, DbParameter>)typeof(SqlContainer)
                .GetField("_parameters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(sc)!;
            var lines = new StringBuilder(sc.Query.ToString());
            foreach (var (name, p) in parameters.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                lines.Append("\n    ").Append(name).Append(" = ").Append(ShowParameter(p));
            }

            return lines.ToString();
        }));
    }

    internal static string DescribeReads()
    {
        // One line per (stored, target): the result on every database, grouped where they agree.
        var products = Products().ToArray();
        var dialects = products.ToDictionary(p => p,
            p => (SqlDialect)SqlDialectFactory.CreateDialectForType(p, new fakeDbFactory(p), NullLogger.Instance));
        var text = new StringBuilder();
        foreach (var (raw, target) in ReadSamples())
        {
            var results = new Dictionary<string, List<SupportedDatabase>>(StringComparer.Ordinal);
            foreach (var product in products)
            {
                var options = TypeCoercionOptions.For(dialects[product]);
                var result = Outcome(() => Show(TypeCoercionHelper.Coerce(raw, raw.GetType(), target, options)));
                if (!results.TryGetValue(result, out var list))
                {
                    results[result] = list = new List<SupportedDatabase>();
                }

                list.Add(product);
            }

            text.Append(Show(raw)).Append(" -> ").Append(target.Name);
            if (Nullable.GetUnderlyingType(target) != null)
            {
                text.Append('?');
            }

            if (results.Count == 1)
            {
                text.Append(": ").AppendLine(results.Keys.Single());
                continue;
            }

            text.AppendLine();
            foreach (var (result, list) in results.OrderByDescending(r => r.Value.Count))
            {
                text.Append("    ").Append(result).Append("  [").Append(string.Join(",", list)).AppendLine("]");
            }
        }

        return text.ToString();
    }

    private static string PinnedPath(string name, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, name);

    private static void AssertPinned(string name, string actual)
    {
        var path = PinnedPath(name);
        if (Environment.GetEnvironmentVariable("PIN_TYPES") == "1")
        {
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"{name} is missing; run with PIN_TYPES=1 to create it.");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        var lines = actual.ReplaceLineEndings("\n").Split('\n');
        var pinned = expected.Split('\n');
        for (var i = 0; i < Math.Max(lines.Length, pinned.Length); i++)
        {
            var a = i < lines.Length ? lines[i] : "<end>";
            var e = i < pinned.Length ? pinned[i] : "<end>";
            Assert.True(a == e, $"{name} line {i + 1} changed:\n  pinned: {e}\n  now:    {a}");
        }
    }

    // A value is never bound as NULL: a dialect that remapped a DbType it can't bind sent anything but
    // the one CLR type it expected as DBNull (a DateTime declared DateTimeOffset on Db2, Informix and
    // Access). Refusing loudly is fine; storing NULL is not.
    [Fact]
    public void Writes_NeverBindAValueAsNull()
    {
        var nulled = new List<string>();
        foreach (var product in Products())
        {
            var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(product, FactoryFor(product), NullLogger.Instance);
            foreach (var (label, type, value) in WriteSamples().Where(s => s.Value is not null and not DBNull))
            {
                DbParameter parameter;
                try
                {
                    parameter = dialect.CreateDbParameter("p", type, value);
                }
                catch (Exception)
                {
                    continue;
                }

                if (parameter.Value is null or DBNull)
                {
                    nulled.Add($"{product}: {label}");
                }
            }
        }

        Assert.True(nulled.Count == 0, string.Join(Environment.NewLine, nulled));
    }

    // A DateTime (Unspecified is UTC) or timestamp text declared DateTimeOffset binds exactly as the
    // DateTimeOffset of its instant does, on every database.
    [Fact]
    public void Writes_DeclaredDateTimeOffset_BindsAsItsInstant()
    {
        var at = new DateTime(2026, 10, 5, 13, 45, 30).AddTicks(1234567);
        var utc = new DateTimeOffset(at, TimeSpan.Zero);
        var cases = new (object Value, DateTimeOffset Instant)[]
        {
            (DateTime.SpecifyKind(at, DateTimeKind.Utc), utc),
            (at, utc),
            ("2026-10-05 13:45:30.1234567", utc),
            ("2026-10-05T15:45:30.1234567+02:00", utc.ToOffset(TimeSpan.FromHours(2)))
        };
        var drifted = new List<string>();
        foreach (var product in Products())
        {
            var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(product, FactoryFor(product), NullLogger.Instance);
            foreach (var (value, instant) in cases)
            {
                var expected = ShowParameter(dialect.CreateDbParameter("p", DbType.DateTimeOffset, instant));
                var actual = ShowParameter(dialect.CreateDbParameter("p", DbType.DateTimeOffset, value));
                if (actual != expected)
                {
                    drifted.Add($"{product}: {value} bound {actual}, its instant {expected}");
                }
            }
        }

        Assert.True(drifted.Count == 0, string.Join(Environment.NewLine, drifted));
    }

    [Fact]
    public void Writes_MatchThePinnedParameters() => AssertPinned("TypeSystemWrites.txt", DescribeWrites());

    [Fact]
    public void Builds_MatchThePinnedSql() => AssertPinned("TypeSystemBuilds.txt", DescribeBuilds());

    [Fact]
    public void Reads_MatchThePinnedResults() => AssertPinned("TypeSystemReads.txt", DescribeReads());
}
