using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Reads a column into a different CLR type than the one it was written as, on the real database: the conversions the
/// read path owns, which the same-type fidelity round trip (TypeFidelityIntegrationTests) never exercises. Each cell has its
/// own table (a stale cached plan from a table recreated with another type fails on PostgreSQL), writes one value in the
/// type the database declares for its semantic type (TypeFidelityDeclarations), and reads it back into another type:
///
/// - a 64-bit integer whose value fits a short reads as that exact short; one that doesn't fit throws DataMappingException;
/// - a double reads into a decimal with the digits it carries;
/// - a decimal reads into a double with the nearest double (a value within the declared scale: 12345.67);
/// - a JSON document reads into a [Json] POCO.
///
/// A cell ends PASS, FAIL, or UNSUPPORTED (the database has no declaration for the semantic type; its refusal is recorded in
/// TypeFidelityDeclarations). Only FAIL fails the test. Each database prints its counts.
/// </summary>
[Collection("IntegrationTests")]
public class TypeReadNarrowingIntegrationTests : DatabaseTestBase
{
    public TypeReadNarrowingIntegrationTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    [SkippableFact]
    public async Task NumericAndJsonColumns_ReadIntoOtherTypes_OnEveryDatabase()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var declarations = TypeFidelityDeclarations.ByDatabase.TryGetValue(provider, out var d)
                ? d
                : new Dictionary<string, (string Declaration, string ProviderType)>();
            var unsupported = TypeFidelityDeclarations.UnsupportedByDatabase.TryGetValue(provider, out var u)
                ? u
                : new Dictionary<string, string>();

            var matrix = new StringBuilder();
            var problems = new List<string>();
            var counts = new Dictionary<string, int>();
            foreach (var cell in Cells)
            {
                string status;
                if (declarations.TryGetValue(cell.Semantic, out var declared))
                {
                    status = await cell.RunAsync(context, declared.Declaration);
                }
                else if (unsupported.TryGetValue(cell.Semantic, out var reason))
                {
                    status = "UNSUPPORTED " + reason;
                }
                else
                {
                    status = "UNRECORDED no declaration and no recorded refusal";
                }

                var kind = status.Split(' ', 2)[0];
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
                matrix.AppendLine($"{provider}\t{cell.Name}\t{cell.Semantic}\t{status}");
                if (kind is "FAIL" or "UNRECORDED")
                {
                    problems.Add($"{cell.Name} ({cell.Semantic}): {status}");
                }
            }

            Output.WriteLine(matrix.ToString());
            var dir = Path.Combine(AppContext.BaseDirectory, "read-narrowing-matrix");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, provider + ".tsv"), matrix.ToString());
            Output.WriteLine($"[{provider}] " + string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")));
            Assert.True(problems.Count == 0,
                $"[{provider}] {problems.Count} read-narrowing cells failed:\n" + string.Join("\n", problems));
        });
    }

    // ---- one cell per conversion: its own table, its writer row, and its reader row ----

    private sealed record Cell(string Name, string Semantic, Func<IDatabaseContext, string, Task<string>> Run)
    {
        public Task<string> RunAsync(IDatabaseContext context, string declaration) => Run(context, declaration);
    }

    private static readonly Cell[] Cells =
    {
        new("int64 into short", "signed 64-bit", IntoShortAsync),
        new("double into decimal", "binary float64", DoubleIntoDecimalAsync),
        new("decimal into double", "fixed decimal", DecimalIntoDoubleAsync),
        new("JSON into a POCO", "JSON text / JSON value", JsonIntoPocoAsync),
    };

    // A cell's table: an id and the declared column (v), created fresh under the cell's own name.
    private static async Task CreateCellTableAsync(IDatabaseContext context, string table, string declaration)
    {
        await DropTableIfExistsAsync(context, table);
        var qualified = IntegrationObjectNameHelper.Table(context, table);
        var columns = $"{context.WrapObjectName("id")} INTEGER NOT NULL PRIMARY KEY, " +
                      $"{context.WrapObjectName("v")} {declaration} NOT NULL";
        var sql = TypeFidelityDeclarations.TableTemplates.TryGetValue(context.Dialect.DatabaseType, out var template)
            ? template.Replace("{table}", qualified).Replace("{columns}", columns)
            : $"CREATE TABLE {qualified} ({columns})";
        await using var create = context.CreateSqlContainer(sql);
        await create.ExecuteNonQueryAsync();
    }

    private static async Task<string> IntoShortAsync(IDatabaseContext context, string declaration)
    {
        try
        {
            await CreateCellTableAsync(context, Int64Writer.Table, declaration);
            var writer = new TableGateway<Int64Writer, int>(context);
            await writer.CreateAsync(new Int64Writer { Id = 1, Value = 400L }, context);
            await writer.CreateAsync(new Int64Writer { Id = 2, Value = 70000L }, context);

            var reader = new TableGateway<ShortReader, int>(context);
            var fits = await reader.RetrieveOneAsync(1, context);
            if (fits?.Value != (short)400)
            {
                return $"FAIL 400 read as {fits?.Value.ToString() ?? "null"}, not 400";
            }

            try
            {
                await reader.RetrieveOneAsync(2, context);
                return "FAIL 70000 into a short did not throw";
            }
            catch (DataMappingException error) when (error.Message.Contains("'v'", StringComparison.Ordinal))
            {
                return "PASS";
            }
        }
        catch (Exception ex)
        {
            return Describe(ex);
        }
    }

    private static async Task<string> DoubleIntoDecimalAsync(IDatabaseContext context, string declaration)
    {
        try
        {
            const double written = 1.2345678901234567d;
            await CreateCellTableAsync(context, DoubleWriter.Table, declaration);
            var writer = new TableGateway<DoubleWriter, int>(context);
            await writer.CreateAsync(new DoubleWriter { Id = 1, Value = written }, context);

            var read = await new TableGateway<DecimalReader, int>(context).RetrieveOneAsync(1, context);
            // Snowflake's FLOAT keeps 15 significant digits (the fidelity matrix's recorded limit: 0.30000000000000004 reads
            // back as 0.3), so the value it returns is 1.23456789. Every other database keeps the 17 digits written.
            var expected = context.Dialect.DatabaseType == SupportedDatabase.Snowflake ? 1.23456789m : 1.2345678901234567m;
            return read?.Value == expected
                ? "PASS"
                : $"FAIL wrote {written:R}, read {read?.Value.ToString() ?? "null"} into a decimal, not {expected}";
        }
        catch (Exception ex)
        {
            return Describe(ex);
        }
    }

    private static async Task<string> DecimalIntoDoubleAsync(IDatabaseContext context, string declaration)
    {
        try
        {
            // Two decimal places: the value must fit the declared scale of the fidelity column on every database.
            const decimal written = 12345.67m;
            await CreateCellTableAsync(context, DecimalWriter.Table, declaration);
            var writer = new TableGateway<DecimalWriter, int>(context);
            await writer.CreateAsync(new DecimalWriter { Id = 1, Value = written }, context);

            var read = await new TableGateway<DoubleReader, int>(context).RetrieveOneAsync(1, context);
            var expected = (double)written;
            return read?.Value == expected
                ? "PASS"
                : $"FAIL wrote {written}, read {read?.Value.ToString("R", CultureInfo.InvariantCulture) ?? "null"} as a double, not {expected:R}";
        }
        catch (Exception ex)
        {
            return Describe(ex);
        }
    }

    private static async Task<string> JsonIntoPocoAsync(IDatabaseContext context, string declaration)
    {
        try
        {
            await CreateCellTableAsync(context, JsonWriter.Table, declaration);
            var writer = new JsonWriter { Id = 1, Value = JsonDocument.Parse("{\"a\":7}") };
            await new TableGateway<JsonWriter, int>(context).CreateAsync(writer, context);

            var read = await new TableGateway<PocoReader, int>(context).RetrieveOneAsync(1, context);
            return read?.Value.A == 7
                ? "PASS"
                : $"FAIL read the document as A={read?.Value.A.ToString() ?? "null"}, not 7";
        }
        catch (Exception ex)
        {
            return Describe(ex);
        }
    }

    private static string Describe(Exception ex)
    {
        var cause = ex.InnerException is { } inner ? " <- " + inner.GetType().Name + ": " + inner.Message : string.Empty;
        var message = (ex.Message + cause).ReplaceLineEndings(" ");
        return "FAIL " + ex.GetType().Name + ": " + (message.Length > 360 ? message[..360] : message);
    }

    // Each cell's writer and reader rows share one table name, fixed at compile time (the attribute must be a constant).
    [Table("type_read_int64")]
    public sealed class Int64Writer
    {
        public const string Table = "type_read_int64";
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Int64)] public long Value { get; set; }
    }

    [Table("type_read_int64")]
    public sealed class ShortReader
    {
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Int16)] public short Value { get; set; }
    }

    [Table("type_read_double")]
    public sealed class DoubleWriter
    {
        public const string Table = "type_read_double";
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Double)] public double Value { get; set; }
    }

    [Table("type_read_double")]
    public sealed class DecimalReader
    {
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Decimal)] public decimal Value { get; set; }
    }

    [Table("type_read_decimal")]
    public sealed class DecimalWriter
    {
        public const string Table = "type_read_decimal";
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Decimal)] public decimal Value { get; set; }
    }

    [Table("type_read_decimal")]
    public sealed class DoubleReader
    {
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Double)] public double Value { get; set; }
    }

    [Table("type_read_json")]
    public sealed class JsonWriter
    {
        public const string Table = "type_read_json";
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Object)] public JsonDocument Value { get; set; } = null!;
    }

    [Table("type_read_json")]
    public sealed class PocoReader
    {
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Json] [Column("v", DbType.Object)] public Payload Value { get; set; } = new();
    }

    public sealed class Payload
    {
        public int A { get; set; }
    }
}
