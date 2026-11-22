#if PENGDOWS_FLATFILE
using System.Data;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.flatfile;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Every type the pengdows.flatfile engine has (0.2.1-preview.27, the newest on nuget.org), found by asking the engine and not from a
/// candidate list. Its ClrTypeMap supports 21 CLR types (its README says 19: TimeSpan and BigInteger are missing there). SQL DDL
/// reaches 15 of them through 45 accepted spellings; byte, sbyte, ushort, uint, ulong and BigInteger have no SQL spelling and come
/// only from a table definition (*.table.json). Each is round-tripped through the gateway with its edge values and NULL, and the common
/// spellings it refuses are pinned.
/// </summary>
[Collection("IntegrationTests")]
public sealed class FlatFileEngineTypeTests : DatabaseTestBase
{
    public FlatFileEngineTypeTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() => [SupportedDatabase.FlatFile];

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) => Task.CompletedTask;

    // ---- a database of its own per test ------------------------------------------------------------------------------------------

    private static (DatabaseContext Context, string Directory) NewDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pengdows.flatfile.types.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var context = new DatabaseContext(
            new DatabaseContextConfiguration { ConnectionString = $"Path={directory}" },
            FlatFileProviderFactory.Instance, null, new TypeMapRegistry());
        return (context, directory);
    }

    private static void Remove(DatabaseContext context, string directory)
    {
        context.Dispose();
        try { Directory.Delete(directory, recursive: true); } catch { /* best-effort */ }
    }

    private static async Task Exec(IDatabaseContext context, string sql)
    {
        await using var container = context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    // ---- one row type per CLR type (the column DbType is an attribute constant, so each needs its own) ---------------------------

    public interface IValueRow
    {
        int Id { get; set; }
        object? Value { get; set; }
    }

    [Table("ff_values")] public sealed class RInt16 : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Int16)] public short? V { get; set; } object? IValueRow.Value { get => V; set => V = (short?)value; } }
    [Table("ff_values")] public sealed class RInt32 : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Int32)] public int? V { get; set; } object? IValueRow.Value { get => V; set => V = (int?)value; } }
    [Table("ff_values")] public sealed class RInt64 : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Int64)] public long? V { get; set; } object? IValueRow.Value { get => V; set => V = (long?)value; } }
    [Table("ff_values")] public sealed class RByte : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Byte)] public byte? V { get; set; } object? IValueRow.Value { get => V; set => V = (byte?)value; } }
    [Table("ff_values")] public sealed class RSByte : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.SByte)] public sbyte? V { get; set; } object? IValueRow.Value { get => V; set => V = (sbyte?)value; } }
    [Table("ff_values")] public sealed class RUInt16 : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.UInt16)] public ushort? V { get; set; } object? IValueRow.Value { get => V; set => V = (ushort?)value; } }
    [Table("ff_values")] public sealed class RUInt32 : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.UInt32)] public uint? V { get; set; } object? IValueRow.Value { get => V; set => V = (uint?)value; } }
    [Table("ff_values")] public sealed class RUInt64 : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.UInt64)] public ulong? V { get; set; } object? IValueRow.Value { get => V; set => V = (ulong?)value; } }
    [Table("ff_values")] public sealed class RSingle : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Single)] public float? V { get; set; } object? IValueRow.Value { get => V; set => V = (float?)value; } }
    [Table("ff_values")] public sealed class RDouble : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Double)] public double? V { get; set; } object? IValueRow.Value { get => V; set => V = (double?)value; } }
    [Table("ff_values")] public sealed class RDecimal : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Decimal)] public decimal? V { get; set; } object? IValueRow.Value { get => V; set => V = (decimal?)value; } }
    [Table("ff_values")] public sealed class RString : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.String)] public string? V { get; set; } object? IValueRow.Value { get => V; set => V = (string?)value; } }
    [Table("ff_values")] public sealed class RBoolean : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Boolean)] public bool? V { get; set; } object? IValueRow.Value { get => V; set => V = (bool?)value; } }
    [Table("ff_values")] public sealed class RBinary : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Binary)] public byte[]? V { get; set; } object? IValueRow.Value { get => V; set => V = (byte[]?)value; } }
    [Table("ff_values")] public sealed class RDateOnly : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Date)] public DateOnly? V { get; set; } object? IValueRow.Value { get => V; set => V = (DateOnly?)value; } }
    [Table("ff_values")] public sealed class RTimeOnly : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Time)] public TimeOnly? V { get; set; } object? IValueRow.Value { get => V; set => V = (TimeOnly?)value; } }
    [Table("ff_values")] public sealed class RDateTime : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.DateTime2)] public DateTime? V { get; set; } object? IValueRow.Value { get => V; set => V = (DateTime?)value; } }
    [Table("ff_values")] public sealed class RDateTimeOffset : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.DateTimeOffset)] public DateTimeOffset? V { get; set; } object? IValueRow.Value { get => V; set => V = (DateTimeOffset?)value; } }
    [Table("ff_values")] public sealed class RTimeSpan : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Time)] public TimeSpan? V { get; set; } object? IValueRow.Value { get => V; set => V = (TimeSpan?)value; } }
    [Table("ff_values")] public sealed class RGuid : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Guid)] public Guid? V { get; set; } object? IValueRow.Value { get => V; set => V = (Guid?)value; } }
    [Table("ff_values")] public sealed class RBigInteger : IValueRow { [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; } [Column("v", DbType.Object)] public BigInteger? V { get; set; } object? IValueRow.Value { get => V; set => V = (BigInteger?)value; } }

    // ---- sample values: the edges of each type, and NULL ---------------------------------------------------------------------------

    private static readonly Dictionary<Type, object?[]> Samples = new()
    {
        [typeof(short)] = [short.MinValue, (short)-1, (short)0, (short)1, short.MaxValue, null],
        [typeof(int)] = [int.MinValue, -1, 0, 1, int.MaxValue, null],
        [typeof(long)] = [long.MinValue, -1L, 0L, 1L, long.MaxValue, null],
        [typeof(byte)] = [byte.MinValue, (byte)1, (byte)127, byte.MaxValue, null],
        [typeof(sbyte)] = [sbyte.MinValue, (sbyte)-1, (sbyte)0, sbyte.MaxValue, null],
        [typeof(ushort)] = [ushort.MinValue, (ushort)1, ushort.MaxValue, null],
        [typeof(uint)] = [uint.MinValue, 1u, uint.MaxValue, null],
        [typeof(ulong)] = [ulong.MinValue, 1ul, ulong.MaxValue, null],
        [typeof(float)] = [0f, 1.5f, -2.25f, 0.1f, float.MaxValue, float.MinValue, float.Epsilon, null],
        [typeof(double)] = [0d, 1.5d, -2.25d, 0.1d, double.MaxValue, double.MinValue, double.Epsilon, null],
        [typeof(decimal)] = [0m, 1.5m, -1.5m, 1.10m, 0.0000000000000000000000000001m, decimal.MaxValue, decimal.MinValue, null],
        [typeof(string)] =
        [
            "plain", "a,b", "quote \" inside", "line1\nline2", "crlf\r\nline", "  padded  ", "tab\there", "unicode \u00e9 \u2603 \U0001F600",
            new string('x', 10_000), null
        ],
        [typeof(bool)] = [true, false, null],
        [typeof(byte[])] = [new byte[] { 0, 1, 2, 127, 128, 255 }, Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray(), null],
        [typeof(DateOnly)] = [DateOnly.MinValue, new DateOnly(2026, 11, 21), DateOnly.MaxValue, null],
        [typeof(TimeOnly)] = [TimeOnly.MinValue, new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1234567)), TimeOnly.MaxValue, null],
        [typeof(DateTime)] =
        [
            DateTime.MinValue, new DateTime(2026, 11, 21, 13, 45, 30).AddTicks(1234567), new DateTime(9999, 12, 31, 23, 59, 59).AddTicks(9999999), null
        ],
        [typeof(DateTimeOffset)] =
        [
            new DateTimeOffset(2026, 11, 21, 13, 45, 30, TimeSpan.Zero), new DateTimeOffset(2026, 11, 21, 13, 45, 30, TimeSpan.FromHours(5.5)),
            new DateTimeOffset(2026, 11, 21, 13, 45, 30, TimeSpan.FromHours(-8)).AddTicks(1234567), null
        ],
        [typeof(TimeSpan)] =
        [
            TimeSpan.Zero, new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(5)), TimeSpan.FromHours(-26.5), TimeSpan.FromDays(10000), null
        ],
        [typeof(Guid)] = [Guid.Empty, Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), Guid.NewGuid(), null],
        [typeof(BigInteger)] =
        [
            BigInteger.Zero, BigInteger.One, BigInteger.MinusOne, BigInteger.Pow(10, 40), -BigInteger.Pow(7, 50), null
        ],
    };

    // A declaration with a precision, scale or length is a constraint the engine enforces: DECIMAL(p,s) rejects what does not fit and
    // rounds to its scale; CHAR(n) pads to n. So those get values that fit, and the padding is the expected read.
    private static object?[] ValuesFor(Type clr, string? spelling)
    {
        if (spelling is not null && clr == typeof(decimal) && spelling.Contains('('))
        {
            return [0m, 1.5m, -1.5m, 1.1m, 12345.6789m, -12345.6789m, null];
        }

        if (spelling is not null && clr == typeof(string) && spelling.StartsWith("CHAR(", StringComparison.Ordinal))
        {
            return ["plain".PadRight(10), "a,b".PadRight(10), "tab	here".PadRight(10), "0123456789", null];
        }

        return Samples[clr];
    }

    // The comparison: bytes by content, a DateTimeOffset by instant (its offset is reported on its own), the rest by value.
    private static bool Same(object? expected, object? actual) => (expected, actual) switch
    {
        (null, null) => true,
        (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
        (DateTimeOffset a, DateTimeOffset b) => a.UtcTicks == b.UtcTicks,
        (DateTime a, DateTime b) => a.Ticks == b.Ticks,
        _ => Equals(expected, actual),
    };

    private static string Show(object? value) => value switch
    {
        null => "NULL",
        byte[] bytes => $"byte[{bytes.Length}]",
        string text when text.Length > 40 => $"string[{text.Length}]",
        string text => "\"" + text.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"",
        DateTimeOffset dto => dto.ToString("o"),
        DateTime dt => dt.ToString("o"),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "?",
    };

    /// <summary>Writes each sample as a row and reads it back; returns one line per value that did not come back the same.</summary>
    private static async Task<List<string>> RoundTripAsync<TRow>(IDatabaseContext context, object?[] values, string label)
        where TRow : class, IValueRow, new()
    {
        var failures = new List<string>();
        var gateway = new TableGateway<TRow, int>(context);
        for (var i = 0; i < values.Length; i++)
        {
            var id = i + 1;
            try
            {
                var row = new TRow { Id = id };
                row.Value = values[i];
                await gateway.CreateAsync(row, context);
                var actual = await gateway.RetrieveOneAsync(id, context);
                if (actual is null)
                {
                    failures.Add($"{label}: {Show(values[i])} was not found after the insert");
                }
                else if (!Same(values[i], actual.Value))
                {
                    failures.Add($"{label}: wrote {Show(values[i])}, read {Show(actual.Value)}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{label}: {Show(values[i])} -> {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        return failures;
    }

    private static Task<List<string>> RoundTripByClrTypeAsync(IDatabaseContext context, Type clr, string label, string? spelling = null)
    {
        var values = ValuesFor(clr, spelling);
        return clr switch
        {
            _ when clr == typeof(short) => RoundTripAsync<RInt16>(context, values, label),
            _ when clr == typeof(int) => RoundTripAsync<RInt32>(context, values, label),
            _ when clr == typeof(long) => RoundTripAsync<RInt64>(context, values, label),
            _ when clr == typeof(byte) => RoundTripAsync<RByte>(context, values, label),
            _ when clr == typeof(sbyte) => RoundTripAsync<RSByte>(context, values, label),
            _ when clr == typeof(ushort) => RoundTripAsync<RUInt16>(context, values, label),
            _ when clr == typeof(uint) => RoundTripAsync<RUInt32>(context, values, label),
            _ when clr == typeof(ulong) => RoundTripAsync<RUInt64>(context, values, label),
            _ when clr == typeof(float) => RoundTripAsync<RSingle>(context, values, label),
            _ when clr == typeof(double) => RoundTripAsync<RDouble>(context, values, label),
            _ when clr == typeof(decimal) => RoundTripAsync<RDecimal>(context, values, label),
            _ when clr == typeof(string) => RoundTripAsync<RString>(context, values, label),
            _ when clr == typeof(bool) => RoundTripAsync<RBoolean>(context, values, label),
            _ when clr == typeof(byte[]) => RoundTripAsync<RBinary>(context, values, label),
            _ when clr == typeof(DateOnly) => RoundTripAsync<RDateOnly>(context, values, label),
            _ when clr == typeof(TimeOnly) => RoundTripAsync<RTimeOnly>(context, values, label),
            _ when clr == typeof(DateTime) => RoundTripAsync<RDateTime>(context, values, label),
            _ when clr == typeof(DateTimeOffset) => RoundTripAsync<RDateTimeOffset>(context, values, label),
            _ when clr == typeof(TimeSpan) => RoundTripAsync<RTimeSpan>(context, values, label),
            _ when clr == typeof(Guid) => RoundTripAsync<RGuid>(context, values, label),
            _ when clr == typeof(BigInteger) => RoundTripAsync<RBigInteger>(context, values, label),
            _ => throw new NotSupportedException(clr.Name),
        };
    }

    // ---- 1. every SQL spelling the engine accepts (found live, preview.27) ----------------------------------------------------------

    private static readonly (string Spelling, Type Clr)[] AcceptedSpellings =
    [
        ("SMALLINT", typeof(short)), ("INT", typeof(int)), ("INTEGER", typeof(int)), ("BIGINT", typeof(long)),
        ("DEC", typeof(decimal)), ("DECIMAL", typeof(decimal)), ("NUMERIC", typeof(decimal)), ("DECIMAL(18,4)", typeof(decimal)), ("NUMERIC(38,10)", typeof(decimal)),
        ("REAL", typeof(float)), ("FLOAT", typeof(float)), ("FLOAT(53)", typeof(float)), ("DOUBLE", typeof(double)), ("DOUBLE PRECISION", typeof(double)),
        ("BOOLEAN", typeof(bool)),
        ("CHAR", typeof(string)), ("CHARACTER", typeof(string)), ("CHAR(10)", typeof(string)), ("VARCHAR", typeof(string)), ("VARCHAR(100)", typeof(string)),
        ("CHARACTER VARYING", typeof(string)), ("CHAR VARYING", typeof(string)), ("NCHAR", typeof(string)), ("NATIONAL CHARACTER", typeof(string)),
        ("NATIONAL CHAR", typeof(string)), ("NATIONAL CHARACTER VARYING", typeof(string)), ("CLOB", typeof(string)), ("JSON", typeof(string)),
        ("INTERVAL YEAR TO MONTH", typeof(string)),
        ("BINARY", typeof(byte[])), ("BINARY(16)", typeof(byte[])), ("VARBINARY", typeof(byte[])), ("VARBINARY(255)", typeof(byte[])), ("BLOB", typeof(byte[])),
        ("DATE", typeof(DateOnly)),
        ("TIME", typeof(TimeOnly)), ("TIME(3)", typeof(TimeOnly)), ("TIME WITHOUT TIME ZONE", typeof(TimeOnly)),
        ("TIMESTAMP", typeof(DateTime)), ("TIMESTAMP(6)", typeof(DateTime)), ("TIMESTAMP WITHOUT TIME ZONE", typeof(DateTime)),
        ("TIMESTAMP WITH TIME ZONE", typeof(DateTimeOffset)), ("TIME WITH TIME ZONE", typeof(DateTimeOffset)),
        ("INTERVAL", typeof(TimeSpan)),
        ("UUID", typeof(Guid)),
    ];

    [SkippableFact]
    public async Task EverySqlSpellingTheEngineAccepts_RoundTripsItsEdgeValuesThroughTheGateway()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.FlatFile, async _ =>
        {
            var (context, directory) = NewDatabase();
            try
            {
                var failures = new List<string>();
                foreach (var (spelling, clr) in AcceptedSpellings)
                {
                    await Exec(context, "DROP TABLE IF EXISTS ff_values");
                    await Exec(context, $"CREATE TABLE ff_values (id INTEGER PRIMARY KEY, v {spelling})");
                    failures.AddRange(await RoundTripByClrTypeAsync(context, clr, $"{spelling} ({clr.Name})", spelling));
                }

                Assert.True(failures.Count == 0, $"{failures.Count} values did not round-trip:\n" + string.Join("\n", failures));
            }
            finally
            {
                Remove(context, directory);
            }
        });
    }

    // ---- 2. every CLR type the engine supports, natively, through a table definition ------------------------------------------------

    [SkippableFact]
    public async Task EveryClrTypeInTheEnginesTypeMap_RoundTripsNatively_ThroughATableDefinition()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.FlatFile, async _ =>
        {
            var (context, directory) = NewDatabase();
            try
            {
                var supported = Samples.Keys.Where(ClrTypeMap.IsSupported).ToList();
                // The map's own list is the contract: every CLR type it supports has a sample here, and every sample is supported.
                Assert.Equal(21, supported.Count);
                Assert.Equal(Samples.Count, supported.Count);

                var failures = new List<string>();
                foreach (var clr in supported)
                {
                    var token = ClrTypeMap.ToToken(clr);
                    // The table the engine itself writes for a text column, with the column's type changed to this one.
                    await Exec(context, "DROP TABLE IF EXISTS ff_values");
                    await Exec(context, "CREATE TABLE ff_values (id INTEGER PRIMARY KEY, v VARCHAR)");
                    var sidecar = Path.Combine(directory, "ff_values.table.json");
                    var json = JsonNode.Parse(File.ReadAllText(sidecar))!;
                    var column = json["Columns"]![1]!;
                    column["ClrType"] = token;
                    column["DeclaredTypeName"] = token.ToUpperInvariant();
                    column["DbType"] = ClrTypeMap.ToDbType(clr).ToString();
                    File.WriteAllText(sidecar, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    File.WriteAllText(Path.Combine(directory, "ff_values.csv"), "id,v\r\n");

                    failures.AddRange(await RoundTripByClrTypeAsync(context, clr, $"{token} ({clr.Name})"));
                }

                Assert.True(failures.Count == 0, $"{failures.Count} values did not round-trip:\n" + string.Join("\n", failures));
            }
            finally
            {
                Remove(context, directory);
            }
        });
    }

    // ---- 3. spellings it does not have ------------------------------------------------------------------------------------------------

    private static readonly string[] RefusedSpellings =
    [
        "TINYINT", "BYTE", "SHORT", "LONG", "INT2", "INT4", "INT8", "FLOAT4", "FLOAT8", "BOOL", "TEXT", "STRING", "NVARCHAR", "BYTEA", "GUID",
        "DATETIME", "TIMESTAMPTZ", "MONEY", "BIT", "YEAR", "SERIAL", "MEDIUMINT", "SMALLMONEY", "NTEXT", "IMAGE", "XML", "UNSIGNED",
    ];

    [SkippableFact]
    public async Task SpellingsTheEngineDoesNotHave_AreRefused()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.FlatFile, async _ =>
        {
            var (context, directory) = NewDatabase();
            try
            {
                var accepted = new List<string>();
                var untyped = new List<string>();
                foreach (var spelling in RefusedSpellings)
                {
                    try
                    {
                        await Exec(context, $"CREATE TABLE ff_refused (id INTEGER PRIMARY KEY, v {spelling})");
                        accepted.Add(spelling);
                        await Exec(context, "DROP TABLE ff_refused");
                    }
                    catch (Exception ex)
                    {
                        // The engine refuses a DDL type it has no mapping for. It raises InvalidOperationException (or
                        // NotSupportedException for BIT) from its own parser, and the library does not rewrap raw DDL, so
                        // what is pinned is that the table is not created.
                        untyped.Add($"{spelling}: {ex.GetType().Name}");
                    }
                }

                Assert.True(accepted.Count == 0, "The engine accepted spellings this test pins as refused: " + string.Join(", ", accepted));
                Assert.Equal(RefusedSpellings.Length, untyped.Count);
            }
            finally
            {
                Remove(context, directory);
            }
        });
    }
}
#endif
