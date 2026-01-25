#region

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// Tests to validate that DataReaderMapper caches are bounded and evict entries
/// to prevent unbounded memory growth with varied query shapes.
/// </summary>
public class DataReaderMapperCacheBoundingTests
{
    [Fact]
    public async Task PlanCache_EvictsOldEntries_WhenCapacityExceeded()
    {
        // Get the plan cache via reflection to check its behavior
        var planCacheField =
            typeof(DataReaderMapper).GetField("_planCache", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(planCacheField);

        var planCache = planCacheField!.GetValue(null);
        Assert.NotNull(planCache);

        // The cache should have a Clear method if it's a BoundedCache
        var clearMethod = planCache!.GetType().GetMethod("Clear");
        Assert.NotNull(
            clearMethod); // This will fail if using ConcurrentDictionary (no Clear method with this signature pattern)

        // Clear the cache to start fresh
        clearMethod!.Invoke(planCache, null);

        var initialCount = GetPlanCacheCount();
        Assert.Equal(0, initialCount);

        // Generate many different schema shapes to fill the cache
        // Each unique column set creates a new plan entry
        const int numSchemas = 150; // More than any reasonable cache limit
        for (var i = 0; i < numSchemas; i++)
        {
            var columnName = $"Column{i}";
            var reader = new fakeDbDataReader(new[]
            {
                new Dictionary<string, object>
                {
                    [columnName] = $"Value{i}"
                }
            });

            await DataReaderMapper.LoadAsync<DynamicEntity>(reader, MapperOptions.Default);
        }

        var finalCount = GetPlanCacheCount();

        // The cache should be bounded - it should NOT have all 150 entries
        // A reasonable bound would be something like 64-128 entries
        Assert.True(finalCount < numSchemas,
            $"Plan cache should be bounded but has {finalCount} entries after {numSchemas} unique schemas");

        // It should have at least some entries (proving caching works)
        Assert.True(finalCount > 0, "Plan cache should have some entries");
    }

    [Fact]
    public async Task PlanCache_StillReusesPlans_ForIdenticalSchemas()
    {
        ClearPlanCache();

        var planKey = BuildPlanCacheKey<CacheTestEntity>(
            new fakeDbDataReader(new[]
            {
                new Dictionary<string, object>
                {
                    ["Name"] = "Template"
                }
            }),
            MapperOptions.Default);

        var reader1 = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "First"
            }
        });
        await DataReaderMapper.LoadAsync<CacheTestEntity>(reader1, MapperOptions.Default);
        var firstPlan = GetPlanEntry(planKey);
        Assert.NotNull(firstPlan);

        var reader2 = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "Second"
            }
        });
        await DataReaderMapper.LoadAsync<CacheTestEntity>(reader2, MapperOptions.Default);
        var secondPlan = GetPlanEntry(planKey);
        Assert.Same(firstPlan, secondPlan);
    }

    [Fact]
    public void PlanCache_HasClearMethod()
    {
        // Verify the cache exposes a Clear method for controlled resets
        var planCacheField =
            typeof(DataReaderMapper).GetField("_planCache", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(planCacheField);

        var planCache = planCacheField!.GetValue(null);
        Assert.NotNull(planCache);

        var clearMethod = planCache!.GetType().GetMethod("Clear");
        Assert.NotNull(clearMethod);
    }

    [Fact]
    public void SetterCache_IsBounded()
    {
        // Verify the setter cache is also bounded
        var setterCacheField =
            typeof(DataReaderMapper).GetField("_setterCache", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(setterCacheField);

        var setterCache = setterCacheField!.GetValue(null);
        Assert.NotNull(setterCache);

        // Check if it's a BoundedCache by looking for the Clear method
        var clearMethod = setterCache!.GetType().GetMethod("Clear");
        Assert.NotNull(clearMethod);
    }

    private static int GetPlanCacheCount()
    {
        var cacheField = typeof(DataReaderMapper).GetField("_planCache", BindingFlags.Static | BindingFlags.NonPublic);
        var cache = cacheField!.GetValue(null)!;

        var countField = cache.GetType().GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic);
        return countField != null ? (int)countField.GetValue(cache)! : -1;
    }

    private static void ClearPlanCache()
    {
        var planCacheField =
            typeof(DataReaderMapper).GetField("_planCache", BindingFlags.Static | BindingFlags.NonPublic);
        var planCache = planCacheField?.GetValue(null);
        var clearMethod = planCache?.GetType().GetMethod("Clear");
        clearMethod?.Invoke(planCache, null);
    }

    private static object BuildPlanCacheKey<T>(DbDataReader templateReader, MapperOptions options)
    {
        var schemaHashMethod = typeof(DataReaderMapper).GetMethod(
                                   "BuildSchemaHash",
                                   BindingFlags.NonPublic | BindingFlags.Static)
                               ?? throw new InvalidOperationException("BuildSchemaHash not found");

        var schemaHash = (string)schemaHashMethod.Invoke(null, new object[] { templateReader, options })!;

        var planKeyType = typeof(DataReaderMapper)
                              .GetNestedType("PlanCacheKey", BindingFlags.NonPublic)
                          ?? throw new InvalidOperationException("PlanCacheKey type not found");

        return Activator.CreateInstance(
            planKeyType,
            new object[] { typeof(T), schemaHash, options.ColumnsOnly, options.EnumMode })!;
    }

    private static object? GetPlanEntry(object planCacheKey)
    {
        var planCacheField =
            typeof(DataReaderMapper).GetField("_planCache", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Plan cache field missing");

        var planCache = planCacheField.GetValue(null)!;
        var mapField = planCache.GetType().GetField("_map", BindingFlags.Instance | BindingFlags.NonPublic)
                       ?? throw new InvalidOperationException("Plan cache map missing");

        var map = mapField.GetValue(planCache)!;
        var tryGetValue = map.GetType().GetMethod("TryGetValue")!;
        var args = new object?[] { planCacheKey, null };
        var found = (bool)tryGetValue.Invoke(map, args)!;
        return found ? args[1] : null;
    }

    private class DynamicEntity
    {
        public string? Column0 { get; set; }
        public string? Column1 { get; set; }
        public string? Column2 { get; set; }
        public string? Column3 { get; set; }
        public string? Column4 { get; set; }
        public string? Column5 { get; set; }
        public string? Column6 { get; set; }
        public string? Column7 { get; set; }
        public string? Column8 { get; set; }

        public string? Column9 { get; set; }

        // Add more to cover various column names
        public string? Column10 { get; set; }
        public string? Column11 { get; set; }
        public string? Column12 { get; set; }
        public string? Column13 { get; set; }
        public string? Column14 { get; set; }
        public string? Column15 { get; set; }
        public string? Column16 { get; set; }
        public string? Column17 { get; set; }
        public string? Column18 { get; set; }
        public string? Column19 { get; set; }
        public string? Column20 { get; set; }
        public string? Column21 { get; set; }
        public string? Column22 { get; set; }
        public string? Column23 { get; set; }
        public string? Column24 { get; set; }
        public string? Column25 { get; set; }
        public string? Column26 { get; set; }
        public string? Column27 { get; set; }
        public string? Column28 { get; set; }
        public string? Column29 { get; set; }
        public string? Column30 { get; set; }
        public string? Column31 { get; set; }
        public string? Column32 { get; set; }
        public string? Column33 { get; set; }
        public string? Column34 { get; set; }
        public string? Column35 { get; set; }
        public string? Column36 { get; set; }
        public string? Column37 { get; set; }
        public string? Column38 { get; set; }
        public string? Column39 { get; set; }
        public string? Column40 { get; set; }
        public string? Column41 { get; set; }
        public string? Column42 { get; set; }
        public string? Column43 { get; set; }
        public string? Column44 { get; set; }
        public string? Column45 { get; set; }
        public string? Column46 { get; set; }
        public string? Column47 { get; set; }
        public string? Column48 { get; set; }
        public string? Column49 { get; set; }
        public string? Column50 { get; set; }
        public string? Column51 { get; set; }
        public string? Column52 { get; set; }
        public string? Column53 { get; set; }
        public string? Column54 { get; set; }
        public string? Column55 { get; set; }
        public string? Column56 { get; set; }
        public string? Column57 { get; set; }
        public string? Column58 { get; set; }
        public string? Column59 { get; set; }
        public string? Column60 { get; set; }
        public string? Column61 { get; set; }
        public string? Column62 { get; set; }
        public string? Column63 { get; set; }
        public string? Column64 { get; set; }
        public string? Column65 { get; set; }
        public string? Column66 { get; set; }
        public string? Column67 { get; set; }
        public string? Column68 { get; set; }
        public string? Column69 { get; set; }
        public string? Column70 { get; set; }
        public string? Column71 { get; set; }
        public string? Column72 { get; set; }
        public string? Column73 { get; set; }
        public string? Column74 { get; set; }
        public string? Column75 { get; set; }
        public string? Column76 { get; set; }
        public string? Column77 { get; set; }
        public string? Column78 { get; set; }
        public string? Column79 { get; set; }
        public string? Column80 { get; set; }
        public string? Column81 { get; set; }
        public string? Column82 { get; set; }
        public string? Column83 { get; set; }
        public string? Column84 { get; set; }
        public string? Column85 { get; set; }
        public string? Column86 { get; set; }
        public string? Column87 { get; set; }
        public string? Column88 { get; set; }
        public string? Column89 { get; set; }
        public string? Column90 { get; set; }
        public string? Column91 { get; set; }
        public string? Column92 { get; set; }
        public string? Column93 { get; set; }
        public string? Column94 { get; set; }
        public string? Column95 { get; set; }
        public string? Column96 { get; set; }
        public string? Column97 { get; set; }
        public string? Column98 { get; set; }
        public string? Column99 { get; set; }
        public string? Column100 { get; set; }
        public string? Column101 { get; set; }
        public string? Column102 { get; set; }
        public string? Column103 { get; set; }
        public string? Column104 { get; set; }
        public string? Column105 { get; set; }
        public string? Column106 { get; set; }
        public string? Column107 { get; set; }
        public string? Column108 { get; set; }
        public string? Column109 { get; set; }
        public string? Column110 { get; set; }
        public string? Column111 { get; set; }
        public string? Column112 { get; set; }
        public string? Column113 { get; set; }
        public string? Column114 { get; set; }
        public string? Column115 { get; set; }
        public string? Column116 { get; set; }
        public string? Column117 { get; set; }
        public string? Column118 { get; set; }
        public string? Column119 { get; set; }
        public string? Column120 { get; set; }
        public string? Column121 { get; set; }
        public string? Column122 { get; set; }
        public string? Column123 { get; set; }
        public string? Column124 { get; set; }
        public string? Column125 { get; set; }
        public string? Column126 { get; set; }
        public string? Column127 { get; set; }
        public string? Column128 { get; set; }
        public string? Column129 { get; set; }
        public string? Column130 { get; set; }
        public string? Column131 { get; set; }
        public string? Column132 { get; set; }
        public string? Column133 { get; set; }
        public string? Column134 { get; set; }
        public string? Column135 { get; set; }
        public string? Column136 { get; set; }
        public string? Column137 { get; set; }
        public string? Column138 { get; set; }
        public string? Column139 { get; set; }
        public string? Column140 { get; set; }
        public string? Column141 { get; set; }
        public string? Column142 { get; set; }
        public string? Column143 { get; set; }
        public string? Column144 { get; set; }
        public string? Column145 { get; set; }
        public string? Column146 { get; set; }
        public string? Column147 { get; set; }
        public string? Column148 { get; set; }
        public string? Column149 { get; set; }
    }

    private class CacheTestEntity
    {
        public string? Name { get; set; }
    }
}