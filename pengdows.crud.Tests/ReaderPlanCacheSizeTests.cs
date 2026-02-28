using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
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

        var cacheField = typeof(TableGateway<EntityA, int>)
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

        var cacheField = typeof(TableGateway<EntityA, int>)
            .GetField("_readerPlans",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = cacheField!.GetValue(gateway);
        var capacityProp = cache!.GetType().GetProperty("Capacity");
        Assert.Equal(configured, capacityProp!.GetValue(cache));
    }
}