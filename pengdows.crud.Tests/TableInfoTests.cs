#region

using System;
using System.Data;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

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

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}