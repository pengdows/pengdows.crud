using System.Collections;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Type fidelity on the real database, for every fidelity type on every database. For each (type, database) cell:
/// the table is created with the declaration that database accepted for the type (TypeFidelityDeclarations, from the
/// live probe), one row is written and read back through TableGateway with the type's entity (TypeFidelityCells,
/// generated), and the value is compared with what was written.
///
/// A cell ends in one of three states. PASS: round-tripped. UNSUPPORTED: the database has no column for the type, and
/// the server's refusal is recorded in TypeFidelityDeclarations. FAIL: the database has a declaration and the round trip
/// did not return the same value, or a type the database has no declaration for has no recorded refusal (UNRECORDED).
/// Only FAIL and UNRECORDED fail the test. The matrix of every cell is written to the test output.
/// </summary>
[Collection("IntegrationTests")]
public class TypeFidelityIntegrationTests : DatabaseTestBase
{
    private const string TableName = "type_fidelity";

    public TypeFidelityIntegrationTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    [SkippableFact]
    public async Task EveryFidelityType_RoundTripsOnEveryDatabase()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var declarations = TypeFidelityDeclarations.ByDatabase.TryGetValue(provider, out var d)
                ? d
                : new Dictionary<string, (string Declaration, string ProviderType)>();
            var unsupported = TypeFidelityDeclarations.UnsupportedByDatabase.TryGetValue(provider, out var u)
                ? u
                : new Dictionary<string, string>();

            // An Oracle BFILE cell names the directory object DATA_DIR, which the application fixes: create it once per run.
            if (provider == SupportedDatabase.Oracle)
            {
                using var directory = context.CreateSqlContainer("CREATE OR REPLACE DIRECTORY DATA_DIR AS '/tmp'");
                await directory.ExecuteNonQueryAsync();

                // Reading a BFILE opens its file (ODP.NET's GetValue fails with ORA-22288 when it is absent), so the cell's
                // file exists on the server, written through UTL_FILE into the same directory.
                using var file = context.CreateSqlContainer(
                    "DECLARE f UTL_FILE.FILE_TYPE; BEGIN f := UTL_FILE.FOPEN('DATA_DIR', 'file.bin', 'W'); " +
                    "UTL_FILE.PUT_LINE(f, 'x'); UTL_FILE.FCLOSE(f); END;");
                await file.ExecuteNonQueryAsync();
            }

            var matrix = new StringBuilder();
            var problems = new List<string>();
            var counts = new Dictionary<string, int>();
            foreach (var (semantic, column, sharedRowType) in TypeFidelityCells.All)
            {
                // A column that declares a native type (its own row class per database) is written with that type.
                var rowType = TypeFidelityCells.NativeRowFor(provider, semantic) ?? sharedRowType;
                string status;
                if (declarations.TryGetValue(semantic, out var declared))
                {
                    // A named type or domain the declaration uses is created first (TypeFidelityDeclarations.Prerequisites).
                    if (TypeFidelityDeclarations.Prerequisites.TryGetValue(provider, out var prerequisites) &&
                        prerequisites.TryGetValue(semantic, out var ddl))
                    {
                        await DropTableIfExistsAsync(context, TableName);
                        for (var i = 0; i < ddl.Length; i++)
                        {
                            try
                            {
                                await using var prerequisite = context.CreateSqlContainer(ddl[i]);
                                await prerequisite.ExecuteNonQueryAsync();
                            }
                            catch (Exception) when (i < ddl.Length - 1)
                            {
                                // A clean-up that fails (the type or domain does not exist yet) is not an error.
                            }
                        }
                    }

                    // A column the server generates is never written by the library: the row is inserted without it, and the
                    // value the server generated is read back through the library's row type.
                    var cell = TypeFidelityIntegrationTests.ServerGenerated(provider, semantic)
                        ? nameof(GeneratedCellAsync)
                        : nameof(RoundTripCellAsync);
                    var method = typeof(TypeFidelityIntegrationTests)
                        .GetMethod(cell, BindingFlags.NonPublic | BindingFlags.Static)!
                        .MakeGenericMethod(rowType);
                    // A TIME WITH TIME ZONE keeps no date, so its value is compared by its UTC time of day.
                    Func<object?, string>? canonical = semantic == "time with zone" ? ZonedTimeOfDay
                        : semantic == "geography / geodetic" && provider == SupportedDatabase.SingleStore ? SixPlaceCoordinates
                        : provider == SupportedDatabase.Snowflake && SnowflakeSpatialSemantics.Contains(semantic) ? WithoutSpaces
                        : null;
                    // A server-generated cell takes three arguments; only the round-trip cell takes the comparison.
                    var arguments = cell == nameof(GeneratedCellAsync)
                        ? new object?[] { context, declared.Declaration, column }
                        : new object?[] { context, declared.Declaration, column, canonical };
                    status = await (Task<string>)method.Invoke(null, arguments)!;
                }
                else if (unsupported.TryGetValue(semantic, out var reason))
                {
                    status = "UNSUPPORTED " + reason;
                }
                else
                {
                    status = "UNRECORDED no declaration and no recorded refusal";
                }

                var kind = status.Split(' ', 2)[0];
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
                matrix.AppendLine($"{provider}\t{semantic}\t{column}\t{status}");
                if (kind is "FAIL" or "UNRECORDED")
                {
                    problems.Add($"{semantic} ({column}): {status}");
                }
            }

            Output.WriteLine(matrix.ToString());
            // The matrix is kept as a file too: the test output is not in the console log of a failing run.
            var dir = Path.Combine(AppContext.BaseDirectory, "fidelity-matrix");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, provider + ".tsv"), matrix.ToString());
            Output.WriteLine($"[{provider}] " + string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")));
            Assert.True(problems.Count == 0,
                $"[{provider}] {problems.Count} fidelity cells failed:\n" + string.Join("\n", problems));
        });
    }

    private static bool ServerGenerated(SupportedDatabase provider, string semantic) =>
        TypeFidelityDeclarations.ServerGenerated.Contains((provider, semantic));

    // One server-generated cell: the table is created with the declaration, the row is inserted without that column, and the
    // value the server generated is read back through the row type. PASS when the library reads a generated value.
    private static async Task<string> GeneratedCellAsync<TRow>(IDatabaseContext context, string declaration, string column)
        where TRow : class, IFidelityRow<TRow>, new()
    {
        try
        {
            await DropTableIfExistsAsync(context, TableName);
            await using (var create = context.CreateSqlContainer(
                             CreateTableSql(context, $"{context.WrapObjectName("id")} INTEGER NOT NULL PRIMARY KEY, " +
                                                    $"{context.WrapObjectName(column)} {declaration} NOT NULL")))
            {
                await create.ExecuteNonQueryAsync();
            }

            await using (var insert = context.CreateSqlContainer(
                             $"INSERT INTO {IntegrationObjectNameHelper.Table(context, TableName)} ({context.WrapObjectName("id")}) VALUES (1)"))
            {
                await insert.ExecuteNonQueryAsync();
            }

            var gateway = new TableGateway<TRow, int>(context);
            var actual = await gateway.RetrieveOneAsync(1, context);
            if (actual == null)
            {
                return "FAIL row not found after insert";
            }

            var value = typeof(TRow).GetProperty("Value")!.GetValue(actual);
            return value is null
                ? "FAIL the server generated no value"
                : "PASS generated " + Canonical(value);
        }
        catch (Exception ex)
        {
            // The cause is the inner exception for a wrapper (TemplateInitializationException, DataMappingException): name it too.
            var cause = ex.InnerException is { } inner ? " <- " + inner.GetType().Name + ": " + inner.Message : string.Empty;
            var message = (ex.Message + cause).ReplaceLineEndings(" ");
            return "FAIL " + ex.GetType().Name + ": " + (message.Length > 360 ? message[..360] : message);
        }
    }

    // The CREATE TABLE for a cell's columns: the database's template where it has one (a tablespace, a rowstore table).
    private static string CreateTableSql(IDatabaseContext context, string columns)
    {
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        return TypeFidelityDeclarations.TableTemplates.TryGetValue(context.Dialect.DatabaseType, out var template)
            ? template.Replace("{table}", table).Replace("{columns}", columns)
            : $"CREATE TABLE {table} ({columns})";
    }

    // One cell: create the table with the declaration, write one row, read it back, compare the value.
    private static async Task<string> RoundTripCellAsync<TRow>(IDatabaseContext context, string declaration, string column,
        Func<object?, string>? canonical = null)
        where TRow : class, IFidelityRow<TRow>, new()
    {
        try
        {
            await DropTableIfExistsAsync(context, TableName);
            await using (var create = context.CreateSqlContainer(
                             CreateTableSql(context, $"{context.WrapObjectName("id")} INTEGER NOT NULL PRIMARY KEY, " +
                                                    $"{context.WrapObjectName(column)} {declaration} NOT NULL")))
            {
                await create.ExecuteNonQueryAsync();
            }

            var gateway = new TableGateway<TRow, int>(context);
            var expected = TRow.Expected();
            if (!await gateway.CreateAsync(expected, context))
            {
                return "FAIL insert reported no row written";
            }

            var actual = await gateway.RetrieveOneAsync(expected.Id, context);
            if (actual == null)
            {
                return "FAIL row not found after insert";
            }

            var compare = canonical ?? Canonical;
            var wrote = compare(typeof(TRow).GetProperty("Value")!.GetValue(expected));
            var read = compare(typeof(TRow).GetProperty("Value")!.GetValue(actual));
            var readValue = typeof(TRow).GetProperty("Value")!.GetValue(actual);
            return wrote == read
                ? "PASS"
                : $"FAIL wrote {wrote}, read {read} (read as {readValue?.GetType().Name ?? "null"})";
        }
        catch (Exception ex)
        {
            // The cause is the inner exception for a wrapper (TemplateInitializationException, DataMappingException): name it too.
            var cause = ex.InnerException is { } inner ? " <- " + inner.GetType().Name + ": " + inner.Message : string.Empty;
            var message = (ex.Message + cause).ReplaceLineEndings(" ");
            return "FAIL " + ex.GetType().Name + ": " + (message.Length > 360 ? message[..360] : message);
        }
    }

    // The value a column holds, in a form two values compare equal in when the database kept them the same:
    // an instant for a date-time, the numeric value for a decimal, the bytes for a binary value.
    // The coordinates of a WKT compare as numbers: "1" and "1.0" are the same ordinate, and a server prints either.
    // A TIME WITH TIME ZONE holds a time of day and an offset, not a date (a read may return any date): the value is its
    // local time of day and its offset. A lost offset or a shifted time of day fails the comparison.
    // Snowflake writes WKT with a space before each parenthesis and after each comma, and reads it back without them
    // ("POINT (1 2)" and "POINT(1 2)", live 2026-11-11). The coordinates are the same text, so only the spacing is compared away.
    private static readonly HashSet<string> SnowflakeSpatialSemantics = new()
    {
        "geography / geodetic", "general geometry", "point geometry", "line / linestring geometry", "polygon geometry",
    };

    private static string WithoutSpaces(object? value) => Canonical(value).Replace(" ", string.Empty, StringComparison.Ordinal);

    // SingleStore keeps a geography's coordinates in single precision (about seven significant digits): 1 reads back as
    // 0.99999998. The comparison is at six decimal places for that database only, so every other database is compared exactly.
    private static string SixPlaceCoordinates(object? value) =>
        Regex.Replace(Canonical(value).Replace("POINT (", "POINT(").Replace("POLYGON (", "POLYGON("), @"-?\d+(\.\d+)?",
            m => Math.Round(double.Parse(m.Value, CultureInfo.InvariantCulture), 6).ToString("R", CultureInfo.InvariantCulture));

    private static string ZonedTimeOfDay(object? value) =>
        value is DateTimeOffset zoned
            ? "zoned-time:" + zoned.TimeOfDay.Ticks + "@" + zoned.Offset.Ticks
            : Canonical(value);

    private static string NormalizeNumbers(string wkt) =>
        Regex.Replace(wkt, @"[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?", match =>
            double.Parse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture)
                .ToString("R", CultureInfo.InvariantCulture));

    private static string Canonical(object? value) => value switch
    {
        null or DBNull => "null",
        string text => "text:" + text,
        DateTime dt => "instant:" + (dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : DateTime.SpecifyKind(dt, DateTimeKind.Utc)).Ticks,
        DateTimeOffset dto => "instant:" + dto.UtcDateTime.Ticks,
        decimal d => "decimal:" + d.ToString("0.##############################", CultureInfo.InvariantCulture),
        double d => "double:" + d.ToString("R", CultureInfo.InvariantCulture),
        float f => "float:" + f.ToString("R", CultureInfo.InvariantCulture),
        byte[] bytes => "bytes:" + Convert.ToHexString(bytes),
        JsonDocument json => "json:" + JsonSerializer.Serialize(json.RootElement),
        // A spatial value is its SRID and its geometry; the text and binary forms of one geometry are the same value.
        SpatialValue spatial => "spatial:srid=" + spatial.Srid + ":" + NormalizeNumbers(ExtendedWellKnownText.WellKnownTextOf(spatial)),
        BitArray bits => "bits:" + string.Concat(bits.Cast<bool>().Select(b => b ? "1" : "0")),
        IPAddress ip => "ip:" + ip,
        IFormattable formattable => formattable.GetType().Name + ":" + formattable.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable sequence => "seq:[" + string.Join(",", sequence.Cast<object?>().Select(Canonical)) + "]",
        _ => value.GetType().Name + ":" + value,
    };
}
