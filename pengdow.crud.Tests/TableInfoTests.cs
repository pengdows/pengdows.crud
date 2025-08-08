#region

using System;
using System.Data;
using pengdow.crud.attributes;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

public class TableInfoTests
{
    [Fact]
    public void TableInfo_HasExpectedColumns()
    {
        var registry = new TypeMapRegistry();
        registry.Register<SampleEntity>();

        var info = registry.GetTableInfo<SampleEntity>();
        Assert.NotNull(info);
        Assert.Contains("Id", info.Columns.Keys);
    }

    [Table("Sample")]
    private class SampleEntity
    {
        [Id] [Column("Id", DbType.Guid)] public Guid Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; }
    }
}