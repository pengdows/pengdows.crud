using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud;
using pengdows.crud.attributes;
using Xunit;

namespace pengdows.crud.Tests;

public class SingleIdGuardTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildRetrieve_NoIdColumn_Throws()
    {
        TypeMap.Register<CompositeEntity>();
        var helper = new EntityHelper<CompositeEntity, int>(Context);
        Assert.Throws<NotSupportedException>(() => helper.BuildRetrieve(new List<int> { 1 }));
    }

    [Fact]
    public void BuildRetrieve_WithIdColumn_Succeeds()
    {
        TypeMap.Register<TestEntity>();
        var helper = new EntityHelper<TestEntity, int>(Context);
        var sc = helper.BuildRetrieve(new List<int> { 1 });
        Assert.Contains("WHERE", sc.Query.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Table("Composite")]
    private class CompositeEntity
    {
        [PrimaryKey]
        [Column("Key1", DbType.Int32)]
        public int Key1 { get; set; }

        [PrimaryKey]
        [Column("Key2", DbType.Int32)]
        public int Key2 { get; set; }
    }
}
