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

    // BP-102 (3.0 4399d3a, CORE-013): reader plans were keyed by a bare 32-bit HashCode widened
    // to long with no structural verification, so two distinct shapes that hash-collide collapse
    // into one cache entry and reuse the wrong compiled mapper. System.HashCode is seeded per
    // process, so the test searches (with the production algorithm) for a collision in this run.
    [Fact]
    public void MapReaderToObject_TwoDistinctShapesWithCollidingHash_CacheTwoSeparatePlans()
    {
        var helper = new TableGateway<NameEntity, int>(Context);

        var (extraNameA, extraNameB) = FindDistinctExtraColumnNamesWithCollidingHash();

        using var readerA = new FakeTrackedReader(new[]
        {
            new Dictionary<string, object> { ["Id"] = 1, ["Name"] = "Alice", [extraNameA] = 111 }
        });
        readerA.Read();
        var entityA = helper.MapReaderToObject(readerA);
        Assert.Equal("Alice", entityA.Name);

        using var readerB = new FakeTrackedReader(new[]
        {
            new Dictionary<string, object> { ["Id"] = 2, ["Name"] = "Bob", [extraNameB] = 222 }
        });
        readerB.Read();
        var entityB = helper.MapReaderToObject(readerB);
        Assert.Equal("Bob", entityB.Name);

        Assert.Equal(2, GetReaderPlanCacheCount(helper));
    }

    private static (string ExtraNameA, string ExtraNameB) FindDistinctExtraColumnNamesWithCollidingHash()
    {
        var seen = new Dictionary<int, string>();
        for (var i = 0; i < 2_000_000; i++)
        {
            var extraName = "Extra" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var names = new[] { "Id", "Name", extraName };
            var types = new[] { typeof(int), typeof(string), typeof(int) };
            var hashBuilder = new HashCode();
            hashBuilder.Add(names.Length);
            for (var f = 0; f < names.Length; f++)
            {
                hashBuilder.Add(names[f], StringComparer.OrdinalIgnoreCase);
                hashBuilder.Add(types[f]);
            }

            var hash = hashBuilder.ToHashCode();
            if (seen.TryGetValue(hash, out var existing))
            {
                return (existing, extraName);
            }

            seen[hash] = extraName;
        }

        throw new InvalidOperationException("Could not find a hash collision within the search budget.");
    }

    private static int GetReaderPlanCacheCount(TableGateway<NameEntity, int> helper)
    {
        var field = typeof(BaseTableGateway<NameEntity>).GetField(
            "_readerPlans", BindingFlags.NonPublic | BindingFlags.Instance);
        var cache = field!.GetValue(helper)!;
        return (int)cache.GetType().GetProperty("Count")!.GetValue(cache)!;
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