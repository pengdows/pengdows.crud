using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.types;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// DRY-010 stage 4: the type system is complete. Every CLR type the library reads or writes (the
/// scalars and every type a converter or coercion is registered for) has a sample, and on every
/// database what the dialect binds for it reads back through that database's coercion options as
/// the same value, or the database is listed in <see cref="Known"/> with the reason it can't. The
/// outcome is pinned as docs/type-support-matrix.md (PIN_TYPES=1 regenerates it).
/// </summary>
public class TypeCompletenessTests
{
    public enum Mood
    {
        Sad = 1,
        Happy = 2
    }

    private sealed record Sample(string Label, Type Clr, DbType DbType, Func<object> Value);

    // POINT(1 2), little-endian WKB.
    private static readonly byte[] PointWkb = Convert.FromHexString("0101000000000000000000F03F0000000000000040");

    private static readonly DateTime At = new DateTime(2026, 10, 5, 13, 45, 30, DateTimeKind.Utc).AddTicks(1234567);

    private static IEnumerable<Sample> Samples()
    {
        Sample S<T>(string label, DbType type, Func<T> value) where T : notnull => new(label, typeof(T), type, () => value());

        yield return S("bool", DbType.Boolean, () => true);
        yield return S("byte", DbType.Byte, () => (byte)200);
        yield return S("sbyte", DbType.SByte, () => (sbyte)-5);
        yield return S("short", DbType.Int16, () => (short)-300);
        yield return S("ushort", DbType.UInt16, () => (ushort)60000);
        yield return S("int", DbType.Int32, () => -42);
        yield return S("uint", DbType.UInt32, () => 4000000000u);
        yield return S("long", DbType.Int64, () => long.MinValue);
        yield return S("ulong", DbType.UInt64, () => ulong.MaxValue);
        yield return S("float", DbType.Single, () => 1.5f);
        yield return S("double", DbType.Double, () => 0.1);
        yield return S("decimal", DbType.Decimal, () => 123.4500m);
        yield return S("Int128", DbType.Object, () => (Int128)long.MaxValue * 4);
        yield return S("BigInteger", DbType.Object, () => BigInteger.Parse("123456789012345678901234567890"));
        yield return S("char", DbType.String, () => 'x');
        yield return S("char[]", DbType.String, () => "abc".ToCharArray());
        yield return S("string", DbType.String, () => "héllo");
        yield return S("Guid", DbType.Guid, () => new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"));
        yield return S("enum as String", DbType.String, () => Mood.Happy);
        yield return S("enum as Int32", DbType.Int32, () => Mood.Happy);
        yield return S("DateTime", DbType.DateTime2, () => At);
        yield return S("DateTimeOffset", DbType.DateTimeOffset, () => new DateTimeOffset(At).ToOffset(TimeSpan.FromHours(2)));
        yield return S("DateOnly", DbType.Date, () => new DateOnly(2026, 10, 5));
        yield return S("TimeOnly", DbType.Time, () => new TimeOnly(13, 45, 30).Add(TimeSpan.FromTicks(1234567)));
        yield return S("TimeSpan", DbType.Time, () => new TimeSpan(0, 13, 45, 30).Add(TimeSpan.FromTicks(1234567)));
        yield return S("byte[]", DbType.Binary, () => new byte[] { 1, 2, 255 });
        yield return S("RowVersion", DbType.Binary, () => RowVersion.FromBytes(new byte[] { 0, 0, 0, 0, 0, 0, 0, 9 }));
        yield return S<Stream>("Stream", DbType.Binary, () => new MemoryStream(new byte[] { 1, 2, 255 }));
        yield return S<TextReader>("TextReader", DbType.String, () => new StringReader("héllo"));
        yield return S("BitArray", DbType.Object, () => new BitArray(new[] { true, false, true }));
        yield return S("int[]", DbType.Object, () => new[] { 1, -2 });
        yield return S("int?[]", DbType.Object, () => new int?[] { 1, null });
        yield return S("long[]", DbType.Object, () => new[] { 1L, -2L });
        yield return S("long?[]", DbType.Object, () => new long?[] { 1, null });
        yield return S("short[]", DbType.Object, () => new short[] { 1, -2 });
        yield return S("short?[]", DbType.Object, () => new short?[] { 1, null });
        yield return S("double[]", DbType.Object, () => new[] { 0.5, 2.0 });
        yield return S("double?[]", DbType.Object, () => new double?[] { 0.5, null });
        yield return S("float[]", DbType.Object, () => new[] { 1.5f, -2f });
        yield return S("float?[]", DbType.Object, () => new float?[] { 1.5f, null });
        yield return S("string[]", DbType.Object, () => new[] { "a", "b\"c" });
        yield return S("JsonDocument", DbType.Object, () => JsonDocument.Parse("{\"a\":1}"));
        yield return S("JsonElement", DbType.Object, () => JsonDocument.Parse("[1,2]").RootElement);
        yield return S("JsonValue", DbType.Object, () => JsonValue.Parse("{\"a\":1}"));
        yield return S("HStore", DbType.Object, () => HStore.Parse("\"a\"=>\"1\", \"b\"=>NULL"));
        yield return S("HierarchyId", DbType.Object, () => HierarchyId.Parse("/1/2/"));
        yield return S("Geometry", DbType.Object, () => Geometry.FromWellKnownBinary(PointWkb, 4326));
        yield return S("Geography", DbType.Object, () => Geography.FromWellKnownBinary(PointWkb, 4326));
        yield return S("Inet", DbType.Object, () => Inet.Parse("192.168.0.1/24"));
        yield return S("Cidr", DbType.Object, () => Cidr.Parse("10.0.0.0/8"));
        yield return S("MacAddress", DbType.Object, () => MacAddress.Parse("08:00:2b:01:02:03"));
        yield return S("IntervalYearMonth", DbType.Object, () => IntervalYearMonth.Parse("P1Y2M"));
        yield return S("IntervalDaySecond", DbType.Object, () => IntervalDaySecond.Parse("P1DT2H3M4.1234567S"));
        yield return S("PostgreSqlInterval", DbType.Object, () => new PostgreSqlInterval(14, 3, 3_723_123_456));
        yield return S("Range<int>", DbType.Object, () => new Range<int>(1, 5, true, false));
        yield return S("Range<long>", DbType.Object, () => new Range<long>(1, 5, true, false));
        yield return S("Range<decimal>", DbType.Object, () => new Range<decimal>(1.25m, 99.999m, true, false));
        yield return S("Range<DateTime>", DbType.Object, () => new Range<DateTime>(At, At.AddDays(1), true, false));
        yield return S("Range<DateTimeOffset>", DbType.Object,
            () => new Range<DateTimeOffset>(new DateTimeOffset(At), new DateTimeOffset(At.AddDays(1)), true, false));
        yield return S("Range<DateOnly>", DbType.Object,
            () => new Range<DateOnly>(new DateOnly(2026, 1, 2), new DateOnly(2026, 3, 4), true, true));
    }

    private const string OffsetLost = "keeps the instant, not the offset: reads back at +00:00";
    private const string Microseconds = "holds microseconds: the 7th fraction digit is truncated, never rounded";
    private const string NoNullElements = "an Informix collection can't hold a NULL element: the write is refused";
    private const string HanaArray = "bound as JSON text the statement builds the ARRAY from (JSON_TABLE); read from the " +
                                     "ARRAY's wire encoding (HanaArrayCoercion), verified live";
    private const string SqlServerSpatial = "bound as SRID-prefixed WKB the statement builds the value from (STGeomFromWKB); " +
                                            "read from the stored encoding, verified live";
    private const string SnowflakeSpatial = "bound as EWKB hex the statement builds the value from; read in the session's " +
                                            "output format, verified live";

    // Why a database can't round-trip a type. "*" is every database not otherwise listed for the label.
    private static readonly Dictionary<(string Label, string Database), string> Known = new()
    {
        [("DateTimeOffset", "*")] = "no offset-aware type, or one that " + OffsetLost,
        [("DateTimeOffset", "SybaseASE")] = "BIGDATETIME text " + Microseconds + "; " + OffsetLost,
        [("DateTime", "SybaseASE")] = "BIGDATETIME " + Microseconds,
        [("TimeOnly", "SingleStore")] = "TIME(6) " + Microseconds,
        [("TimeSpan", "SingleStore")] = "TIME(6) " + Microseconds,
        [("IntervalDaySecond", "Oracle")] = "INTERVAL DAY TO SECOND(6) " + Microseconds,
        [("int?[]", "Informix")] = NoNullElements,
        [("long?[]", "Informix")] = NoNullElements,
        [("short?[]", "Informix")] = NoNullElements,
        [("double?[]", "Informix")] = NoNullElements,
        [("float?[]", "Informix")] = NoNullElements,
        [("int?[]", "SapHana")] = HanaArray,
        [("long?[]", "SapHana")] = HanaArray,
        [("short?[]", "SapHana")] = HanaArray,
        [("double?[]", "SapHana")] = HanaArray,
        [("float?[]", "SapHana")] = HanaArray,
        [("string[]", "SapHana")] = HanaArray,
        [("Geometry", "SqlServer")] = SqlServerSpatial,
        [("Geography", "SqlServer")] = SqlServerSpatial,
        [("Geometry", "Snowflake")] = SnowflakeSpatial,
        [("Geography", "Snowflake")] = SnowflakeSpatial,
        [("Geometry", "SingleStore")] = "written as WKT, which carries no SRID: a Geometry reads back with SRID 0 " +
                                        "(GEOGRAPHY is always 4326)"
    };

    private static IEnumerable<SupportedDatabase> Products() =>
        Enum.GetValues<SupportedDatabase>().Where(p => p != SupportedDatabase.Unknown);

    private static string Canonical(object? value) => value switch
    {
        Stream stream => "Stream:" + Convert.ToHexString(ReadAll(stream)),
        TextReader reader => "Text:\"" + reader.ReadToEnd() + "\"",
        DateTime dt => TypeSystemCharacterizationTests.Show(TypeCoercionHelper.NormalizeDateTime(dt)),
        // Every dialect sends a decimal without trailing zeros (its value, not its scale, is the data).
        decimal m => "decimal:" + m.ToString("G29", System.Globalization.CultureInfo.InvariantCulture),
        // The same shape whether held as WKB or WKT (the library's own EWKT writer decodes WKB).
        SpatialValue sv => sv.GetType().Name + ":" + Outcome(() => types.converters.ExtendedWellKnownText.From(sv)),
        _ => TypeSystemCharacterizationTests.Show(value)
    };

    private static string Outcome(Func<string> text)
    {
        try
        {
            return text();
        }
        catch (Exception ex)
        {
            return "undecodable (" + ex.GetType().Name + ")";
        }
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // What happens to a sample on a database: "ok", or what went wrong.
    private static string Outcome(Sample sample, SqlDialect dialect)
    {
        var expected = Canonical(sample.Value());
        object? bound;
        try
        {
            bound = dialect.CreateDbParameter("p", sample.DbType, sample.Value()).Value;
        }
        catch (Exception ex)
        {
            return "refused (" + ex.GetType().Name + ")";
        }

        if (bound is null or DBNull)
        {
            return "bound NULL";
        }

        object? read;
        try
        {
            read = TypeCoercionHelper.Coerce(bound, bound.GetType(), sample.Clr, TypeCoercionOptions.For(dialect));
        }
        catch (Exception ex)
        {
            return "bound " + Canonical(bound) + ", unreadable (" + ex.GetType().Name + ")";
        }

        var actual = Canonical(read);
        return actual == expected ? "ok" : "bound " + Canonical(bound) + ", reads " + actual;
    }

    private static Dictionary<string, Dictionary<SupportedDatabase, string>> Observe()
    {
        var dialects = Products().ToDictionary(p => p,
            p => (SqlDialect)SqlDialectFactory.CreateDialectForType(p, new pengdows.crud.fakeDb.fakeDbFactory(p), NullLogger.Instance));
        return Samples().ToDictionary(s => s.Label, s => Products().ToDictionary(p => p, p => Outcome(s, dialects[p])));
    }

    [Fact]
    public void EveryRegisteredType_HasASample()
    {
        var sampled = Samples().Select(s => s.Clr).ToHashSet();
        // Other tests register their own types into the shared registries; the library's are the ones
        // that matter.
        static bool FromTests(Type t) =>
            t.Assembly == typeof(TypeCompletenessTests).Assembly ||
            (t.HasElementType && FromTests(t.GetElementType()!)) ||
            (t.IsGenericType && t.GetGenericArguments().Any(FromTests));
        var registered = AdvancedTypeRegistry.Shared.ConverterTypes
            .Concat(CoercionRegistry.Shared.RegisteredTypes().Select(r => r.Type))
            .Where(t => !FromTests(t))
            .Distinct();

        var missing = registered.Where(t => !sampled.Contains(t) && !sampled.Any(s => t.IsAssignableFrom(s) && t.IsAbstract))
            .Select(t => t.ToString()).OrderBy(t => t, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, "no sample for: " + string.Join(", ", missing));
    }

    private static string? Reason(string label, SupportedDatabase product) =>
        Known.TryGetValue((label, product.ToString()), out var reason) ? reason
        : Known.TryGetValue((label, "*"), out var any) ? any
        : null;

    [Fact]
    public void EveryTypeRoundTripsOnEveryDatabase_OrIsKnownWithAReason()
    {
        var unexplained = new List<string>();
        foreach (var (label, outcomes) in Observe())
        {
            foreach (var (product, outcome) in outcomes)
            {
                var listed = Reason(label, product) != null;
                if (outcome != "ok" && !listed)
                {
                    unexplained.Add($"{label} on {product}: {outcome}");
                }

                if (outcome == "ok" && Known.ContainsKey((label, product.ToString())))
                {
                    unexplained.Add($"{label} on {product}: listed as known but round-trips");
                }
            }
        }

        Assert.True(unexplained.Count == 0, string.Join(Environment.NewLine, unexplained));
    }

    // docs/type-support-matrix.md: every type, the databases it round-trips on, and why the others don't.
    [Fact]
    public void SupportMatrix_MatchesTheDocs()
    {
        var observed = Observe();
        var text = new StringBuilder();
        text.Append("""
            # Type support matrix

            Generated by `TypeCompletenessTests` (`PIN_TYPES=1` regenerates it); do not edit by hand.

            Every CLR type pengdows.crud reads or writes, and every database it supports. A type
            **round-trips** on a database when the parameter the dialect binds for it reads back,
            through that database's coercion rules, as the same value. Where it doesn't, the reason is
            listed: a database limit, or a statement that builds the stored value from the bound one
            (checked live by the integration type matrix instead). The test fails when a type is added
            without a sample, or when a database stops round-tripping a type without a listed reason.

            DateTime values are read as UTC (`DateTimeKind.Unspecified` is UTC); a decimal's value, not
            its trailing zeros, is the data (every dialect sends it without them).

            | Type | Declared as | Round-trips on | Exceptions |
            |---|---|---|---|

            """.ReplaceLineEndings("\n"));
        var products = Products().ToList();
        foreach (var sample in Samples())
        {
            var outcomes = observed[sample.Label];
            var failing = products.Where(p => outcomes[p] != "ok").ToList();
            var ok = failing.Count == 0 ? "every database" : products.Count - failing.Count == 0 ? "none" : $"all but {failing.Count}";
            var exceptions = string.Join("<br>", failing
                .GroupBy(p => Reason(sample.Label, p) ?? outcomes[p])
                .Select(g => $"{string.Join(", ", g)}: {g.Key}"));
            text.Append("| `").Append(sample.Label.Replace("|", "\\|")).Append("` | `DbType.").Append(sample.DbType).Append("` | ")
                .Append(ok).Append(" | ").Append(exceptions).Append(" |\n");
        }

        TypeSystemCharacterizationTests.AssertPinnedFile(DocsPath(), text.ToString());
    }

    private static string DocsPath([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "docs", "type-support-matrix.md"));
}
