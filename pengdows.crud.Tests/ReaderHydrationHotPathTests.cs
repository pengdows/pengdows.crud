using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Validates hot-path correctness for DataReader-to-entity hydration:
/// - GetName / GetFieldType called exactly once per column on a cold-cache miss
/// - Enum and DateTimeOffset coercion round-trips
/// - DataReaderMapper.LoadAsync materialises every row via the direct loop
/// </summary>
public class ReaderHydrationHotPathTests : SqlLiteContextTestBase
{
    public ReaderHydrationHotPathTests()
    {
        TypeMap.Register<HotPathEntity>();
        TypeMap.Register<EnumEntity>();
        TypeMap.Register<DtoEntity>();
    }

    // ------------------------------------------------------------------
    // Tests that should be RED before the array-reuse fix
    // ------------------------------------------------------------------

    [Fact]
    public void GetOrBuildRecordsetPlan_CacheMiss_CallsGetNameExactlyOnce_PerColumn()
    {
        // Fresh gateway → cold _readerPlans cache.
        // Before the fix the hash loop AND the plan-build loop each call GetName,
        // giving 2 × fieldCount.  After the fix names are collected once and reused.
        var helper = new TableGateway<HotPathEntity, int>(Context);

        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Name"] = "test",
                ["Value"] = 42
            }
        };

        using var reader = new InstrumentedReader(rows);
        reader.Read();
        helper.MapReaderToObject(reader);

        var fieldCount = 3; // Id, Name, Value
        Assert.True(reader.GetNameCallCount == fieldCount,
            $"GetName should be called exactly once per column on cache miss (expected {fieldCount}, got {reader.GetNameCallCount})");
    }

    [Fact]
    public void GetOrBuildRecordsetPlan_CacheMiss_CallsGetFieldTypeExactlyOnce_PerColumn()
    {
        var helper = new TableGateway<HotPathEntity, int>(Context);

        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Name"] = "test",
                ["Value"] = 42
            }
        };

        using var reader = new InstrumentedReader(rows);
        reader.Read();
        helper.MapReaderToObject(reader);

        var fieldCount = 3;
        Assert.True(reader.GetFieldTypeCallCount == fieldCount,
            $"GetFieldType should be called exactly once per column on cache miss (expected {fieldCount}, got {reader.GetFieldTypeCallCount})");
    }

    // ------------------------------------------------------------------
    // Regression guards (should pass before AND after)
    // ------------------------------------------------------------------

    [Fact]
    public void MapReaderToObject_EnumColumn_CoercesCorrectly()
    {
        var helper = new TableGateway<EnumEntity, int>(Context);

        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Status"] = "Active"
            }
        };

        using var reader = new InstrumentedReader(rows);
        reader.Read();
        var entity = helper.MapReaderToObject(reader);

        Assert.Equal(EntityStatus.Active, entity.Status);
    }

    [Fact]
    public void MapReaderToObject_DateTimeOffsetColumn_CoercesFromDateTime()
    {
        var helper = new TableGateway<DtoEntity, int>(Context);
        var now = DateTime.UtcNow;

        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Created"] = now
            }
        };

        using var reader = new InstrumentedReader(rows);
        reader.Read();
        var entity = helper.MapReaderToObject(reader);

        // UTC DateTime → DateTimeOffset with zero offset; round-trip must be lossless
        Assert.Equal(new DateTimeOffset(now, TimeSpan.Zero), entity.Created);
    }

    [Fact]
    public async Task DataReaderMapper_LoadAsync_MapsAllRows_DirectLoop()
    {
        // Regression guard for the LoadInternalAsync direct-loop rewrite.
        // 200 rows; spot-check first, middle, and last.
        var rows = Enumerable.Range(1, 200).Select(i => new Dictionary<string, object>
        {
            ["Id"] = i,
            ["Name"] = $"Item{i}",
            ["Value"] = i * 10
        });

        using var reader = new fakeDbDataReader(rows);
        var result = await DataReaderMapper.LoadAsync<MapperEntity>(reader, MapperOptions.Default);

        Assert.Equal(200, result.Count);

        Assert.Equal(1, result[0].Id);
        Assert.Equal("Item1", result[0].Name);
        Assert.Equal(10, result[0].Value);

        Assert.Equal(100, result[99].Id);
        Assert.Equal("Item100", result[99].Name);
        Assert.Equal(1000, result[99].Value);

        Assert.Equal(200, result[199].Id);
        Assert.Equal("Item200", result[199].Name);
        Assert.Equal(2000, result[199].Value);
    }

    // ------------------------------------------------------------------
    // Test entities
    // ------------------------------------------------------------------

    [Table("HotPathEntity")]
    private class HotPathEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string? Name { get; set; }

        [Column("Value", DbType.Int32)]
        public int Value { get; set; }
    }

    private enum EntityStatus
    {
        Active,
        Inactive
    }

    [Table("EnumEntity")]
    private class EnumEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Status", DbType.String)]
        public EntityStatus Status { get; set; }
    }

    [Table("DtoEntity")]
    private class DtoEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Created", DbType.DateTime)]
        public DateTimeOffset Created { get; set; }
    }

    // Property-name matching only — no attributes needed for DataReaderMapper
    private class MapperEntity
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    // ------------------------------------------------------------------
    // Instrumented reader — tracks metadata-method invocation counts
    // ------------------------------------------------------------------

    private sealed class InstrumentedReader : fakeDbDataReader, ITrackedReader
    {
        private int _getFieldTypeCount;
        private int _getNameCount;

        public InstrumentedReader(IEnumerable<Dictionary<string, object>> rows) : base(rows) { }

        public int GetFieldTypeCallCount => Interlocked.CompareExchange(ref _getFieldTypeCount, 0, 0);
        public int GetNameCallCount => Interlocked.CompareExchange(ref _getNameCount, 0, 0);

        public override Type GetFieldType(int ordinal)
        {
            Interlocked.Increment(ref _getFieldTypeCount);
            var value = GetValue(ordinal);
            return value?.GetType() ?? typeof(object);
        }

        public override string GetName(int ordinal)
        {
            Interlocked.Increment(ref _getNameCount);
            return base.GetName(ordinal);
        }

        public new Task<bool> ReadAsync()
        {
            return base.ReadAsync(CancellationToken.None);
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            return base.ReadAsync(cancellationToken);
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
