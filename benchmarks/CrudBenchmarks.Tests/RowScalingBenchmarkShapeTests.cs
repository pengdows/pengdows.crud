using System.Reflection;
using BenchmarkDotNet.Attributes;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// The row-scaling benchmarks separate a library's fixed cost per query from its cost per row: the same two read queries
// at 1 to 10,000 rows. That only works if every arm can actually return 10,000 rows (the equal-footing classes seed
// 1,000, which caps the filtered query near 210), if the arms run the same SQL, and if only the two multi-row reads
// scale (a looping scenario at 10,000 would be 10,000 separate round trips, not a bigger result set).
public class RowScalingBenchmarkShapeTests
{
    public static TheoryData<Type> Classes => new()
    {
        typeof(PostgreSqlRowScalingBenchmarks),
        typeof(SqlServerRowScalingBenchmarks)
    };

    [Theory]
    [MemberData(nameof(Classes))]
    public void TheClass_IsOptIn_TracksMemory_AndScalesFromOneToTenThousandRows(Type type)
    {
        Assert.NotNull(type.GetCustomAttribute<OptInBenchmarkAttribute>());
        Assert.NotNull(type.GetCustomAttribute<MemoryDiagnoserAttribute>());
        var recordCount = type.GetProperty("RecordCount")!.GetCustomAttribute<ParamsAttribute>()!;
        Assert.Equal(new object[] { 1, 10, 100, 1000, 10000 }, recordCount.Values);
    }

    [Theory]
    [MemberData(nameof(Classes))]
    public void OnlyTheTwoMultiRowReads_AreBenchmarked_ForEveryLibrary(Type type)
    {
        var benchmarks = type.GetMethods()
            .Where(m => m.GetCustomAttribute<BenchmarkAttribute>() != null)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(new[]
        {
            "FilteredQuery_Dapper", "FilteredQuery_EntityFramework", "FilteredQuery_Pengdows",
            "ReadList_Dapper", "ReadList_EntityFramework", "ReadList_Pengdows"
        }, benchmarks);
    }

    [Fact]
    public void TheSeed_LetsBothQueriesReturnTenThousandRows()
    {
        Assert.True(RowScalingSeed.ReadListMatches(RowScalingSeed.Rows) >= 10_000);
        Assert.True(RowScalingSeed.FilteredQueryMatches(RowScalingSeed.Rows) >= 10_000);
    }

    [Fact]
    public void TheMatchCounts_FollowTheSeedFormula()
    {
        // age = 20 + i % 50, is_active = i is even. ReadList: age > 30. FilteredQuery: active and 25 <= age <= 45.
        var readList = Enumerable.Range(1, 1000).Count(i => RowScalingSeed.Age(i) > RowScalingSeed.ReadListMinAgeExclusive);
        var filtered = Enumerable.Range(1, 1000).Count(i => RowScalingSeed.IsActive(i)
            && RowScalingSeed.Age(i) >= RowScalingSeed.FilteredMinAge && RowScalingSeed.Age(i) <= RowScalingSeed.FilteredMaxAge);

        Assert.Equal(readList, RowScalingSeed.ReadListMatches(1000));
        Assert.Equal(filtered, RowScalingSeed.FilteredQueryMatches(1000));
        Assert.Equal(780, readList);
        Assert.Equal(200, filtered);
    }

    // Dapper and EF both run these constants; pengdows builds the same WHERE and limit through its gateway.
    [Fact]
    public void TheSharedSql_UsesEachDatabasesRowLimit()
    {
        Assert.Contains("LIMIT @Limit", PostgreSqlRowScalingBenchmarks.ReadListSql);
        Assert.Contains("LIMIT @Limit", PostgreSqlRowScalingBenchmarks.FilteredQuerySql);
        Assert.Contains("FETCH NEXT @Limit ROWS ONLY", SqlServerRowScalingBenchmarks.ReadListSql);
        Assert.Contains("FETCH NEXT @Limit ROWS ONLY", SqlServerRowScalingBenchmarks.FilteredQuerySql);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(60_000)]
    public void SeedBatches_CoverEveryRowOnce_InBatchesSqlServerAccepts(int rows)
    {
        var batches = RowScalingSeed.Batches(rows).ToList();

        Assert.All(batches, b => Assert.InRange(b.Count, 1, 1000));
        Assert.Equal(Enumerable.Range(1, rows), batches.SelectMany(b => Enumerable.Range(b.First, b.Count)));
    }
}

public class RowScalingValidationTests
{
    private sealed record Row(string Name, string CreatedAt, bool Active);

    private static (string, string, bool) Describe(Row r) => (r.Name, r.CreatedAt, r.Active);

    [Fact]
    public void FullyHydratedRows_OfTheExpectedCount_Pass()
    {
        RowScalingValidation.Check("arm", 2, new[] { new Row("a", "t", true), new Row("b", "t", true) }, Describe);
    }

    [Fact]
    public void TheWrongRowCount_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RowScalingValidation.Check("arm", 3, new[] { new Row("a", "t", true) }, Describe));
    }

    [Fact]
    public void AnUnmappedColumn_Throws()
    {
        // What a Dapper arm without MatchNamesWithUnderscores returns: created_at left at its default.
        Assert.Throws<InvalidOperationException>(() =>
            RowScalingValidation.Check("arm", 1, new[] { new Row("a", "", true) }, Describe));
    }

    [Fact]
    public void ARowOutsideTheFilter_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RowScalingValidation.Check("arm", 1, new[] { new Row("a", "t", false) }, Describe));
    }
}
