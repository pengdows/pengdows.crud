using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class TableGatewayRecordsetPlanTests : SqlLiteContextTestBase
{
    public TableGatewayRecordsetPlanTests()
    {
        TypeMap.Register<NameEntity>();
    }

    [Fact]
    public void MapReaderToObject_DifferentFieldTypes_BuildsSeparatePlans()
    {
        var helper = new TableGateway<NameEntity, int>(Context);

        var rows1 = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Name"] = "Alice"
            }
        };
        using var reader1 = new FakeTrackedReader(rows1);
        reader1.Read();
        var e1 = helper.MapReaderToObject(reader1);
        Assert.Equal("Alice", e1.Name);

        var rows2 = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 2,
                ["Name"] = 123
            }
        };
        using var reader2 = new FakeTrackedReader(rows2);
        reader2.Read();
        var e2 = helper.MapReaderToObject(reader2);
        Assert.Equal("123", e2.Name);
    }

    // CORE-013: reader plans were previously keyed directly by a bare 32-bit HashCode widened to
    // long, with no verification that a hash hit actually came from the same schema. Two
    // genuinely different shapes that happen to hash-collide would silently collapse into one
    // cache entry, reusing the wrong compiled mapper. Rather than relying on a fixed pair of
    // inputs (System.HashCode's seed is randomized per process, so a hard-coded pair found once
    // would not necessarily collide on a different run), this test performs a bounded, in-process
    // search — using the exact same hashing algorithm the production code uses — for two
    // genuinely distinct shapes that collide for *this* run's seed, then proves the cache holds
    // two separate entries rather than collapsing them into one.
    [Fact]
    public void MapReaderToObject_TwoDistinctShapesWithCollidingHash_CacheTwoSeparatePlans()
    {
        var helper = new TableGateway<NameEntity, int>(Context);

        var (extraNameA, extraNameB) = FindDistinctExtraColumnNamesWithCollidingHash();

        var rowA = new Dictionary<string, object>
        {
            ["Id"] = 1,
            ["Name"] = "Alice",
            [extraNameA] = 111
        };
        using var readerA = new FakeTrackedReader(new[] { rowA });
        readerA.Read();
        var entityA = helper.MapReaderToObject(readerA);
        Assert.Equal(1, entityA.Id);
        Assert.Equal("Alice", entityA.Name);

        var rowB = new Dictionary<string, object>
        {
            ["Id"] = 2,
            ["Name"] = "Bob",
            [extraNameB] = 222
        };
        using var readerB = new FakeTrackedReader(new[] { rowB });
        readerB.Read();
        var entityB = helper.MapReaderToObject(readerB);
        Assert.Equal(2, entityB.Id);
        Assert.Equal("Bob", entityB.Name);

        var planCacheCount = GetReaderPlanCacheCount(helper);
        Assert.Equal(2, planCacheCount);
    }

    /// <summary>
    /// Reproduces RecordsetShape.GetHashCode()'s exact algorithm (fieldCount, then each field's
    /// name via StringComparer.OrdinalIgnoreCase, then its type) to find two distinct three-column
    /// shapes — "Id"(int)/"Name"(string)/extraName(int) — whose extra column name differs but
    /// whose overall hash collides, for whatever seed System.HashCode is using in this process.
    /// </summary>
    private static (string ExtraNameA, string ExtraNameB) FindDistinctExtraColumnNamesWithCollidingHash()
    {
        var seen = new Dictionary<int, string>();
        for (var i = 0; i < 2_000_000; i++)
        {
            var extraName = "Extra" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var hash = ComputeShapeHash(extraName);

            if (seen.TryGetValue(hash, out var existingExtraName))
            {
                if (!string.Equals(existingExtraName, extraName, StringComparison.OrdinalIgnoreCase))
                {
                    return (existingExtraName, extraName);
                }

                continue;
            }

            seen[hash] = extraName;
        }

        throw new InvalidOperationException(
            "Could not find a hash collision within the search budget — this would need a larger " +
            "budget or a different search strategy, not a change to the production fix.");
    }

    private static int ComputeShapeHash(string extraColumnName)
    {
        var names = new[] { "Id", "Name", extraColumnName };
        var types = new[] { typeof(int), typeof(string), typeof(int) };

        var hashBuilder = new HashCode();
        hashBuilder.Add(names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            hashBuilder.Add(names[i], StringComparer.OrdinalIgnoreCase);
            hashBuilder.Add(types[i]);
        }

        return hashBuilder.ToHashCode();
    }

    private static int GetReaderPlanCacheCount(TableGateway<NameEntity, int> helper)
    {
        var field = typeof(BaseTableGateway<NameEntity>).GetField(
            "_readerPlans", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);

        var cache = field!.GetValue(helper);
        Assert.NotNull(cache);

        var countProperty = cache!.GetType().GetProperty("Count");
        Assert.NotNull(countProperty);

        return (int)countProperty!.GetValue(cache)!;
    }

    [Fact]
    public void MapReaderToObject_WithMoreThanPoolThresholdFields_UsesSharedArrayPoolPaths()
    {
        var helper = new TableGateway<NameEntity, int>(Context);
        var row = new Dictionary<string, object>
        {
            ["Id"] = 11,
            ["Name"] = "Large"
        };

        for (var i = 0; i < 70; i++)
        {
            row[$"Extra{i}"] = i;
        }

        using var reader = new FakeTrackedReader(new[] { row });
        reader.Read();
        var entity = helper.MapReaderToObject(reader);

        Assert.Equal(11, entity.Id);
        Assert.Equal("Large", entity.Name);
    }

    [Table("NameEntity")]
    private class NameEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)] public string? Name { get; set; }
    }

    private sealed class FakeTrackedReader : fakeDbDataReader, ITrackedReader
    {
        public FakeTrackedReader(IEnumerable<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public new ValueTask<bool> ReadAsync()
        {
            return new ValueTask<bool>(base.ReadAsync(CancellationToken.None));
        }

        public new ValueTask<bool> ReadAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<bool>(base.ReadAsync(cancellationToken));
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public override Type GetFieldType(int ordinal)
        {
            var value = GetValue(ordinal);
            return value?.GetType() ?? typeof(object);
        }
    }
}