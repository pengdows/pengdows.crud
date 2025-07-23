#region

using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.exceptions;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TypeMapRegistryTests
{
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
        [PrimaryKey]
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
}