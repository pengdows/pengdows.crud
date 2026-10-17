using System.Reflection;
using BenchmarkDotNet.Attributes;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// FakeDbHydrationBenchmarks says how much slower a mapper is over a whole 13-column row; this one says which
// column types the time goes on. One single-column entity per type, the same four mappers (Dapper, the hand-written
// mapper, pengdows' compiled path and pengdows' plan path) plus the bare reader read, so a per-type cost can be
// compared across mappers and against the floor.
public class FakeDbColumnHydrationBenchmarkShapeTests
{
    private static readonly string[] ExpectedColumns =
    [
        "Int32", "Int64", "String", "Decimal", "Boolean", "DateTime", "DateTimeOffset", "Double", "Single", "Guid", "Bytes",
        // the rest of the common set: nullable and NULL, the other integer widths, text-ish and temporal types,
        // and enums stored as a number and as text
        "NullableInt32", "NullableInt32Null", "StringNull", "Byte", "Int16", "SByte", "UInt16", "UInt32", "UInt64",
        "Char", "TimeSpan", "DateOnly", "TimeOnly", "EnumInt", "EnumString",
        // types Dapper reads only through a type handler a developer installs
        "JsonDocument", "JsonElement",
        // PostgreSQL types: pengdows.crud returns portable value objects; a Dapper user on Npgsql declares Npgsql's own type
        "PgRangeInt", "PgRangeTstz", "PgInterval", "PgInet", "PgCidr", "PgMacAddr", "PgIntArray", "PgTextArray", "PgHStore"
    ];

    private static readonly string[] ExpectedArms =
    [
        "Dapper", "Manual", "CompiledMapper", "ReaderMapperPlan", "ReadFloor"
    ];

    private static MethodInfo[] Arms() =>
        typeof(FakeDbColumnHydrationBenchmarks).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<BenchmarkAttribute>() != null)
            .ToArray();

    [Fact]
    public void ColumnParameter_CoversEveryCommonColumnType()
    {
        var column = typeof(FakeDbColumnHydrationBenchmarks).GetProperty("Column")!;
        var source = column.GetCustomAttribute<ParamsSourceAttribute>()!;
        var names = (IEnumerable<string>)typeof(FakeDbColumnHydrationBenchmarks)
            .GetMethod(source.Name, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;

        Assert.Equal(ExpectedColumns, names);
    }

    // BENCH_COLUMNS=PgRangeInt,PgCidr re-measures just those types (BenchmarkDotNet's filter can't select a parameter).
    [Fact]
    public void ColumnParameter_CanBeNarrowedWithAnEnvironmentVariable()
    {
        var original = Environment.GetEnvironmentVariable("BENCH_COLUMNS");
        try
        {
            Environment.SetEnvironmentVariable("BENCH_COLUMNS", "PgRangeInt, PgCidr");

            Assert.Equal(["PgRangeInt", "PgCidr"], FakeDbColumnHydrationBenchmarks.ColumnNames());
        }
        finally
        {
            Environment.SetEnvironmentVariable("BENCH_COLUMNS", original);
        }
    }

    [Fact]
    public void EveryMapperHasAnArm_AndDapperIsTheOnlyBaseline()
    {
        Assert.Equal(ExpectedArms.OrderBy(n => n), Arms().Select(m => m.Name).OrderBy(n => n));
        var baseline = Assert.Single(Arms(), m => m.GetCustomAttribute<BenchmarkAttribute>()!.Baseline);
        Assert.Equal("Dapper", baseline.Name);
    }

    [Fact]
    public async Task EveryArm_ReadsEveryRow_ForEveryColumnType()
    {
        foreach (var column in ExpectedColumns)
        {
            var benchmarks = new FakeDbColumnHydrationBenchmarks { Column = column, RowCount = 50 };
            benchmarks.GlobalSetup();

            var dapperSupported = ColumnCases.Create(column, 1).DapperSupported;
            foreach (var arm in Arms())
            {
                var result = (int)arm.Invoke(benchmarks, null)!;
                // Dapper has no mapping for some types (it reports -1 rather than hand back the wrong thing).
                var expected = arm.Name == "Dapper" && !dapperSupported ? -1 : 50;
                Assert.True(result == expected, $"{column}/{arm.Name} returned {result}, expected {expected}");
            }
        }

        await Task.CompletedTask;
    }

    [Fact]
    public void ColumnCases_ProduceTheSameValuesInEveryMapper()
    {
        foreach (var column in ExpectedColumns)
        {
            var mismatch = ColumnCases.Verify(column, rowCount: 20);
            Assert.True(mismatch == null, $"{column}: {mismatch}");
        }
    }

    // Dapper 2.1.86 re-enabled built-in DateOnly/TimeOnly mapping. The comparison has to be against the Dapper a
    // developer would use today; an older pin made Dapper look unable to read them (it was reported as "no mapping").
    [Theory]
    [InlineData("DateOnly")]
    [InlineData("TimeOnly")]
    public void Dapper_MapsDateOnlyAndTimeOnlyNatively(string column)
    {
        Assert.True(ColumnCases.Create(column, 5).DapperSupported, $"{column}: the benchmarks' Dapper cannot map it");
    }

    // Dapper pushes type conversion to the developer (SqlMapper.AddTypeHandler), so what it costs depends on the handler.
    // Each column says which baseline its Dapper arm is, so a result is never read as something it is not.
    [Fact]
    public void EveryColumn_NamesItsDapperBaseline()
    {
        string[] kinds = ["built-in", "TinyHelpers.Dapper", "none"];
        foreach (var column in ExpectedColumns)
        {
            var baseline = ColumnCases.Create(column, 1).DapperBaseline;
            Assert.Contains(kinds, k => baseline.StartsWith(k, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("JsonDocument")]
    [InlineData("JsonElement")]
    public void JsonColumns_UseTheTinyHelpersJsonHandler_ForDapper(string column)
    {
        var @case = ColumnCases.Create(column, 5);

        Assert.True(@case.DapperSupported, $"{column}: Dapper should read it through TinyHelpers' JsonTypeHandler");
        Assert.StartsWith("TinyHelpers.Dapper", @case.DapperBaseline, StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnsWithNoDapperMapping_SayNone()
    {
        foreach (var column in ExpectedColumns)
        {
            var @case = ColumnCases.Create(column, 1);
            Assert.Equal(!@case.DapperSupported, @case.DapperBaseline == "none");
        }
    }

    [Theory]
    [InlineData("PgRangeInt")]
    [InlineData("PgRangeTstz")]
    [InlineData("PgInterval")]
    [InlineData("PgInet")]
    [InlineData("PgCidr")]
    [InlineData("PgMacAddr")]
    [InlineData("PgHStore")]
    public void PostgreSqlTypes_ComparedWithDapperReadingTheProviderTypeDirectly(string column)
    {
        var @case = ColumnCases.Create(column, 5);

        // A Dapper user on Npgsql declares NpgsqlRange<int>, NpgsqlInet and so on: no handler, no conversion. The gap to
        // pengdows.crud is the price of returning a portable, database-agnostic value object.
        Assert.True(@case.DapperSupported, $"{column}: Dapper reads the provider type as it is");
        Assert.StartsWith("built-in (provider type directly)", @case.DapperBaseline, StringComparison.Ordinal);
    }
}
