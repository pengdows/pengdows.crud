using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests that IDatabaseContext.ReaderPlanCacheSize is configurable and that
/// TableGateway respects the configured limit when caching reader plans.
/// </summary>
public class ReaderPlanCacheSizeTests
{
    // Three minimal entities that differ only in column shape so each produces
    // a distinct reader plan cache key.
    [Table("A")]
    private class EntityA
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("X", DbType.String)]
        public string X { get; set; } = "";
    }

    [Table("B")]
    private class EntityB
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Y", DbType.String)]
        public string Y { get; set; } = "";
    }

    [Table("C")]
    private class EntityC
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("Z", DbType.String)]
        public string Z { get; set; } = "";
    }

    private static DatabaseContext MakeContext(int? readerPlanCacheSize = null)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            ReaderPlanCacheSize = readerPlanCacheSize
        };
        return new DatabaseContext(config, factory);
    }

    // -------------------------------------------------------------------------
    // IDatabaseContext must expose the property
    // -------------------------------------------------------------------------

    [Fact]
    public void IDatabaseContext_HasReaderPlanCacheSizeProperty()
    {
        var prop = typeof(IDatabaseContext).GetProperty(nameof(IDatabaseContext.ReaderPlanCacheSize));
        Assert.NotNull(prop);
        Assert.Equal(typeof(int?), prop!.PropertyType);
    }

    [Fact]
    public void DatabaseContext_ReaderPlanCacheSize_DefaultIsNull()
    {
        using var ctx = MakeContext();
        Assert.Null(ctx.ReaderPlanCacheSize);
    }

    [Fact]
    public void DatabaseContext_ReaderPlanCacheSize_ReflectsConfiguredValue()
    {
        using var ctx = MakeContext(readerPlanCacheSize: 10);
        Assert.Equal(10, ctx.ReaderPlanCacheSize);
    }

    // -------------------------------------------------------------------------
    // TableGateway must size its _readerPlans cache from the config value
    // -------------------------------------------------------------------------

    [Fact]
    public void TableGateway_DefaultCacheSize_Is32()
    {
        // When ReaderPlanCacheSize is null the gateway uses the built-in default of 32.
        var typeMap = new TypeMapRegistry();
        typeMap.Register<EntityA>();
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory, typeMap);

        var gateway = new TableGateway<EntityA, int>(ctx);

        // _readerPlans is declared on BaseTableGateway<TEntity> (moved from TableGateway during refactor).
        var cacheField = typeof(BaseTableGateway<EntityA>)
            .GetField("_readerPlans",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(cacheField);

        var cache = cacheField!.GetValue(gateway);
        Assert.NotNull(cache);

        // BoundedCache exposes its capacity via a Capacity property
        var capacityProp = cache!.GetType().GetProperty("Capacity");
        Assert.NotNull(capacityProp);
        Assert.Equal(32, capacityProp!.GetValue(cache));
    }

    [Fact]
    public void TableGateway_ConfiguredCacheSize_IsRespected()
    {
        const int configured = 5;
        var typeMap = new TypeMapRegistry();
        typeMap.Register<EntityA>();
        using var ctx = MakeContext(readerPlanCacheSize: configured);

        var gateway = new TableGateway<EntityA, int>(ctx);

        // _readerPlans is declared on BaseTableGateway<TEntity> (moved from TableGateway during refactor).
        var cacheField = typeof(BaseTableGateway<EntityA>)
            .GetField("_readerPlans",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = cacheField!.GetValue(gateway);
        var capacityProp = cache!.GetType().GetProperty("Capacity");
        Assert.Equal(configured, capacityProp!.GetValue(cache));
    }

    // CORE-019: ReaderPlanCacheSize (and table metadata) are captured once from whichever
    // IDatabaseContext constructs the gateway. In the documented multi-tenancy pattern
    // (CLAUDE.md's "Multi-Tenancy" section), one singleton gateway is reused across MANY
    // different tenant IDatabaseContext instances passed per-call — but that per-call context
    // only ever changes which physical connection executes the operation. It never re-derives
    // the gateway's own _readerPlans capacity or _tableInfo. This is intentional, not a gap:
    // reader plans are keyed by RecordsetShape (column names/types), a property of the entity's
    // query result shape, not of which tenant's database produced it, so sharing one cache
    // across all tenants using the same entity is correct and avoids pointless recompilation.
    // MapReaderToObject makes this concrete: it has no IDatabaseContext parameter at all — it
    // can't distinguish which context's connection produced the reader it's mapping.
    [Fact]
    public void TableGateway_ReaderPlanCacheSize_StaysFixedFromConstructorContext_RegardlessOfLaterReaderSource()
    {
        const int constructorCacheSize = 5;
        const int otherTenantCacheSize = 999;

        var typeMap = new TypeMapRegistry();
        typeMap.Register<EntityA>();

        using var constructorContext = MakeContext(readerPlanCacheSize: constructorCacheSize);
        using var otherTenantContext = MakeContext(readerPlanCacheSize: otherTenantCacheSize);

        var gateway = new TableGateway<EntityA, int>(constructorContext);

        var cacheField = typeof(BaseTableGateway<EntityA>)
            .GetField("_readerPlans",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = cacheField!.GetValue(gateway);
        var capacityProp = cache!.GetType().GetProperty("Capacity");

        Assert.Equal(constructorCacheSize, capacityProp!.GetValue(cache));

        // Map a row that, per CLAUDE.md's documented multi-tenancy pattern, could have come
        // from otherTenantContext's own connection — MapReaderToObject has no way to know or
        // care, since it takes only a reader, never an IDatabaseContext.
        var row = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["X"] = "tenant-b-value"
            }
        };
        using var reader = new FakeTrackedReader(row);
        reader.Read();
        var entity = gateway.MapReaderToObject(reader);
        Assert.Equal("tenant-b-value", entity.X);

        // The cache actually got used (proving this isn't a vacuous check)...
        var countProp = cache.GetType().GetProperty("Count");
        Assert.True((int)countProp!.GetValue(cache)! >= 1);

        // ...but its capacity is still the constructor context's value, never otherTenantContext's.
        Assert.Equal(constructorCacheSize, capacityProp.GetValue(cache));
        _ = otherTenantContext; // never actually used to build or execute anything — that's the point.
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

        public override System.Type GetFieldType(int ordinal)
        {
            var value = GetValue(ordinal);
            return value?.GetType() ?? typeof(object);
        }
    }
}