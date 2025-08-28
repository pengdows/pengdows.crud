#region

using System;
using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.exceptions;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TypeMapRegistryTests
{
    [Fact]
    public void Instance_ReturnsSameRegistry()
    {
        TypeMapRegistry.Instance.Clear();
        var first = TypeMapRegistry.Instance;
        var second = TypeMapRegistry.Instance;
        Assert.Same(first, second);
    }

    [Fact]
    public void NewInstance_DoesNotAffectSingleton()
    {
        TypeMapRegistry.Instance.Clear();
        var custom = new TypeMapRegistry();
        var customInfo = custom.GetTableInfo<MyEntity>();
        customInfo.Name = "Changed";
        var singletonInfo = TypeMapRegistry.Instance.GetTableInfo<MyEntity>();
        Assert.Equal("MyEntity", singletonInfo.Name);
    }
    [Fact]
    public void Register_AddsAndRetrievesTableInfo()
    {
        var registry = new TypeMapRegistry();
        registry.Register<MyEntity>();

        var info = registry.GetTableInfo<MyEntity>();
        Assert.NotNull(info);
        Assert.Equal("Id", info.Id?.Name);
    }

    [Fact]
    public void GetTableInfo_ThrowsIfMultipleVersions()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<TooManyColumns>(() => registry.GetTableInfo<MultipleVersions>());
    }

    [Fact]
    public void GetTableInfo_ThrowsIfIdMarkedPrimaryKey()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<PrimaryKeyOnRowIdColumn>(() => registry.GetTableInfo<IdWithPrimaryKey>());
    }

    [Fact]
    public void GetTableInfo_ThrowsIfMultipleIds()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<TooManyColumns>(() => registry.GetTableInfo<MultipleIds>());
    }

    [Fact]
    public void GetTableInfo_ThrowsIfNoColumnAttributes()
    {
        var registry = new TypeMapRegistry();
        var ex = Assert.Throws<NoColumnsFoundException>(() => registry.GetTableInfo<NoColumns>());
        Assert.Contains("no properties, marked as columns", ex.Message);
    }

    [Fact]
    public void GetTableInfo_ThrowsIfMissingTableAttribute()
    {
        var registry = new TypeMapRegistry();
        var ex = Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<NoTable>());
        Assert.Contains("does not have a TableAttribute", ex.Message);
    }

    [Fact]
    public void GetTableInfo_SetsHasAuditColumns_WhenPresent()
    {
        var registry = new TypeMapRegistry();
        registry.Register<AuditOnOnlyEntity>();

        var info = registry.GetTableInfo<AuditOnOnlyEntity>();

        Assert.True(info.HasAuditColumns);
    }

    [Fact]
    public void GetTableInfo_SetsHasAuditColumnsFalse_WhenAbsent()
    {
        var registry = new TypeMapRegistry();
        registry.Register<MyEntity>();

        var info = registry.GetTableInfo<MyEntity>();

        Assert.False(info.HasAuditColumns);
    }

    [Fact]
    public void GetTableInfo_StoresOrdinalAndPkOrder()
    {
        var registry = new TypeMapRegistry();
        var info = registry.GetTableInfo<OrderedEntity>();

        var columns1 = info.OrderedColumns;
        var columns2 = info.OrderedColumns;
        Assert.Same(columns1, columns2);
        Assert.Equal(new[] { "A", "B" }, columns1.Select(c => c.Name));

        var pks = info.PrimaryKeys;
        Assert.Equal(new[] { "A", "B" }, pks.Select(c => c.Name));
    }

    [Fact]
    public void GetTableInfo_AutoAssignsOrdinalWhenZero()
    {
        var registry = new TypeMapRegistry();
        var info = registry.GetTableInfo<MyEntity>();
        var ordinals = info.Columns.Values.Select(c => c.Ordinal).ToList();
        Assert.Equal(new[] { 1 }, ordinals);
    }

    [Fact]
    public void GetTableInfo_SucceedsWithPrimaryKeysOnly()
    {
        var registry = new TypeMapRegistry();
        var info = registry.GetTableInfo<PkOnlyEntity>();
        Assert.NotNull(info);
    }

    [Fact]
    public void GetTableInfo_ThrowsIfNoKeyDefined()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<NoKeyEntity>());
    }

    [Fact]
    public void GetTableInfo_SucceedsWithEnumColumn()
    {
        var registry = new TypeMapRegistry();
        var info = registry.GetTableInfo<EnumValidEntity>();
        Assert.True(info.Columns["State"].IsEnum);
    }

    [Fact]
    public void GetTableInfo_AllowsEnumColumnOnNonEnum()
    {
        var registry = new TypeMapRegistry();
        var info = registry.GetTableInfo<EnumAttrOnNonEnum>();
        var propType = typeof(EnumAttrOnNonEnum).GetProperty("Name")?.PropertyType;
        Assert.False(propType?.IsEnum);
        Assert.True(info.Columns["Name"].IsEnum);
    }

    [Fact]
    public void GetTableInfo_AllowsEnumPropertyMissingEnumColumn()
    {
        var registry = new TypeMapRegistry();
        var info = registry.GetTableInfo<EnumMissingAttr>();
        var propType = typeof(EnumMissingAttr).GetProperty("State")?.PropertyType;
        Assert.True(propType?.IsEnum);
        Assert.False(info.Columns["State"].IsEnum);
    }

    [Fact]
    public void GetTableInfo_SucceedsWithJsonString()
    {
        var registry = new TypeMapRegistry();
        var info = registry.GetTableInfo<JsonValidEntity>();
        Assert.True(info.Columns["Data"].IsJsonType);
    }

    [Fact]
    public void GetTableInfo_AllowsJsonNotString()
    {
        var registry = new TypeMapRegistry();
        var info = registry.GetTableInfo<JsonInvalidEntity>();
        Assert.Equal(DbType.Int32, info.Columns["Data"].DbType);
        Assert.True(info.Columns["Data"].IsJsonType);
    }

    [Fact]
    public void GetTableInfo_ThrowsOnDuplicateColumnName()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<DuplicateColumns>());
    }

    [Fact]
    public void GetTableInfo_ThrowsOnInvalidPrimaryKeyOrder()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<BadPkOrder>());
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<ZeroPkOrder>());
    }

    [Fact]
    public void GetTableInfo_ThrowsOnDuplicateOrdinal()
    {
        var registry = new TypeMapRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetTableInfo<DuplicateOrdinalEntity>());
    }

    [Fact]
    public void Registries_DoNotShareTableInfo()
    {
        var registry1 = new TypeMapRegistry();
        var registry2 = new TypeMapRegistry();

        var info1 = registry1.GetTableInfo<MyEntity>();
        info1.Name = "Changed";

        var info2 = registry2.GetTableInfo<MyEntity>();

        Assert.Equal("Changed", info1.Name);
        Assert.Equal("MyEntity", info2.Name);
    }

    [Table("MultipleVersions")]
    private class MultipleVersions
    {
        [Column("V1", DbType.Int32)] [Version] public int V1 { get; set; }

        [Column("V2", DbType.Int32)] [Version] public int V2 { get; set; }
    }

    [Table("Invalid")]
    private class IdWithPrimaryKey
    {
        [Id]
        [PrimaryKey(1)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }
    }

    [Table("MultipleIds")]
    private class MultipleIds
    {
        [Id] [Column("Id1", DbType.Int32)] public int Id1 { get; set; }
        [Id] [Column("Id2", DbType.Int32)] public int Id2 { get; set; }
    }

    [Table("NoColumns")]
    private class NoColumns
    {
        public int Unmapped { get; set; }
    }

    private class NoTable
    {
        [Column("Id", DbType.Int32)] public int Id { get; set; }
    }

    [Table("MyEntity")]
    private class MyEntity
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }
    }

    [Table("Ordered")]
    private class OrderedEntity
    {
        [PrimaryKey(2)]
        [Column("B", DbType.Int32, 2)]
        public int B { get; set; }

        [PrimaryKey(1)]
        [Column("A", DbType.Int32, 1)]
        public int A { get; set; }
    }

    [Table("PkOnly")]
    private class PkOnlyEntity
    {
        [PrimaryKey(1)]
        [Column("A", DbType.Int32)]
        public int A { get; set; }
    }

    [Table("NoKey")]
    private class NoKeyEntity
    {
        [Column("A", DbType.Int32)]
        public int A { get; set; }
    }

    private enum SampleEnum
    {
        One,
        Two
    }

    [Table("EnumValid")]
    private class EnumValidEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("State", DbType.Int32)]
        [EnumColumn(typeof(SampleEnum))]
        public SampleEnum State { get; set; }
    }

    [Table("EnumAttrOnNonEnum")]
    private class EnumAttrOnNonEnum
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        [EnumColumn(typeof(SampleEnum))]
        public string Name { get; set; } = string.Empty;
    }

    [Table("EnumMissingAttr")]
    private class EnumMissingAttr
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("State", DbType.Int32)]
        public SampleEnum State { get; set; }
    }

    [Table("JsonValid")]
    private class JsonValidEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Data", DbType.String)]
        [Json]
        public object Data { get; set; } = new();
    }

    [Table("JsonInvalid")]
    private class JsonInvalidEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Data", DbType.Int32)]
        [Json]
        public int Data { get; set; }
    }

    [Table("DuplicateColumns")]
    private class DuplicateColumns
    {
        [Id]
        [Column("Name", DbType.String)]
        public string Name1 { get; set; } = string.Empty;

        [Column("name", DbType.String)]
        public string Name2 { get; set; } = string.Empty;
    }

    [Table("BadPkOrder")]
    private class BadPkOrder
    {
        [PrimaryKey(1)]
        [Column("A", DbType.Int32)]
        public int A { get; set; }

        [PrimaryKey(1)]
        [Column("B", DbType.Int32)]
        public int B { get; set; }
    }

    [Table("ZeroPkOrder")]
    private class ZeroPkOrder
    {
        [PrimaryKey(0)]
        [Column("A", DbType.Int32)]
        public int A { get; set; }
    }

    [Table("DuplicateOrdinal")]
    private class DuplicateOrdinalEntity
    {
        [Id]
        [Column("A", DbType.Int32, 1)]
        public int A { get; set; }

        [Column("B", DbType.Int32, 1)]
        public int B { get; set; }
    }
}