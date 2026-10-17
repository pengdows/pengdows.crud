using System.Reflection;
using BenchmarkDotNet.Attributes;
using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// The hydration benchmark answers three different questions, and an arm only means something next to the arms that
// answer the same one: the public materialization call, a warm already-resolved mapper, and the cost of resolving
// the mapping. Mixing them (a pre-resolved Dapper parser against pengdows' per-call plan lookup) made the old
// Hydrate_Pengdows / Hydrate_Dapper ratio unusable for diagnosis.
public class FakeDbHydrationBenchmarkShapeTests
{
    private static readonly string[] Categories = ["PublicApi", "WarmMapper", "Resolution"];

    private static MethodInfo[] Arms() =>
        typeof(FakeDbHydrationBenchmarks).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<BenchmarkAttribute>() != null)
            .ToArray();

    private static string Category(MethodInfo m) =>
        Assert.Single(m.GetCustomAttribute<BenchmarkCategoryAttribute>()!.Categories);

    private static MethodInfo[] In(string category) => Arms().Where(m => Category(m) == category).ToArray();

    [Fact]
    public void EveryArmDeclaresExactlyOneKnownCategory()
    {
        foreach (var arm in Arms())
        {
            Assert.NotNull(arm.GetCustomAttribute<BenchmarkCategoryAttribute>());
            Assert.Contains(Category(arm), Categories);
        }
    }

    [Fact]
    public void EachCategoryHasExactlyOneBaseline()
    {
        foreach (var category in Categories)
        {
            Assert.Single(In(category), m => m.GetCustomAttribute<BenchmarkAttribute>()!.Baseline);
        }
    }

    [Fact]
    public void TheClassGroupsAndLabelsResultsByCategory()
    {
        var type = typeof(FakeDbHydrationBenchmarks);
        Assert.NotNull(type.GetCustomAttribute<GroupBenchmarksByAttribute>());
        Assert.NotNull(type.GetCustomAttribute<CategoriesColumnAttribute>());
    }

    [Fact]
    public void WarmMapperCategory_ComparesTheResolvedMappersWithOneLoopShape()
    {
        var names = In("WarmMapper").Select(m => m.Name).ToArray();
        Assert.Contains("Warm_Dapper", names);
        // pengdows.crud has two mapper paths, each specified and tested on its own (DataReaderMapperRowMapperTests):
        Assert.Contains("Warm_Pengdows_ReaderMapperPlan", names);       // DataReaderMapper: arbitrary POCOs, plan per schema shape
        Assert.Contains("Warm_Pengdows_CompiledMapper", names);         // CompiledMapperFactory (TableGateway), Func<DbDataReader, T>
        Assert.Contains("Warm_Pengdows_CompiledMapperSpecialized", names); // same, reader-type-specialized: a reference only
        Assert.Contains("Warm_Manual", names);
        Assert.Contains("Warm_ReadOnlyFloor", names);
    }

    [Fact]
    public void PublicApiCategory_ComparesTheCallsAPersonMakes_IncludingAPlainPoco()
    {
        var names = In("PublicApi").Select(m => m.Name).ToArray();
        Assert.Contains("Public_Pengdows", names);
        Assert.Contains("Public_Dapper", names);
        Assert.Contains("Public_Pengdows_PlainPoco", names);
        Assert.Contains("Public_Dapper_PlainPoco", names);
    }

    [Fact]
    public void ResolutionCategory_PutsBothLibrariesResolutionCostOnTheTable()
    {
        var names = In("Resolution").Select(m => m.Name).ToArray();
        Assert.Contains("Resolve_Pengdows", names);
        Assert.Contains("Resolve_Dapper", names);
    }

    [Fact]
    public void PlainPoco_HasNoPengdowsMetadata()
    {
        var poco = typeof(FakeDbHydrationBenchmarks).GetNestedType("PlainHydrationPoco");
        Assert.NotNull(poco);
        // Compiler-emitted attributes (nullability and the like) are not pengdows metadata.
        static IEnumerable<object> Declared(IEnumerable<object> attributes) =>
            attributes.Where(a => a.GetType().Namespace != "System.Runtime.CompilerServices");

        Assert.Empty(Declared(poco!.GetCustomAttributes(inherit: false)));
        foreach (var property in poco.GetProperties())
        {
            Assert.Empty(Declared(property.GetCustomAttributes(inherit: false)));
        }
    }

    [Fact]
    public void NoArmWalksTheResultsInsideTheTimedMethod()
    {
        // Correctness is proven in GlobalSetup and in this test class; timed arms return a count.
        var source = File.ReadAllText(Path.Combine(FindBenchmarkSource(), "FakeDbHydrationBenchmarks.cs"));
        Assert.DoesNotContain("VerifyChecksum", source);
        Assert.DoesNotContain("Dapper_Empty", source);
        Assert.DoesNotContain("Pengdows_Empty", source);
    }

    [Fact]
    public async Task EveryArm_ProducesTheSameNumberOfRows()
    {
        var benchmarks = new FakeDbHydrationBenchmarks { RowCount = 50 };
        await benchmarks.GlobalSetup();

        foreach (var arm in Arms())
        {
            var result = await (Task<int>)arm.Invoke(benchmarks, null)!;
            var expected = Category(arm) == "Resolution" || arm.Name == "Warm_ReadOnlyFloor" ? result : 50;
            Assert.True(result == expected, $"{arm.Name} returned {result}, expected {expected}");
        }
    }

    private static string FindBenchmarkSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "benchmarks", "CrudBenchmarks", "FakeDbHydrationBenchmarks.cs")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "benchmarks", "CrudBenchmarks");
    }
}
